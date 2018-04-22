
// TODO cancellation token
[CompilerGenerated]
class unprounouncable : IAsyncEnumerable, IAsyncEnumerator
{
    private static readonly Task<bool> s_falseTask = Task.FromResult(false);
    private static readonly Task<bool> s_trueTask = Task.FromResult(true);

    This savedThis; // saved copy of 'this'
    int state; // -1 means not-yet-started, -2 means finished, even values correspond to 'await' states and odd values correspond to 'yield' states
    bool HasValue => state & 1 != 0; // odd states have values from 'yield' 

    // awaiter and builder are used for 'await' in the method body
    TaskAwaiter awaiter; // thin wrapper around a Task (that we'll be waiting on) <-- Not sure why we need to store this
    AsyncTaskMethodBuilder builder; // used for getting a callback to MoveNext when async code completes  

    // current is used for 'yield' in the method body
    Value current; // only populated for states that have values

    // this code can probably be pulled into a base class (it's not specific to the async iterator method)
    Task<bool> WaitForNextAsync()
    {
        // throw if state has value is already true (you should call TryGetNext after WaitForNext, not twice in a row)

        // if we have a promise stored already (ie. the async code was started by last TryGetNext), then we will use it
        // if we don't have a promise stored already, then call MoveNext()
        // return a promise from MoveNext with a continuation to determine the bool (false if state==End) <--- tricky

        // (shortcut if promise already completed?)
    }

    // this code can probably be pulled into a base class (it's not specific to the async iterator method)
    Value TryGetNext(out bool success)
    {
        // throw if state == -1 (you should call WaitForNextAsync first, ie. when in starting state)

        // if we already have a value (ie. first call after WaitForNext), then return it <--- this is the tricky part
        // otherwise, call MoveNext
        //	if state=End, return nothing and false (state machine completed)
        //	if state has value, then return it and true
        //  if state doesn't have a value, return nothing and false. Async task started and promise stored for WaitForNext.

    }

    // MoveNext either sets a promise of a future value, or sets a state that has a value (with a value set)
    void MoveNext()
    {

        // await
        // get task and store it in awaiter (awaiter = task.GetAwaiter())
        // if not already completed:
        //	save locals,
        //	set state to an even value,
        //	tell builder that we want a MoveNext callback after task/awaiter completes (using AwaitUnsafeOnCompleted),
        //	return
		// note: this is exactly what the async state machine does today



        // yield
        //	set current 
        //  set state (odd value), 
		//  if someone is waiting for a value, complete promise 
        //  return

    }

    void INewInterface.SetStateMachine() { } // does nothing, not sure what SetStateMachine is for, in general...

    IAsyncEnumerable GetEnumerable()
    {
        // make a copy of this (or maybe return this when threads are helpful?)
        // do we initialize the builder here, or when unpronouncable is first constructed (in AsyncIterator method)?
    }
}


[DebuggerStepThrough, /*new*/ AsyncIteratorStateMachine(typeof(unpronouncable))] // not sure what this does
IAsyncEnumerable AsyncIterator()
{
    // create an unpronouncable
    // save this/locals
    // set its state to -1 (or is it -2 as with yield iterators?)
    // return the unpronouncable
}

void LoweredForeach()
{
    E e = e.GetAsyncEnumerator();
    while (await e.WaitForNextAsync()) /* outer loop */
    {
        while (true) /* inner loop */
        {
            V v = (V)e.TryGetNext(out bool success);
            if (!success) goto outer_loop_continue;

            // body
        }
        outer_loop_continue:;
    }
}

[DebuggerStepThrough, AsyncStateMachine(typeof(unpronouncable))] // not sure what this does
Task RegularAsyncMethod()
{
    // create an unpronouncable
    // save this/locals
    // set its state to -1
    // initialize its builder and start it (I think this will call MoveNext)
    // return the builder's Task (promise)
}

//IAsyncStateMachine has MoveNext and SetStateMachine(IAsyncStateMachine^)

async IAsyncEnumerable AyncIterator()
{
    // call to WaitForNext will start all the code until state 1
    Console.Write("abc");
    await Slow(); // state 0, automatic continuation to line below
    Console.Write("def");
    await Slow(); // state 2, automatic continuation to line below 
    Console.Write("ghi");
    yield return 42; // state 1, WaitForNext completes with true
                     // first call to TryGetNext returns true, remains in state 1
                     // second call to TryGetNext returns true, moved to state 3
    Console.Write("jkl");
    yield return 43; // state 3
                     // third call to TryGetNext returns false, moved to state 4
    Console.Write("mno");
    await Slow(); // state 4, automatic continuation to line below
                  // call to WaitForNext in parallel with code executing already
    Console.Write("pqr");
    await Slow(); // state 6, automatic continuation to line below
    Console.Write("stu");
    yield return 44; // state 5, WaitForNext completes with true 
                     // fourth call to TryGetNext returns true, remains in state 5
                     // fifth call to TryGetNext returns false, move to state -2 
                     // call to WaitForNext returns false
}

class Value { }
class This { }
