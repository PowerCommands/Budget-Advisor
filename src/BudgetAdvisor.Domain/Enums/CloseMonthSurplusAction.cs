namespace BudgetAdvisor.Domain.Enums;

public enum CloseMonthSurplusAction
{
    DoNothing = 1,
    RegisterExpense = 2,
    DepositToBankSavings = 3,
    DepositToFundSavings = 4
}
