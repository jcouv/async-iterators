using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

//class Doc
//{
//    async IAsyncEnumerable<int> Illustration()
//    {
//        // note: states are divided in two independent sequences: awaits (even), yields (odd)

//        // call to WaitForNext will start all the code until state 1
//        Console.Write("abc");
//        await Slow(); // state 0, machine automatically continues

//        Console.Write("def");
//        await Slow(); // state 2, machine automatically continues

//        Console.Write("ghi");
//        yield return 42; // state 1, promise of a value is fulfilled, WaitForNext completes with true, promise is left as signal of available value
//                         // first call to TryGetNext notices promise from running machine, clears it, returns value and true, moves to state 3
//                         // second call to TryGetNext prints "jkl", has no promise to fulfill, returns true, moves to state 3

//        Console.Write("jkl");
//        yield return 43; // state 3
//                         // third call to TryGetNext prints "mno", has no promise to fulfill, returns false, moves to state 4, lets machine run asynchronously

//        Console.Write("mno");
//        await Slow(); // state 4, machine automatically continues
//                      // call to WaitForNext in parallel with code executing already

//        Console.Write("pqr");
//        await Slow(); // state 6, machine automatically continues

//        Console.Write("stu");
//        yield return 44; // state 5, promise of a value is fulfilled, WaitForNext completes with true
//                         // fourth call to TryGetNext returns true, moves to state -2
//                         // fifth call to TryGetNext returns false
//                         // call to WaitForNext returns false
//    }

//    Task Slow() => throw null;
//}

class Program
{
    static async Task Main()
    {
        IAsyncEnumerable<int> test = AsyncIterator();
        IAsyncEnumerator<int> enumerable = test.GetAsyncEnumerator();

        while (await enumerable.WaitForNextAsync())
        {
            while (true)
            {
                int value = enumerable.TryGetNext(out bool success);
                if (!success) goto outer_loop_continue;

                Console.WriteLine(value);
            }
            outer_loop_continue:;
        }
    }

    static IAsyncEnumerable<int> AsyncIterator()
    {
        var stateMachine = new Unprounouncable();
        stateMachine.State = -1;
        stateMachine.Builder = AsyncTaskMethodBuilder<int>.Create();
        // note: we don't start the machine.
        return stateMachine;
    }

    // TODO cancellation token
    // TODO what happens to exceptions? (should they bubble into value promise, or somewhere else?)
    private sealed class Unprounouncable : CompilerImplementationDetails.AsyncIteratorBase<int>
    {
        // MoveNext returns with either:
        //  1. A promise of a future value (or finished state) that you can wait on
        //      - may already be completed (ie. reaching `yield` right from start)
        //      - may not be completed (ie. reaching an `await`)
        //  2. A state that has a value (or finished state). (no promise necessary since value available) (ie. `yield` after `yield`)
        //
        // await:
        // get task and store it in awaiter (awaiter = task.GetAwaiter())
        // if not already completed:
        //	save locals,
        //	set state to an even value,
        //  initialize the promise of a value (the caller of MoveNext will be interested in that notification)
        //	tell builder that we want a MoveNext callback after task/awaiter completes (using AwaitUnsafeOnCompleted),
        //	return
        // note: this is what the async state machine does today, except for initializing the promise of a value
        //
        // yield:
        // set current,
        // set state (odd value),
        // if someone was promised a value, complete promise with true (value found)
        // return
        //
        // end:
        // set state (finished)
        // if someone was promised a value, set it to done and false (finished state)
        // return
        public override void MoveNext()
        {
            switch (State)
            {
                case -1:
                    // yield return 42;
                    yieldReturn(42, state: 1);
                    return;
                case 1:
                    // await Slow();
                    awaitSlow(state: 2);
                    // note: the machine is running in another thread, so we need to be very careful to just let it run (don't look, don't touch)
                    return;
                case 2:
                    // yield return 43;
                    yieldReturn(43, state: 3);
                    return;
                case 3:
                    State = -2;
                    return;
            }

            void yieldReturn(int value, int state)
            {
                // yield:
                // set current,
                // set state (odd value),
                // if someone was promised a value, complete promise with true (value found)
                // return
                _current = value;

                Debug.Assert((state & 1) != 0); // odd states for 'yield'
                int previousState = State;
                State = state;

                if (_valueOrEndPromise != null)
                {
                    // reaching a `yield` following an `await`
                    _valueOrEndPromise.SetResult(true);
                }
                else if (previousState == -1)
                {
                    // reaching a `yield` from start
                    // If we came from the start, we'll pretend that the state machine ran.
                    // This way, the value will be properly yielded in the following TryGetNext
                    _valueOrEndPromise = CompilerImplementationDetails.s_completed;
                }
            }

            void awaitSlow(int state)
            {
                // await:
                // get task and store it in awaiter (awaiter = task.GetAwaiter())
                // if not already completed:
                //	save locals,
                //	set state to an even value,
                //  initialize the promise of a value (the caller of MoveNext will be interested in that notification)
                //	tell builder that we want a MoveNext callback after task/awaiter completes (using AwaitUnsafeOnCompleted),
                //	return
                // note: this is what the async state machine does today, except for initializing the promise of a value

                Task task = Task.Delay(100);
                this.Awaiter = task.GetAwaiter();

                Debug.Assert((state & 1) == 0); // even states for 'await'
                State = state;

                _valueOrEndPromise = new TaskCompletionSource<bool>(); // <--- Could we avoid allocation?

                var self = this;
                Builder.AwaitOnCompleted(ref Awaiter, ref self);
            }
        }
    }
}

public static class CompilerImplementationDetails
{
    // Is there a good place to store these?
    internal static readonly Task<bool> s_falseTask = Task.FromResult(false);
    internal static readonly Task<bool> s_trueTask = Task.FromResult(true);

    internal static TaskCompletionSource<bool> s_completed;

    static CompilerImplementationDetails()
    {
        s_completed = new TaskCompletionSource<bool>();
        s_completed.SetResult(true);
    }

    public abstract class AsyncIteratorBase<T> : IAsyncStateMachine, IAsyncEnumerable<T>, IAsyncEnumerator<T>
    {
        public abstract void MoveNext();

        public int State; // -1 means not-yet-started, -2 means finished, even values correspond to 'await' states and odd values correspond to 'yield' states
        private bool HasValue => State > 0 && (State & 1) != 0; // odd states have values from 'yield'

        // current is used for 'yield' in the method body
        protected T _current; // only populated for states that have values

        // awaiter and builder are used for 'await' in the method body
        public TaskAwaiter Awaiter; // thin wrapper around a Task (that we'll be waiting on) <-- Not sure why we need to store this
        public AsyncTaskMethodBuilder<int> Builder; // used for getting a callback to MoveNext when async code completes

        // If the promise is set, then don't check the machine state from code that isn't actively running the machine. The machine may be running on another thread.
        // The promise being set and completed signals there is a value that has not yet been yielded.
        protected TaskCompletionSource<bool> _valueOrEndPromise; // promise for a value or end (true means value found, false means finished state)

        // Contract: WaitForNextAsync will either return a false (no value left) or a true (found a value) with the promise set (to signal an unreturned value is stored).
        public Task<bool> WaitForNextAsync()
        {
            // if we have a promise stored already (ie. the async code was started by last TryGetNext), then we will use it
            // if we don't have a promise stored already, then call MoveNext()
            if (_valueOrEndPromise is null)
            {
                if (State == -2)
                {
                    return s_falseTask;
                }

                MoveNext();
                if (_valueOrEndPromise is null)
                {
                    if (State == -2)
                    {
                        return s_falseTask;
                    }
                    else if (HasValue)
                    {
                        return s_trueTask;
                    }
                }
            }

            return _valueOrEndPromise.Task;
        }

        public T TryGetNext(out bool success)
        {
            if (_valueOrEndPromise != null)
            {
                // if this is the first TryGetNext call after WaitForNext, then we'll return the value we already have
                _valueOrEndPromise = null;
            }
            else
            {
                // throw if state == -1 (you should call WaitForNextAsync first, ie. when in starting state)
                // throw if state doesn't have value
                if (State == -1 || !HasValue) throw new Exception("You should call WaitForNextAsync first");

                // otherwise, call MoveNext (to get a value or a promise of one)
                MoveNext();
            }

            // if we have a promise, no point in checking the state, MoveNext made the machine run. WaitForNext will pick up the result (no value available immediately)
            // if no promise, but reached last state, then no values to return (ever)
            if (_valueOrEndPromise != null || State == -2)
            {
                success = false;
                return default;
            }

            // if no promise, the machine is stopped and state has value, so return it (success)
            Debug.Assert(HasValue);
            success = true;
            return _current;
        }

        public void SetStateMachine(IAsyncStateMachine stateMachine) { }

        // TODO when can we re-use this, and when should we create a new instance?
        public IAsyncEnumerator<T> GetAsyncEnumerator() => this;
    }
}

public interface IAsyncEnumerable<out T>
{
    IAsyncEnumerator<T> GetAsyncEnumerator();
}

public interface IAsyncEnumerator<out T>
{
    Task<bool> WaitForNextAsync();
    T TryGetNext(out bool success);
}
