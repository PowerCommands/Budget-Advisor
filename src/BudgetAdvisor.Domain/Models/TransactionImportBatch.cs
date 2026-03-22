namespace BudgetAdvisor.Domain.Models;

public sealed class TransactionImportBatch
{
    public Guid ImportId { get; set; }

    public Guid? MemberId { get; set; }

    public DateTime ImportedAtUtc { get; set; }

    public int ImportedRecordCount { get; set; }
}
