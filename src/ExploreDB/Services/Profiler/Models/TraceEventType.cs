namespace ExploreDB.Services.Profiler.Models;

public enum TraceEventType
{
    SqlBatchCompleted,
    RpcCompleted,
    ErrorReported,
    Deadlock,
    Attention,
    Other
}
