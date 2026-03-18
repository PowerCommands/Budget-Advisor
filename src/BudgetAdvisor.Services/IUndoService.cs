namespace BudgetAdvisor.Services;

public interface IUndoService
{
    event Action? Changed;

    bool CanUndo { get; }

    void InitializeCurrentState(string json);

    void CaptureBeforeSave();

    void CommitSavedState(string json);

    string? ConsumeUndoSnapshot();
}
