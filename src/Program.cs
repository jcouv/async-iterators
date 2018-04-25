﻿using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

//class Doc
//{
//    async IAsyncEnumerable<int> Illustration()
//    {
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

        // TODO: do we need the inner loop?
        //foreach await (var value in enumerable)
        //{
        //    Console.WriteLine(value)
        //}
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
        stateMachine.State = -1; // TODO: should state be -2 ?
        stateMachine.Builder = AsyncTaskMethodBuilder<int>.Create();
        // note: we don't start the machine.
        return stateMachine;
    }

    // TODO cancellation token
    // TODO what happens to exceptions? (should they bubble into value promise, or somewhere else?)
    private sealed class Unprounouncable : CompilerImplementationDetails.AsyncIteratorBase<int>
    {
        // Constract: MoveNext returns with either:
        //  1. A promise of a future value (or finished state) that you can wait on
        //      - may already be completed (ie. reaching `yield` right from start)
        //      - may not be completed (ie. reaching an `await` that doesn't short-circuit). When the promise completes, the state machine will be stopped
        //          on the end state or a `yield` state.
        //  2. In end state
        //  3. With a current value. (no promise necessary since value available) (ie. `yield` after `yield`)
        //
        // await handshake:
        // get task and store it in awaiter (awaiter = task.GetAwaiter())
        // if the task is already completed, then just continue. Otherwise:
        //  initialize the promise of a value
        //	save locals, set state, tell builder that we want a MoveNext callback after task/awaiter completes (using AwaitUnsafeOnCompleted),
        //	return
        // note: this is what the async state machine does today, except for initializing the promise of a value
        //
        // yield handshake:
        // set current,
        // set state,
        // if someone was promised a value, complete promise with true (value found)
        // return
        //
        // end handshake:
        // set state (finished)
        // if someone was promised a value, set it to done and false (finished state)
        // return
        public override void MoveNext()
        {
            switch (State)
            {
                case -1:
                    // yield return 42;
                    yieldReturn(42, state: 2);
                    return;
                case 2:
                    // await Slow();
                    awaitSlow(state: 3);
                    // note: the machine is running in another thread, so we need to be very careful to just let it run (just return, don't touch anything)
                    return;
                case 3:
                    // yield return 43;
                    yieldReturn(43, state: 5);
                    return;
                case 5:
                    // yield return 44;
                    yieldReturn(44, state: 6);
                    return;
                case 6:
                    // await Done();
                    awaitDone(state: 7);
                    goto case 7;
                case 7:
                    // yield return 45;
                    yieldReturn(45, state: 9);
                    return;
                case 9:
                    // yield return 46;
                    yieldReturn(46, state: 10);
                    return;
                case 10:
                    end();
                    return;
            }

            // yield handshake:
            void yieldReturn(int value, int state)
            {
                _current = value;

                int previousState = State;
                State = state;

                if (_valueOrEndPromise != null)
                {
                    // reaching a `yield` following an `await`
                    _valueOrEndPromise.SetResult(true);
                }
                else if (previousState == -1)
                {
                    // TODO: what if we came from a short-circuited await?

                    // reaching a `yield` from start
                    // If we came from the start, we'll pretend that the state machine ran.
                    // This way, the value will be properly yielded in the following TryGetNext
                    _valueOrEndPromise = CompilerImplementationDetails.s_completed;
                }
            }

            // await handshake:
            void awaitSlow(int state)
            {
                Task task = Task.Delay(100);
                this.Awaiter = task.GetAwaiter();
                State = state;

                _valueOrEndPromise = new TaskCompletionSource<bool>(); // <--- Could we avoid allocation?

                var self = this;
                Builder.AwaitOnCompleted(ref Awaiter, ref self);
            }

            // await handshake (short-circuit):
            void awaitDone(int state)
            {
                Task task = Task.CompletedTask;
                State = state;
            }

            void end()
            {
                State = -2;
                if (_valueOrEndPromise != null)
                {
                    _valueOrEndPromise.SetResult(false);
                }
            }
        }
    }
}

public static class CompilerImplementationDetails
{
    // TODO: Is there a good place to store these?
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

        public int State; // -1 means not-yet-started, -2 means finished (TODO: confirm -1)

        protected T _current;

        // awaiter and builder are used for 'await' in the method body
        public TaskAwaiter Awaiter; // thin wrapper around a Task (that we'll be waiting on) <-- Not sure why we need to store this
        public AsyncTaskMethodBuilder<int> Builder; // used for getting a callback to MoveNext when async code completes

        // If the promise is set, then don't check the machine state from code that isn't actively running the machine. The machine may be running on another thread.
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
                // MoveNext always returns with a promise of a future value, reaching end state, or an immediately available value
                if (_valueOrEndPromise is null)
                {
                    if (State == -2)
                    {
                        return s_falseTask;
                    }

                    return s_trueTask;
                }
            }

            return _valueOrEndPromise.Task;
        }

        public T TryGetNext(out bool success)
        {
            if (_valueOrEndPromise != null)
            {
                // if this is the first TryGetNext call after WaitForNext, then we'll return the value we already have
                _valueOrEndPromise = null; // clear the promise, so that the next call to TryGetNext can move forward
            }
            else
            {
                // throw if state == -1 (you should call WaitForNextAsync first, ie. when in starting state)
                if (State == -1) throw new Exception("You should call WaitForNextAsync first");

                // otherwise, call MoveNext to get a value or a promise of one
                MoveNext();
                // MoveNext always returns with a promise of a future value, reaching end state, or an immediately available value
            }

            if (_valueOrEndPromise != null || State == -2)
            {
                success = false;
                return default;
            }

            // if no promise, the machine is stopped and state has value, so return it (success)
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

// -2 is "no state" (not started or already finished)
// -1 is initial state

// Way to explain feature:
//  General framing of async rewriting (code in state machine with handshakes)
//  Foreach pattern
//  Code example with annotations
//  Logic for WaitForNext, TryGetNext
//  Data stored in unpronouncable type
//  Contract of MoveNext


// TODO: How to test all the possible transitions?
// start
// await
// await-shortcut
// yield
// end

