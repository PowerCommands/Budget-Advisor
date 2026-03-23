using System.ComponentModel.DataAnnotations;

namespace BudgetAdvisor.App.Shared;

public sealed class SavingsAmountDialogModel
{
    [Required(ErrorMessage = "Account is required.")]
    public Guid? SavingsAccountId { get; set; }

    [Required(ErrorMessage = "Date is required.")]
    public DateTime? Date { get; set; }

    [Range(typeof(decimal), "0.01", "999999999", ErrorMessage = "Amount must be greater than zero.")]
    public decimal Amount { get; set; }

    public bool IsAdjustment { get; set; }
}
