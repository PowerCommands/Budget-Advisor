namespace BudgetAdvisor.Services;

public sealed class UndoService : IUndoService
{
    private string? _currentStateJson;
    private string? _undoSnapshotJson;

    public event Action? Changed;

    public bool CanUndo => !string.IsNullOrWhiteSpace(_undoSnapshotJson);

    public void InitializeCurrentState(string json)
    {
        _currentStateJson = json;
        _undoSnapshotJson = null;
        Changed?.Invoke();
    }

    public void CaptureBeforeSave()
    {
        _undoSnapshotJson = string.IsNullOrWhiteSpace(_currentStateJson) ? null : _currentStateJson;
        Changed?.Invoke();
    }

    public void CommitSavedState(string json)
    {
        _currentStateJson = json;
        Changed?.Invoke();
    }

    public string? ConsumeUndoSnapshot()
    {
        var snapshot = _undoSnapshotJson;
        _undoSnapshotJson = null;
        Changed?.Invoke();
        return snapshot;
    }
}
