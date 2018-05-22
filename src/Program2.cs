using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

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
        stateMachine.State = -1; // TODO: should state be -2 ? -1 may be "running"
        stateMachine.Builder = AsyncTaskMethodBuilder.Create();
        stateMachine._valueOrEndPromise = new ManualResetValueTaskSourceLogic<bool>(stateMachine);

        // note: we don't start the machine.
        return stateMachine;
    }

    // TODO what happens to exceptions? (should they bubble into value promise, or anywhere else?)
    private sealed class Unprounouncable :
        IAsyncStateMachine,
        IAsyncEnumerable<int>,
        IAsyncEnumerator<int>,
        IValueTaskSource<bool>, // used as the backing store behind the ValueTask<bool> returned from each MoveNextAsync
        IStrongBox<ManualResetValueTaskSourceLogic<bool>> // exposes its ValueTaskSource logic implementation
    {
        // If the promise is set (ie. _promiseIsActive is true), then don't check the machine state from code that isn't actively running the machine. The machine may be running on another thread.
        public int State; // -1 means not-yet-started, -2 means finished (TODO: confirm -1)

        // awaiter and builder are used for 'await' in the method body
        public TaskAwaiter Awaiter; // thin wrapper around a Task (that we'll be waiting on) <-- Not sure why we need to store this
        public AsyncTaskMethodBuilder Builder; // used for getting a callback to MoveNext when async code completes

        private int _current;

        #region promise
        /// <summary>All of the logic for managing the IValueTaskSource implementation.</summary>
        public ManualResetValueTaskSourceLogic<bool> _valueOrEndPromise; // promise for a value or end (true means value found, false means finished state)
        private bool _promiseIsActive = false; // this is spiritually equivalent to saying the promise is set versus null

        ref ManualResetValueTaskSourceLogic<bool> IStrongBox<ManualResetValueTaskSourceLogic<bool>>.Value
            => ref _valueOrEndPromise;
        ValueTaskSourceStatus IValueTaskSource<bool>.GetStatus(short token)
            => _valueOrEndPromise.GetStatus(token);
        void IValueTaskSource<bool>.OnCompleted(Action<object> continuation, object state, short token, ValueTaskSourceOnCompletedFlags flags)
            => _valueOrEndPromise.OnCompleted(continuation, state, token, flags);
        bool IValueTaskSource<bool>.GetResult(short token)
            => _valueOrEndPromise.GetResult(token);
        #endregion

        // Contract: MoveNext returns with either:
        //  1. A promise of a future value (or finished state) that you can wait on (ie. _promiseIsActive is true)
        //      - may already be completed (ie. reaching `yield` right from start)
        //      - may be pending (ie. reaching an `await` that doesn't short-circuit). When the promise completes, the state machine will be stopped
        //          on the end state or a `yield` state.
        //  2. In end state
        //  3. With a current value. (no promise necessary since value available) (ie. `yield` after `yield`)
        //
        // await handshake:
        // get task and store it in awaiter (awaiter = task.GetAwaiter())
        // if the task is already completed, then just continue (short-circuit). Otherwise:
        //  initialize the promise of a value (with _promiseIsActive = true, and _valueOrEndPromise.Reset())
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
        public void MoveNext()
        {
            switch (State)
            {
                case -1:
                    // yield return 42;
                    yieldReturn(42, state: 2);
                    return;
                case 2:
                    // await Slow();
                    awaitExpr(state: 3, fast: false);
                    // note: the machine is running in another thread, so we need to be very careful to just let it run (just return, don't touch anything)
                    return;
                case 3:
                    // await Slow();
                    awaitExpr(state: 4, fast: false);
                    // note: the machine is running in another thread, so we need to be very careful to just let it run (just return, don't touch anything)
                    return;
                case 4:
                    // yield return 43;
                    yieldReturn(43, state: 5);
                    return;
                case 5:
                    // yield return 44;
                    yieldReturn(44, state: 6);
                    return;
                case 6:
                    // await Done();
                    awaitExpr(state: 7, fast: true);
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

                if (this._promiseIsActive)
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
                    _promiseIsActive = true;
                    _valueOrEndPromise.SetResult(true);
                }
            }

            // await handshake:
            void awaitExpr(int state, bool fast)
            {
                Task task = fast ? Task.CompletedTask : Task.Delay(100);
                this.Awaiter = task.GetAwaiter();
                State = state;
                if (!task.IsCompleted)
                {
                    if (!this._promiseIsActive)
                    {
                        _promiseIsActive = true;
                        _valueOrEndPromise.Reset();
                    }
                    var self = this;
                    Builder.AwaitOnCompleted(ref Awaiter, ref self);
                }
            }

            void end()
            {
                State = -2;
                if (_promiseIsActive)
                {
                    _valueOrEndPromise.SetResult(false);
                }
            }
        }

        // PROTOTYPE(async-streams): update interface definition and async-foreach should recognize task-like in pattern
        // Contract: WaitForNextAsync will either return a false (no value left) or a true (found a value) with the promise set (to signal an unreturned value is stored).
        public ValueTask<bool> WaitForNextAsync()
        {
            // PROTOTYPE(async-streams)
            // if we have a pending promise already (ie. the async code was started by last TryGetNext), then we will use it
            // if we don't have a pending promise, then call MoveNext()
            if (!this._promiseIsActive)
            {
                // PROTOTYPE(async-streams): I don't think we need this check anymore. The state machine should SetResult(false) when reaching end state.
                if (State == -2)
                {
                    return new ValueTask<bool>(false);
                }

                MoveNext();
                // MoveNext always returns with a promise of a future value, reaching end state, or an immediately available value

                if (!this._promiseIsActive)
                {
                    return new ValueTask<bool>(State != 2);
                }
            }

            return new ValueTask<bool>(this, _valueOrEndPromise.Version);
        }

        /// <summary>
        /// You should only call this method once the promise from WaitForNextAsync completed with true.
        /// </summary>
        public int TryGetNext(out bool success)
        {
            if (_promiseIsActive)
            {
                // if this is the first TryGetNext call after WaitForNext, then we'll return the value we already have
                _promiseIsActive = false; // clear the promise, so that the next call to TryGetNext can move forward
            }
            else
            {
                // throw if state == -1 (you should call WaitForNextAsync first, ie. when in starting state)
                if (State == -1) throw new Exception("You should call WaitForNextAsync first");

                // otherwise, call MoveNext to get a value or a promise of one
                MoveNext();
                // MoveNext always returns with a promise of a future value, reaching end state, or an immediately available value
            }

            if (_promiseIsActive || State == -2)
            {
                success = false;
                return default;
            }

            // PROTOTYPE(async-streams): what if the promise is failed or cancelled?
            // the machine is stopped and state has value, so return it (success)
            success = true;
            return _current;
        }

        public void SetStateMachine(IAsyncStateMachine stateMachine) { }

        // TODO when can we re-use this, and when should we create a new instance?
        public IAsyncEnumerator<int> GetAsyncEnumerator()
            => this;
    }
}

// PROTOTYPE(async-streams): Figure how to get this type
namespace System.Runtime.CompilerServices
{
    public interface IStrongBox<T>
    {
        ref T Value { get; }
    }
}

// PROTOTYPE(async-streams): Figure how to get this type
public struct ManualResetValueTaskSourceLogic<TResult>
{
    private static readonly Action<object> s_sentinel = new Action<object>(s => throw new InvalidOperationException());

    private readonly IStrongBox<ManualResetValueTaskSourceLogic<TResult>> _parent;
    private Action<object> _continuation;
    private object _continuationState;
    private object _capturedContext;
    private ExecutionContext _executionContext;
    private bool _completed;
    private TResult _result;
    private ExceptionDispatchInfo _error;
    private short _version;

    public ManualResetValueTaskSourceLogic(IStrongBox<ManualResetValueTaskSourceLogic<TResult>> parent)
    {
        _parent = parent ?? throw new ArgumentNullException(nameof(parent));
        _continuation = null;
        _continuationState = null;
        _capturedContext = null;
        _executionContext = null;
        _completed = false;
        _result = default;
        _error = null;
        _version = 0;
    }

    public short Version => _version;

    private void ValidateToken(short token)
    {
        if (token != _version)
        {
            throw new InvalidOperationException();
        }
    }

    public ValueTaskSourceStatus GetStatus(short token)
    {
        ValidateToken(token);

        return
            !_completed ? ValueTaskSourceStatus.Pending :
            _error == null ? ValueTaskSourceStatus.Succeeded :
            _error.SourceException is OperationCanceledException ? ValueTaskSourceStatus.Canceled :
            ValueTaskSourceStatus.Faulted;
    }

    public TResult GetResult(short token)
    {
        ValidateToken(token);

        if (!_completed)
        {
            throw new InvalidOperationException();
        }

        _error?.Throw();
        return _result;
    }

    public void Reset()
    {
        _version++;

        _completed = false;
        _continuation = null;
        _continuationState = null;
        _result = default;
        _error = null;
        _executionContext = null;
        _capturedContext = null;
    }

    public void OnCompleted(Action<object> continuation, object state, short token, ValueTaskSourceOnCompletedFlags flags)
    {
        if (continuation == null)
        {
            throw new ArgumentNullException(nameof(continuation));
        }
        ValidateToken(token);

        if ((flags & ValueTaskSourceOnCompletedFlags.FlowExecutionContext) != 0)
        {
            _executionContext = ExecutionContext.Capture();
        }

        if ((flags & ValueTaskSourceOnCompletedFlags.UseSchedulingContext) != 0)
        {
            SynchronizationContext sc = SynchronizationContext.Current;
            if (sc != null && sc.GetType() != typeof(SynchronizationContext))
            {
                _capturedContext = sc;
            }
            else
            {
                TaskScheduler ts = TaskScheduler.Current;
                if (ts != TaskScheduler.Default)
                {
                    _capturedContext = ts;
                }
            }
        }

        _continuationState = state;
        if (Interlocked.CompareExchange(ref _continuation, continuation, null) != null)
        {
            _executionContext = null;

            object cc = _capturedContext;
            _capturedContext = null;

            switch (cc)
            {
                case null:
                    Task.Factory.StartNew(continuation, state, CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
                    break;

                case SynchronizationContext sc:
                    sc.Post(s =>
                    {
                        var tuple = (Tuple<Action<object>, object>)s;
                        tuple.Item1(tuple.Item2);
                    }, Tuple.Create(continuation, state));
                    break;

                case TaskScheduler ts:
                    Task.Factory.StartNew(continuation, state, CancellationToken.None, TaskCreationOptions.DenyChildAttach, ts);
                    break;
            }
        }
    }

    public void SetResult(TResult result)
    {
        _result = result;
        SignalCompletion();
    }

    public void SetException(Exception error)
    {
        _error = ExceptionDispatchInfo.Capture(error);
        SignalCompletion();
    }

    private void SignalCompletion()
    {
        if (_completed)
        {
            throw new InvalidOperationException();
        }
        _completed = true;

        if (Interlocked.CompareExchange(ref _continuation, s_sentinel, null) != null)
        {
            if (_executionContext != null)
            {
                ExecutionContext.Run(
                    _executionContext,
                    s => ((IStrongBox<ManualResetValueTaskSourceLogic<TResult>>)s).Value.InvokeContinuation(),
                    _parent ?? throw new InvalidOperationException());
            }
            else
            {
                InvokeContinuation();
            }
        }
    }

    private void InvokeContinuation()
    {
        object cc = _capturedContext;
        _capturedContext = null;

        switch (cc)
        {
            case null:
                _continuation(_continuationState);
                break;

            case SynchronizationContext sc:
                sc.Post(s =>
                {
                    ref ManualResetValueTaskSourceLogic<TResult> logicRef = ref ((IStrongBox<ManualResetValueTaskSourceLogic<TResult>>)s).Value;
                    logicRef._continuation(logicRef._continuationState);
                }, _parent ?? throw new InvalidOperationException());
                break;

            case TaskScheduler ts:
                Task.Factory.StartNew(_continuation, _continuationState, CancellationToken.None, TaskCreationOptions.DenyChildAttach, ts);
                break;
        }
    }
}
public interface IAsyncEnumerable<out T>
{
    IAsyncEnumerator<T> GetAsyncEnumerator();
}

public interface IAsyncEnumerator<out T>
{
    ValueTask<bool> WaitForNextAsync();
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

