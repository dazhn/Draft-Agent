namespace DraftAgent.Core;

public enum WorkflowLogLevel
{
    Info,
    Warning,
    Error
}

public sealed record WorkflowLogEntry(
    DateTimeOffset Timestamp,
    WorkflowLogLevel Level,
    string Step,
    string Operation,
    string Message,
    IReadOnlyDictionary<string, string> Data);

public interface IWorkflowLogger
{
    IReadOnlyList<WorkflowLogEntry> Entries { get; }

    void Info(string step, string operation, string message, IReadOnlyDictionary<string, string>? data = null);

    void Warning(string step, string operation, string message, IReadOnlyDictionary<string, string>? data = null);

    void Error(string step, string operation, string message, IReadOnlyDictionary<string, string>? data = null);
}

public sealed class InMemoryWorkflowLogger : IWorkflowLogger
{
    private readonly List<WorkflowLogEntry> _entries = [];

    public IReadOnlyList<WorkflowLogEntry> Entries => _entries.ToArray();

    public void Info(string step, string operation, string message, IReadOnlyDictionary<string, string>? data = null)
    {
        Write(WorkflowLogLevel.Info, step, operation, message, data);
    }

    public void Warning(string step, string operation, string message, IReadOnlyDictionary<string, string>? data = null)
    {
        Write(WorkflowLogLevel.Warning, step, operation, message, data);
    }

    public void Error(string step, string operation, string message, IReadOnlyDictionary<string, string>? data = null)
    {
        Write(WorkflowLogLevel.Error, step, operation, message, data);
    }

    private void Write(WorkflowLogLevel level, string step, string operation, string message, IReadOnlyDictionary<string, string>? data)
    {
        _entries.Add(new WorkflowLogEntry(
            DateTimeOffset.UtcNow,
            level,
            step,
            operation,
            message,
            data is null ? new Dictionary<string, string>() : new Dictionary<string, string>(data, StringComparer.Ordinal)));
    }
}
