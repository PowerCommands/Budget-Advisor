using BudgetAdvisor.Domain.Serialization;
using System.Text.Json.Serialization;

namespace BudgetAdvisor.Domain.Models;

[JsonConverter(typeof(IncomeEntryJsonConverter))]
public sealed class IncomeEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid MemberId { get; set; }

    public decimal Amount { get; set; }

    public int Year { get; set; }

    public int Month { get; set; }

    public DateOnly? TransactionDate { get; set; }

    public Guid? ImportId { get; set; }

    public int? ImportOccurrence { get; set; }

    public string Type { get; set; } = string.Empty;

    public string Metadata { get; set; } = string.Empty;

    public Guid? SavingsAccountId { get; set; }

    public Guid? AssetId { get; set; }

    public Guid? SeriesId { get; set; }
}
