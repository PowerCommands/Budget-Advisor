using BudgetAdvisor.Domain.Enums;
using BudgetAdvisor.Domain.Models;
using BudgetAdvisor.Services.Extensions;
using System.Globalization;

namespace BudgetAdvisor.Services;

public sealed class ApplicationState
{
    private const string ApplicationDataKey = "budget-advisor.application-data";

    private readonly LocalStorageService _localStorageService;

    public event Action? Changed;

    public ApplicationData Data { get; private set; } = new();

    public ApplicationState(LocalStorageService localStorageService)
    {
        _localStorageService = localStorageService;
    }

    public async Task InitializeAsync()
    {
        Data = await _localStorageService.LoadAsync<ApplicationData>(ApplicationDataKey) ?? new ApplicationData();
        NormalizeData();
    }

    public async Task AddMemberAsync(string name)
    {
        Data.Members.Add(new HouseholdMember { Name = name.Trim() });
        await PersistAndNotifyAsync();
    }

    public async Task AddOneTimeIncomeAsync(Guid memberId, decimal amount, int year, int month, IncomeType type)
    {
        Data.IncomeRecords.Add(new IncomeEntry
        {
            MemberId = memberId,
            Amount = amount,
            Year = year,
            Month = month,
            Type = type
        });

        await PersistAndNotifyAsync();
    }

    public async Task AddMonthlySalaryAsync(Guid memberId, decimal amount, int startYear, int startMonth, int endYear, int endMonth)
    {
        var seriesId = Guid.NewGuid();

        foreach (var month in EnumerateMonths(startYear, startMonth, endYear, endMonth))
        {
            Data.IncomeRecords.Add(new IncomeEntry
            {
                MemberId = memberId,
                Amount = amount,
                Year = month.Year,
                Month = month.Month,
                Type = IncomeType.Salary,
                SeriesId = seriesId
            });
        }

        await PersistAndNotifyAsync();
    }

    public async Task AddYearlyIncomeAsync(Guid memberId, decimal amount, int year, IncomeType type)
    {
        var seriesId = Guid.NewGuid();
        var distributedAmounts = DistributeAcrossMonths(amount);
        for (var month = 1; month <= 12; month++)
        {
            Data.IncomeRecords.Add(new IncomeEntry
            {
                MemberId = memberId,
                Amount = distributedAmounts[month - 1],
                Year = year,
                Month = month,
                Type = type,
                SeriesId = seriesId
            });
        }

        await PersistAndNotifyAsync();
    }

    public async Task AddOneTimeExpenseAsync(decimal amount, int year, int month, ExpenseCategory category, string subcategory, string description)
    {
        Data.ExpenseRecords.Add(new ExpenseEntry
        {
            Amount = amount,
            Year = year,
            Month = month,
            Category = category,
            Subcategory = subcategory.Trim(),
            Description = description.Trim()
        });

        await PersistAndNotifyAsync();
    }

    public async Task AddSubscriptionAsync(string name, decimal amount, int intervalMonths, int startYear, int startMonth, int? endYear, int? endMonth, ExpenseCategory category, string subcategory)
    {
        Data.Subscriptions.Add(new SubscriptionExpenseDefinition
        {
            Name = name.Trim(),
            Amount = amount,
            IntervalMonths = intervalMonths,
            StartYear = startYear,
            StartMonth = startMonth,
            EndYear = endYear,
            EndMonth = endMonth,
            Category = category,
            Subcategory = subcategory.Trim()
        });

        await PersistAndNotifyAsync();
    }

    public async Task<int> GenerateMissingSubscriptionExpensesAsync()
    {
        var generatedCount = 0;
        var generationLimit = GetGenerationLimit();

        foreach (var subscription in Data.Subscriptions)
        {
            var startDate = new DateOnly(subscription.StartYear, subscription.StartMonth, 1);
            var endDate = subscription.EndYear.HasValue && subscription.EndMonth.HasValue
                ? new DateOnly(subscription.EndYear.Value, subscription.EndMonth.Value, 1)
                : generationLimit;

            if (endDate > generationLimit)
            {
                endDate = generationLimit;
            }

            for (var current = startDate; current <= endDate; current = current.AddMonthsSafe(subscription.IntervalMonths))
            {
                var exists = Data.ExpenseRecords.Any(record =>
                    record.SubscriptionDefinitionId == subscription.Id &&
                    record.Year == current.Year &&
                    record.Month == current.Month);

                if (exists)
                {
                    continue;
                }

                Data.ExpenseRecords.Add(new ExpenseEntry
                {
                    Amount = subscription.Amount,
                    Year = current.Year,
                    Month = current.Month,
                    Category = subscription.Category,
                    Subcategory = subscription.Subcategory,
                    Description = subscription.Name,
                    SubscriptionDefinitionId = subscription.Id
                });

                generatedCount++;
            }
        }

        if (generatedCount > 0)
        {
            await PersistAndNotifyAsync();
        }

        return generatedCount;
    }

    public MonthlyOverview GetLatestMonthlyOverview()
    {
        var latest = GetLatestMonth();
        return GetMonthlyOverview(latest.Year, latest.Month);
    }

    public MonthlyOverview GetMonthlyOverview(int year, int month)
    {
        var income = Data.IncomeRecords.Where(entry => entry.Year == year && entry.Month == month).Sum(entry => entry.Amount);
        var expenses = Data.ExpenseRecords.Where(entry => entry.Year == year && entry.Month == month).ToList();

        return new MonthlyOverview
        {
            Year = year,
            Month = month,
            Income = income,
            Food = expenses.Where(entry => entry.Category == ExpenseCategory.Food).Sum(entry => entry.Amount),
            Clothing = expenses.Where(entry => entry.Category == ExpenseCategory.Clothing).Sum(entry => entry.Amount),
            Other = expenses.Where(entry => entry.Category == ExpenseCategory.Other).Sum(entry => entry.Amount)
        };
    }

    public IReadOnlyList<MonthlyReportRow> GetMonthlyReportRows()
    {
        var currentMonth = GetCurrentMonth().ToDateOnly();
        return GetMonthlyReportRows(currentMonth.AddMonths(-11), currentMonth);
    }

    public IReadOnlyList<MonthlyReportRow> GetMonthlyReportRows(DateOnly startDate, DateOnly endDate)
    {
        var normalizedStart = new DateOnly(startDate.Year, startDate.Month, 1);
        var normalizedEnd = new DateOnly(endDate.Year, endDate.Month, 1);

        if (normalizedEnd < normalizedStart)
        {
            (normalizedStart, normalizedEnd) = (normalizedEnd, normalizedStart);
        }

        var months = EnumerateMonths(normalizedStart.Year, normalizedStart.Month, normalizedEnd.Year, normalizedEnd.Month).ToList();
        if (months.Count == 0)
        {
            months.Add(GetCurrentMonth());
        }

        var balance = 0m;
        var rows = new List<MonthlyReportRow>();

        foreach (var month in months)
        {
            var overview = GetMonthlyOverview(month.Year, month.Month);
            balance += overview.NetChange;

            rows.Add(new MonthlyReportRow
            {
                Year = month.Year,
                Month = month.Month,
                Income = overview.Income,
                Expenses = overview.TotalExpenses,
                Change = overview.NetChange,
                Balance = balance
            });
        }

        return rows.OrderByDescending(row => row.Year).ThenByDescending(row => row.Month).ToList();
    }

    public async Task<string> ExportAsync() => await _localStorageService.BackupAsync("budget-advisor-backup.json", Data);

    public async Task ImportAsync(string json)
    {
        var data = await _localStorageService.RestoreAsync<ApplicationData>(json);
        if (data is null)
        {
            throw new InvalidOperationException("The backup file is invalid.");
        }

        Data = data;
        NormalizeData();
        await PersistAndNotifyAsync();
    }

    public async Task SetCurrencyAsync(string currencyCode)
    {
        var normalizedCode = currencyCode.Trim().ToUpperInvariant();
        var supportedCurrencies = new[] { "SEK", "USD", "EUR", "GBP" };

        if (!supportedCurrencies.Contains(normalizedCode, StringComparer.Ordinal))
        {
            return;
        }

        Data.CurrencyCode = normalizedCode;
        await PersistAndNotifyAsync();
    }

    public async Task SetRegionalSettingsAsync(string decimalSeparator, string thousandsSeparator)
    {
        if (decimalSeparator is not "." and not ",")
        {
            return;
        }

        var normalizedThousandsSeparator = thousandsSeparator switch
        {
            "." => ".",
            "," => ",",
            " " => " ",
            "" => "",
            _ => Data.ThousandsSeparator
        };

        if (normalizedThousandsSeparator == decimalSeparator)
        {
            return;
        }

        Data.DecimalSeparator = decimalSeparator;
        Data.ThousandsSeparator = normalizedThousandsSeparator;
        await PersistAndNotifyAsync();
    }

    public async Task SetThemeModeAsync(string themeMode)
    {
        var normalizedThemeMode = themeMode.Trim();
        var supportedThemeModes = new[] { "Primary", "Secondary", "Tertiary", "Dark" };

        if (!supportedThemeModes.Contains(normalizedThemeMode, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        Data.ThemeMode = supportedThemeModes.First(mode => mode.Equals(normalizedThemeMode, StringComparison.OrdinalIgnoreCase));
        await PersistAndNotifyAsync();
    }

    public async Task RemoveExpenseAsync(Guid expenseId)
    {
        Data.ExpenseRecords.RemoveAll(expense => expense.Id == expenseId);
        await PersistAndNotifyAsync();
    }

    public async Task RemoveSubscriptionAsync(Guid subscriptionId)
    {
        Data.Subscriptions.RemoveAll(subscription => subscription.Id == subscriptionId);
        Data.ExpenseRecords.RemoveAll(expense => expense.SubscriptionDefinitionId == subscriptionId);
        await PersistAndNotifyAsync();
    }

    public async Task RemoveIncomeAsync(Guid incomeId, bool removeSeries)
    {
        var income = Data.IncomeRecords.FirstOrDefault(entry => entry.Id == incomeId);
        if (income is null)
        {
            return;
        }

        if (removeSeries && income.SeriesId.HasValue)
        {
            Data.IncomeRecords.RemoveAll(entry => entry.SeriesId == income.SeriesId);
        }
        else
        {
            Data.IncomeRecords.RemoveAll(entry => entry.Id == incomeId);
        }

        await PersistAndNotifyAsync();
    }

    public string FormatCurrency(decimal amount)
    {
        return amount.ToString("C", CreateNumberFormat());
    }

    private async Task PersistAndNotifyAsync()
    {
        await _localStorageService.SaveAsync(ApplicationDataKey, Data);
        Changed?.Invoke();
    }

    private void NormalizeData()
    {
        if (string.IsNullOrWhiteSpace(Data.CurrencyCode))
        {
            Data.CurrencyCode = "SEK";
        }

        if (Data.DecimalSeparator is not "." and not ",")
        {
            Data.DecimalSeparator = ",";
        }

        Data.ThousandsSeparator = Data.ThousandsSeparator switch
        {
            "." => ".",
            "," => ",",
            "" => "",
            " " => " ",
            _ => " "
        };

        if (Data.ThousandsSeparator == Data.DecimalSeparator)
        {
            Data.ThousandsSeparator = Data.DecimalSeparator == "," ? " " : ",";
        }

        if (string.IsNullOrWhiteSpace(Data.ThemeMode))
        {
            Data.ThemeMode = "Primary";
        }
    }

    private NumberFormatInfo CreateNumberFormat()
    {
        var numberFormat = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
        numberFormat.CurrencySymbol = Data.CurrencyCode switch
        {
            "SEK" => "kr",
            "USD" => "$",
            "EUR" => "EUR",
            "GBP" => "GBP",
            _ => Data.CurrencyCode
        };
        numberFormat.CurrencyGroupSeparator = Data.ThousandsSeparator;
        numberFormat.CurrencyDecimalSeparator = Data.DecimalSeparator;
        numberFormat.CurrencyDecimalDigits = 2;
        numberFormat.CurrencyGroupSizes = [3];
        numberFormat.CurrencyPositivePattern = Data.CurrencyCode == "SEK" ? 3 : 0;
        numberFormat.CurrencyNegativePattern = Data.CurrencyCode == "SEK" ? 8 : 1;
        return numberFormat;
    }

    private static IEnumerable<MonthKey> EnumerateMonths(int startYear, int startMonth, int endYear, int endMonth)
    {
        var current = new DateOnly(startYear, startMonth, 1);
        var end = new DateOnly(endYear, endMonth, 1);

        while (current <= end)
        {
            yield return new MonthKey(current.Year, current.Month);
            current = current.AddMonthsSafe(1);
        }
    }

    private static IReadOnlyList<decimal> DistributeAcrossMonths(decimal yearlyAmount)
    {
        var baseAmount = Math.Round(yearlyAmount / 12m, 2, MidpointRounding.AwayFromZero);
        var distributed = Enumerable.Repeat(baseAmount, 12).ToArray();
        distributed[^1] += yearlyAmount - distributed.Sum();
        return distributed;
    }

    private MonthKey GetLatestMonth()
    {
        return GetAllMonths().Distinct().OrderByDescending(month => month.ToDateOnly()).FirstOrDefault(GetCurrentMonth());
    }

    private IEnumerable<MonthKey> GetAllMonths()
    {
        foreach (var income in Data.IncomeRecords)
        {
            yield return new MonthKey(income.Year, income.Month);
        }

        foreach (var expense in Data.ExpenseRecords)
        {
            yield return new MonthKey(expense.Year, expense.Month);
        }
    }

    private static MonthKey GetCurrentMonth()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return new MonthKey(today.Year, today.Month);
    }

    private static DateOnly GetGenerationLimit()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return new DateOnly(today.Year, today.Month, 1);
    }
}
