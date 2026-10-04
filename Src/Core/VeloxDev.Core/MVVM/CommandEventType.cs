namespace VeloxDev.MVVM;

/// <summary>The stage of a command execution that an event reports.</summary>
public enum CommandEventType : int
{
    None = 0,
    Created,   // Created
    Enqueued,  // Enqueued, waiting to run
    Dequeued,  // Dequeued, ready to execute
    Started,   // Execution actually started
    Completed, // Executed successfully
    Failed,    // Execution failed
    Canceled,  // Cancelled
    Exited     // Lifecycle ended
}
