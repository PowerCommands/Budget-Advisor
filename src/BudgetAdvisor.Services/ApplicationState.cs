using BudgetAdvisor.Domain.Enums;
using BudgetAdvisor.Domain.Models;
using BudgetAdvisor.Services.Extensions;
using System.Globalization;

using BudgetAdvisor.Domain.Theming;

namespace BudgetAdvisor.Services;

public sealed class ApplicationState
{
    public const string ApplicationDataKey = "budget-advisor.application-data";
    private const string SalaryIncomeType = "Salary";
    private const string TaxRefundIncomeType = "Tax refund";
    private const string InheritanceIncomeType = "Inheritance";
    private const string GiftIncomeType = "Gift";
    private const string OtherIncomeType = "Other";
    private const string InterestSubcategory = "Interest";
    private const string AmortizationSubcategory = "Amortization";
    private const string LeasingSubcategory = "Leasing";
    private const string AssetSaleSubcategory = "Asset Sale";
    private const string UnspecifiedSubcategory = "Unspecified";
    private const string CreditMetadata = "Credit";
    private const string VehicleAssetType = "Vehicle";
    private const string HousingAssetType = "Housing";
    private const string DefaultSavingsAccountName = "Savings";

    private readonly LocalStorageService _localStorageService;
    private readonly LocalizationService _localizer;
    private readonly IApplicationLogService _applicationLogService;
    private readonly IDataPruningService _dataPruningService;
    private readonly IUndoService _undoService;

    public event Action? Changed;

    public ApplicationData Data { get; private set; } = new();

    public ApplicationState(LocalStorageService localStorageService, LocalizationService localizer, IApplicationLogService applicationLogService, IDataPruningService dataPruningService, IUndoService undoService)
    {
        _localStorageService = localStorageService;
        _localizer = localizer;
        _applicationLogService = applicationLogService;
        _dataPruningService = dataPruningService;
        _undoService = undoService;
    }

    public async Task InitializeAsync()
    {
        var data = await _localStorageService.LoadAsync<ApplicationData>(ApplicationDataKey) ?? new ApplicationData();
        ApplyLoadedData(data, initializeUndoState: true);
    }

    public async Task ReloadFromStorageAsync()
    {
        var data = await _localStorageService.LoadAsync<ApplicationData>(ApplicationDataKey) ?? new ApplicationData();
        ApplyLoadedData(data, initializeUndoState: true);
        Changed?.Invoke();
    }

    public IReadOnlyList<ExpenseEntry> GetFilteredExpenseEntries(ExpenseTableFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        return ApplyExpenseTableFilter(GetScopedExpenseEntries(filter.Scope), filter, includeCategoryFilter: true, includeSubcategoryFilter: true)
            .OrderByDescending(entry => entry.Year)
            .ThenByDescending(entry => entry.Month)
            .ThenBy(entry => entry.Category)
            .ThenBy(entry => entry.Subcategory)
            .ThenBy(entry => entry.Description)
            .ToList();
    }

    public IReadOnlyList<ExpenseFilterCategoryOption> GetExpenseFilterCategoryOptions(ExpenseTableFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        return ApplyExpenseTableFilter(GetScopedExpenseEntries(filter.Scope), filter, includeCategoryFilter: false, includeSubcategoryFilter: true)
            .Select(entry => new ExpenseFilterCategoryOption
            {
                Value = entry.Category.ToString(),
                Category = entry.Category
            })
            .DistinctBy(option => option.Category)
            .OrderBy(option => option.Category)
            .ToList();
    }

    public IReadOnlyList<string> GetExpenseFilterSubcategoryOptions(ExpenseTableFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        return ApplyExpenseTableFilter(GetScopedExpenseEntries(filter.Scope), filter, includeCategoryFilter: true, includeSubcategoryFilter: false)
            .Select(entry => entry.Subcategory.Trim())
            .Where(subcategory => !string.IsNullOrWhiteSpace(subcategory))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(subcategory => subcategory, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task AddMemberAsync(string name)
    {
        var memberName = name.Trim();
        Data.Members.Add(new HouseholdMember { Name = memberName });
        await PersistAndNotifyAndLogAsync(
            BuildLogDescription(_localizer["nav.household"], _localizer["household.membersTab"]),
            BuildLogActivity("log.verb.added", "log.entity.member", memberName));
    }

    public async Task AddOneTimeIncomeAsync(Guid memberId, decimal amount, int year, int month, string type, string? metadata = null, Guid? savingsAccountId = null, Guid? assetId = null)
    {
        Data.IncomeRecords.Add(CreateOneTimeIncomeEntry(memberId, amount, year, month, NormalizeSystemIncomeType(type), metadata, savingsAccountId, assetId));

        await PersistAndNotifyAndLogAsync(
            BuildLogDescription(_localizer["nav.household"], _localizer["household.incomesTab"]),
            BuildLogActivity("log.verb.added", "log.entity.income", BuildIncomeLogSubject(memberId, type)));
    }

    public async Task AddMonthlySalaryAsync(Guid memberId, decimal amount, int startYear, int startMonth, int endYear, int endMonth)
    {
        var seriesId = Guid.NewGuid();
        var memberName = GetMember(memberId).Name;

        Data.SalaryIncomePeriods.Add(new SalaryIncomePeriod
        {
            SeriesId = seriesId,
            MemberId = memberId,
            MemberName = memberName,
            MonthlyAmount = amount,
            StartYear = startYear,
            StartMonth = startMonth,
            EndYear = endYear,
            EndMonth = endMonth
        });

        foreach (var month in EnumerateMonths(startYear, startMonth, endYear, endMonth))
        {
            Data.IncomeRecords.Add(new IncomeEntry
            {
                MemberId = memberId,
                Amount = amount,
                Year = month.Year,
                Month = month.Month,
                Type = SalaryIncomeType,
                SeriesId = seriesId
            });
        }

        await PersistAndNotifyAndLogAsync(
            BuildLogDescription(_localizer["nav.household"], _localizer["household.salaryTab"]),
            BuildLogActivity("log.verb.added", "log.entity.salaryPeriod", memberName));
    }

    public async Task AddYearlyIncomeAsync(Guid memberId, decimal amount, int year, string type)
    {
        var seriesId = Guid.NewGuid();
        var normalizedType = NormalizeSystemIncomeType(type);
        var distributedAmounts = DistributeAcrossMonths(amount);

        if (string.Equals(normalizedType, SalaryIncomeType, StringComparison.OrdinalIgnoreCase))
        {
            Data.SalaryIncomePeriods.Add(new SalaryIncomePeriod
            {
                SeriesId = seriesId,
                MemberId = memberId,
                MemberName = GetMember(memberId).Name,
                MonthlyAmount = distributedAmounts[0],
                StartYear = year,
                StartMonth = 1,
                EndYear = year,
                EndMonth = 12
            });
        }

        for (var month = 1; month <= 12; month++)
        {
            Data.IncomeRecords.Add(new IncomeEntry
            {
                MemberId = memberId,
                Amount = distributedAmounts[month - 1],
                Year = year,
                Month = month,
                Type = normalizedType,
                SeriesId = seriesId
            });
        }

        await PersistAndNotifyAndLogAsync(
            BuildLogDescription(_localizer["nav.household"], _localizer["household.salaryTab"]),
            BuildLogActivity("log.verb.added", "log.entity.salaryPeriod", GetMember(memberId).Name));
    }

    public async Task<bool> UpdateIncomeAmountAsync(Guid incomeId, decimal amount)
    {
        if (amount <= 0m)
        {
            throw new InvalidOperationException("Income amount must be greater than zero.");
        }

        var income = Data.IncomeRecords.FirstOrDefault(entry => entry.Id == incomeId);
        if (income is null)
        {
            return false;
        }

        if (income.Amount == amount)
        {
            return false;
        }

        income.Amount = amount;

        if (income.AssetId.HasValue)
        {
            var asset = Data.Assets.FirstOrDefault(item => item.Id == income.AssetId.Value && item.SaleIncomeEntryId == income.Id);
            if (asset is not null)
            {
                asset.SaleAmount = amount;
            }
        }

        if (income.SavingsAccountId.HasValue)
        {
            await RecalculateSavingsBalancesAsync();
            await LogActivityAsync(
                BuildLogDescription(_localizer["nav.household"], _localizer["household.incomesTab"]),
                BuildLogActivity("log.verb.updated", "log.entity.income", BuildIncomeLogSubject(income.MemberId, income.Type)));
            return true;
        }

        await PersistAndNotifyAndLogAsync(
            BuildLogDescription(_localizer["nav.household"], _localizer["household.incomesTab"]),
            BuildLogActivity("log.verb.updated", "log.entity.income", BuildIncomeLogSubject(income.MemberId, income.Type)));
        return true;
    }

    public async Task AddOneTimeExpenseAsync(decimal amount, int year, int month, ExpenseCategory category, string subcategory, string description, string? metadata = null, Guid? transportVehicleId = null, Guid? savingsAccountId = null, Guid? assetId = null)
    {
        _ = AddOneTimeExpenseEntry(amount, year, month, category, subcategory, description, metadata, transportVehicleId, savingsAccountId, assetId);

        await PersistAndNotifyAndLogAsync(
            BuildLogDescription(_localizer["nav.expenses"], _localizer["expenses.expensesTab"]),
            BuildLogActivity("log.verb.added", "log.entity.expense", GetExpenseActivitySubject(subcategory, description)));
    }

    public async Task ImportExpenseEntriesAsync(IEnumerable<ImportedExpenseDraft> drafts)
    {
        ArgumentNullException.ThrowIfNull(drafts);

        foreach (var draft in drafts)
        {
            await ImportExpenseEntryAsync(draft);
        }
    }

    public async Task ImportExpenseEntryAsync(ImportedExpenseDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (draft.IsRecurring && draft.EndDate.HasValue)
        {
            var startDate = draft.StartDate ?? draft.Date;
            Data.Subscriptions.Add(new SubscriptionExpenseDefinition
            {
                Name = draft.Description.Trim(),
                Amount = draft.Amount,
                IntervalMonths = 1,
                StartYear = startDate.Year,
                StartMonth = startDate.Month,
                EndYear = draft.EndDate.Value.Year,
                EndMonth = draft.EndDate.Value.Month,
                Category = draft.Category,
                Subcategory = draft.Subcategory.Trim()
            });

            await RegenerateSubscriptionExpensesAsync();
            await LogActivityAsync(
                BuildLogDescription(_localizer["nav.expenses"], _localizer["expenses.subscriptionAndDefinitions"]),
                BuildLogActivity("log.verb.added", "log.entity.recurringExpense", draft.Description));
            return;
        }

        Data.ExpenseRecords.Add(CreateOneTimeExpenseEntry(
            draft.Amount,
            draft.Date.Year,
            draft.Date.Month,
            draft.Category,
            draft.Subcategory,
            draft.Description,
            null,
            null,
            null,
            null));

        await PersistAndNotifyAndLogAsync(
            BuildLogDescription(_localizer["nav.expenses"], _localizer["expenses.expensesTab"]),
            BuildLogActivity("log.verb.imported", "log.entity.expense", draft.Description));
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

        await RegenerateSubscriptionExpensesAsync();
        await LogActivityAsync(
            BuildLogDescription(_localizer["nav.expenses"], _localizer["expenses.subscriptionAndDefinitions"]),
            BuildLogActivity("log.verb.added", "log.entity.recurringExpense", name));
    }

    public async Task AddHousingDefinitionAsync(decimal amount, int intervalMonths, int startYear, int startMonth, int endYear, int endMonth, string subcategory, string description)
    {
        var definition = new HousingCostDefinition
        {
            Amount = amount,
            IntervalMonths = intervalMonths,
            StartYear = startYear,
            StartMonth = startMonth,
            EndYear = endYear,
            EndMonth = endMonth,
            Description = description.Trim(),
            Subcategory = subcategory.Trim()
        };

        Data.HousingDefinitions.Add(definition);

        foreach (var occurrence in EnumerateOccurrences(startYear, startMonth, endYear, endMonth, intervalMonths))
        {
            Data.ExpenseRecords.Add(new ExpenseEntry
            {
                Amount = amount,
                Year = occurrence.Year,
                Month = occurrence.Month,
                Category = ExpenseCategory.Housing,
                Subcategory = definition.Subcategory,
                Description = definition.Description,
                HousingDefinitionId = definition.Id
            });
        }

        await PersistAndNotifyAndLogAsync(
            BuildLogDescription(_localizer["nav.housingTab"], _localizer["housing.recurringCostsTab"]),
            BuildLogActivity("log.verb.added", "log.entity.housingDefinition", definition.Description));
    }

    public async Task AddTransportDefinitionAsync(decimal amount, int intervalMonths, int startYear, int startMonth, int endYear, int endMonth, string subcategory, string description, Guid vehicleId)
    {
        var vehicle = GetTransportVehicle(vehicleId);
        var definition = new TransportCostDefinition
        {
            VehicleId = vehicleId,
            Amount = amount,
            IntervalMonths = intervalMonths,
            StartYear = startYear,
            StartMonth = startMonth,
            EndYear = endYear,
            EndMonth = endMonth,
            Description = description.Trim(),
            Subcategory = subcategory.Trim()
        };

        Data.TransportDefinitions.Add(definition);

        foreach (var occurrence in EnumerateOccurrences(startYear, startMonth, endYear, endMonth, intervalMonths))
        {
            Data.ExpenseRecords.Add(new ExpenseEntry
            {
                Amount = amount,
                Year = occurrence.Year,
                Month = occurrence.Month,
                Category = ExpenseCategory.Transport,
                Subcategory = definition.Subcategory,
                Metadata = vehicle.Name.Trim(),
                Description = definition.Description,
                TransportDefinitionId = definition.Id,
                TransportVehicleId = vehicleId
            });
        }

        await PersistAndNotifyAndLogAsync(
            BuildLogDescription(_localizer["nav.transport"], _localizer["transport.recurringCostsTab"]),
            BuildLogActivity("log.verb.added", "log.entity.transportDefinition", string.IsNullOrWhiteSpace(definition.Description) ? vehicle.Name : definition.Description));
    }

    public async Task AddHousingLoanAsync(string name, string lender, DateOnly loanStartDate, decimal initialLoanAmount, decimal remainingDebt)
    {
        var loan = new HousingLoan
        {
            Name = name.Trim(),
            Lender = lender.Trim(),
            LoanStartDate = loanStartDate,
            InitialLoanAmount = initialLoanAmount,
            StartingRemainingDebt = remainingDebt,
            CurrentAmortization = 0m
        };

        Data.HousingLoans.Add(loan);
        SetMonthlyBalance(loan.Id, NormalizeMonth(loanStartDate), remainingDebt);

        await PersistAndNotifyAndLogAsync(
            BuildLogDescription(_localizer["nav.housingTab"], _localizer["housing.loansTab"]),
            BuildLogActivity("log.verb.added", "log.entity.housingLoan", name));
    }

    public async Task AddLoanInterestBindingPeriodAsync(Guid loanId, DateOnly startDate, DateOnly endDate, decimal interestRate, decimal monthlyAmortization)
    {
        if (interestRate < 0m)
        {
            throw new InvalidOperationException("Interest rate must not be negative.");
        }

        ValidateAnyLoanExists(loanId);
        ValidateNoMonthOverlap(
            startDate,
            endDate,
            Data.LoanInterestBindingPeriods.Where(period => period.LoanId == loanId).Select(period => (period.StartMonth, period.EndMonth)));

        Data.LoanInterestBindingPeriods.Add(new LoanInterestBindingPeriod
        {
            LoanId = loanId,
            StartDate = NormalizeMonth(startDate),
            EndDate = NormalizeMonth(endDate),
            StartMonth = NormalizeMonth(startDate),
            EndMonth = NormalizeMonth(endDate),
            InterestRate = interestRate,
            MonthlyAmortization = monthlyAmortization
        });

        await CalculateLoanCategoryCostsAsync(loanId, NormalizeMonth(startDate), NormalizeMonth(endDate));
        await LogActivityAsync(
            BuildLogDescription(
                Data.HousingLoans.Any(loan => loan.Id == loanId) ? _localizer["nav.housingTab"] : _localizer["nav.transport"],
                Data.HousingLoans.Any(loan => loan.Id == loanId) ? _localizer["housing.loansTab"] : _localizer["transport.loansTab"]),
            BuildLogActivity("log.verb.added", "log.entity.bindingPeriod", FormatMonthRange(startDate, endDate)));
    }

    public async Task AddLoanAmortizationPlanAsync(Guid loanId, DateOnly startDate, DateOnly endDate, decimal monthlyAmortizationAmount)
    {
        ValidateAnyLoanExists(loanId);
        ValidateNoMonthOverlap(
            startDate,
            endDate,
            Data.LoanAmortizationPlans.Where(plan => plan.LoanId == loanId).Select(plan => (plan.StartDate, plan.EndDate)));

        Data.LoanAmortizationPlans.Add(new LoanAmortizationPlan
        {
            LoanId = loanId,
            StartDate = startDate,
            EndDate = endDate,
            MonthlyAmortizationAmount = monthlyAmortizationAmount
        });

        await RecalculateLoanCategoryCostsAsync(loanId);
        await LogActivityAsync(
            BuildLogDescription(
                Data.HousingLoans.Any(loan => loan.Id == loanId) ? _localizer["nav.housingTab"] : _localizer["nav.transport"],
                Data.HousingLoans.Any(loan => loan.Id == loanId) ? _localizer["housing.loansTab"] : _localizer["transport.loansTab"]),
            BuildLogActivity("log.verb.added", "log.entity.amortizationPlan", FormatMonthRange(startDate, endDate)));
    }

    public async Task<int> BackfillLoanExpensesAsync(Guid loanId, DateOnly startDate, DateOnly endDate, decimal monthlyAmortizationAmount, decimal monthlyInterestAmount)
    {
        ValidateAnyLoanExists(loanId);

        if (monthlyAmortizationAmount <= 0m && monthlyInterestAmount <= 0m)
        {
            throw new InvalidOperationException("At least one monthly amount must be greater than zero.");
        }

        var normalizedStart = NormalizeMonth(startDate);
        var normalizedEnd = NormalizeMonth(endDate);
        if (normalizedEnd < normalizedStart)
        {
            (normalizedStart, normalizedEnd) = (normalizedEnd, normalizedStart);
        }

        var isHousingLoan = Data.HousingLoans.Any(loan => loan.Id == loanId);
        var category = isHousingLoan ? ExpenseCategory.Housing : ExpenseCategory.Transport;
        var lender = Data.HousingLoans.FirstOrDefault(loan => loan.Id == loanId)?.Lender
            ?? Data.TransportLoans.First(loan => loan.Id == loanId).Lender;
        var vehicleId = Data.TransportLoans.FirstOrDefault(loan => loan.Id == loanId)?.VehicleId;
        var metadata = category == ExpenseCategory.Transport ? GetTransportVehicleName(vehicleId) : null;

        Data.ExpenseRecords.RemoveAll(expense =>
            expense.LoanId == loanId &&
            expense.LoanBalanceNeutral &&
            expense.Subcategory is AmortizationSubcategory or InterestSubcategory &&
            NormalizeMonth(new DateOnly(expense.Year, expense.Month, 1)) >= normalizedStart &&
            NormalizeMonth(new DateOnly(expense.Year, expense.Month, 1)) <= normalizedEnd);

        var createdCount = 0;

        foreach (var month in EnumerateMonths(normalizedStart.Year, normalizedStart.Month, normalizedEnd.Year, normalizedEnd.Month))
        {
            if (monthlyAmortizationAmount > 0m)
            {
                Data.ExpenseRecords.Add(CreateLoanExpenseEntry(
                    loanId,
                    lender,
                    category,
                    month,
                    AmortizationSubcategory,
                    monthlyAmortizationAmount,
                    null,
                    null,
                    metadata,
                    vehicleId,
                    loanBalanceNeutral: true));
                createdCount++;
            }

            if (monthlyInterestAmount > 0m)
            {
                Data.ExpenseRecords.Add(CreateLoanExpenseEntry(
                    loanId,
                    lender,
                    category,
                    month,
                    InterestSubcategory,
                    monthlyInterestAmount,
                    null,
                    null,
                    metadata,
                    vehicleId,
                    loanBalanceNeutral: true));
                createdCount++;
            }
        }

        await PersistAndNotifyAndLogAsync(
            BuildLogDescription(
                isHousingLoan ? _localizer["nav.housingTab"] : _localizer["nav.transport"],
                isHousingLoan ? _localizer["housing.loansTab"] : _localizer["transport.loansTab"]),
            BuildLogActivity("log.verb.registered", "log.entity.expense", $"{_localizer["loans.backfillActionLabel"]} {FormatMonthRange(normalizedStart, normalizedEnd)}"));

        return createdCount;
    }

    public async Task AddHousingLoanAmortizationAsync(Guid loanId, DateOnly month, decimal monthlyAmortizationAmount)
    {
        var normalizedMonth = NormalizeMonth(month);
        await AddLoanAmortizationPlanAsync(loanId, normalizedMonth, normalizedMonth, monthlyAmortizationAmount);
    }

    public async Task UpdateHousingLoanInterestBindingPeriodAsync(Guid periodId)
    {
        var period = Data.LoanInterestBindingPeriods.FirstOrDefault(item => item.Id == periodId);
        if (period is null)
        {
            return;
        }

        var effectiveStartDate = NormalizeMonth(DateOnly.FromDateTime(DateTime.Today));
        if (effectiveStartDate < NormalizeMonth(period.StartMonth))
        {
            effectiveStartDate = NormalizeMonth(period.StartMonth);
        }

        await CalculateHousingLoanCostsAsync(effectiveStartDate, NormalizeMonth(period.EndMonth));
    }

    public async Task AddTransportLoanAsync(Guid vehicleId, string lender, DateOnly loanStartDate, decimal initialLoanAmount, decimal remainingDebt)
    {
        _ = GetTransportVehicle(vehicleId);

        var loan = new TransportLoan
        {
            VehicleId = vehicleId,
            Lender = lender.Trim(),
            LoanStartDate = loanStartDate,
            InitialLoanAmount = initialLoanAmount,
            StartingRemainingDebt = remainingDebt,
            CurrentAmortization = 0m
        };

        Data.TransportLoans.Add(loan);
        SetMonthlyBalance(loan.Id, NormalizeMonth(loanStartDate), remainingDebt);

        await PersistAndNotifyAndLogAsync(
            BuildLogDescription(_localizer["nav.transport"], _localizer["transport.loansTab"]),
            BuildLogActivity("log.verb.added", "log.entity.transportLoan", lender));
    }

    public async Task AddTransportLeasingContractAsync(Guid vehicleId, DateOnly startDate, DateOnly endDate, decimal monthlyCost)
    {
        _ = GetTransportVehicle(vehicleId);

        ValidateNoMonthOverlap(
            startDate,
            endDate,
            Data.TransportLeasingContracts.Select(contract => (contract.StartDate, contract.EndDate)));

        Data.TransportLeasingContracts.Add(new TransportLeasingContract
        {
            VehicleId = vehicleId,
            StartDate = startDate,
            EndDate = endDate,
            MonthlyCost = monthlyCost
        });

        await RecalculateTransportCostsAsync();
        await LogActivityAsync(
            BuildLogDescription(_localizer["nav.transport"], _localizer["transport.leasingTab"]),
            BuildLogActivity("log.verb.added", "log.entity.leasingContract", GetTransportVehicle(vehicleId).Name));
    }

    public async Task AddTransportVehicleAsync(
        string name,
        int modelYear,
        int mileage,
        VehicleFuelType fuelType,
        TransportVehicleType vehicleType,
        VehicleOwnershipType ownershipType,
        decimal? purchasePrice,
        decimal? estimatedSaleValue)
    {
        var vehicle = new TransportVehicle
        {
            Name = name.Trim(),
            ModelYear = modelYear,
            Mileage = mileage,
            FuelType = fuelType,
            VehicleType = vehicleType,
            OwnershipType = ownershipType,
            PurchasePrice = ownershipType == VehicleOwnershipType.Private ? purchasePrice : null,
            EstimatedSaleValue = ownershipType == VehicleOwnershipType.Private ? estimatedSaleValue : null
        };

        if (vehicle.OwnershipType == VehicleOwnershipType.Private)
        {
            var asset = CreateTransportVehicleAsset(vehicle);
            Data.Assets.Add(asset);
            vehicle.AssetId = asset.Id;
        }

        Data.TransportVehicles.Add(vehicle);
        await PersistAndNotifyAndLogAsync(
            BuildLogDescription(_localizer["nav.transport"], _localizer["transport.vehiclesTab"]),
            BuildLogActivity("log.verb.added", "log.entity.vehicle", vehicle.Name));
    }

    public async Task AddSavingsAccountAsync(
        SavingsAccountType accountType,
        string providerName,
        string accountName,
        decimal currentBalance,
        DateOnly currentPeriod,
        bool backfillDeposits = false,
        DateOnly? backfillStartPeriod = null,
        decimal? backfillStartBalance = null)
    {
        var normalizedCurrentPeriod = NormalizeMonth(currentPeriod);
        var normalizedCreatedMonth = backfillDeposits
            ? NormalizeMonth(backfillStartPeriod ?? currentPeriod)
            : normalizedCurrentPeriod;
        var normalizedBackfillStartBalance = backfillDeposits
            ? Math.Max(0m, backfillStartBalance ?? 0m)
            : currentBalance;

        if (normalizedCreatedMonth > normalizedCurrentPeriod)
        {
            throw new InvalidOperationException("Backfill start period cannot be later than the current period.");
        }

        if (backfillDeposits && normalizedBackfillStartBalance > currentBalance)
        {
            throw new InvalidOperationException("Backfill start balance cannot exceed the current balance.");
        }

        if (backfillDeposits &&
            normalizedCreatedMonth == normalizedCurrentPeriod &&
            normalizedBackfillStartBalance != currentBalance)
        {
            throw new InvalidOperationException("Start balance must match current balance when the backfill period is the same month.");
        }

        var account = new SavingsAccount
        {
            AccountType = accountType,
            ProviderName = providerName.Trim(),
            AccountName = accountName.Trim(),
            CreatedDate = normalizedCreatedMonth,
            OpeningBalance = backfillDeposits ? normalizedBackfillStartBalance : currentBalance
        };

        Data.SavingsAccounts.Add(account);

        if (!backfillDeposits)
        {
            SetMonthlyBalance(account.Id, normalizedCurrentPeriod, currentBalance);
            await PersistAndNotifyAndLogAsync(
                BuildLogDescription(_localizer["nav.savings"], GetSavingsTabTitle(accountType)),
                BuildLogActivity("log.verb.added", "log.entity.savingsAccount", accountName));
            return;
        }

        SetMonthlyBalance(account.Id, normalizedCreatedMonth, normalizedBackfillStartBalance);
        await PersistAndNotifyAsync();

        var amountToBackfill = currentBalance - normalizedBackfillStartBalance;
        if (amountToBackfill > 0m)
        {
            var distributionStartMonth = normalizedCreatedMonth.AddMonths(1);
            var months = distributionStartMonth > normalizedCurrentPeriod
                ? []
                : EnumerateMonths(
                    distributionStartMonth.Year,
                    distributionStartMonth.Month,
                    normalizedCurrentPeriod.Year,
                    normalizedCurrentPeriod.Month)
                    .Select(month => month.ToDateOnly())
                    .ToList();

            if (months.Count == 0)
            {
                throw new InvalidOperationException("At least one later month is required to distribute the balance difference.");
            }

            var distributedAmounts = DistributeEvenlyAcrossPeriods(amountToBackfill, months.Count);
            for (var index = 0; index < months.Count; index++)
            {
                if (distributedAmounts[index] <= 0m)
                {
                    continue;
                }

                await AddSavingsDepositAsync(account.Id, months[index], distributedAmounts[index]);
            }
        }

        await LogActivityAsync(
            BuildLogDescription(_localizer["nav.savings"], GetSavingsTabTitle(accountType)),
            BuildLogActivity("log.verb.added", "log.entity.savingsAccount", accountName));
    }

    public async Task AddSavingsReturnPeriodAsync(Guid accountId, DateOnly startDate, DateOnly endDate, decimal ratePercent, decimal taxPercent)
    {
        var account = GetSavingsAccount(accountId);
        if (account.AccountType == SavingsAccountType.Bank && ratePercent < 0m)
        {
            throw new InvalidOperationException("Bank interest rate must not be negative.");
        }

        if (taxPercent < 0m || taxPercent > 100m)
        {
            throw new InvalidOperationException("Tax percent must be between 0 and 100.");
        }

        ValidateNoMonthOverlap(
            startDate,
            endDate,
            Data.SavingsReturnPeriods.Where(period => period.SavingsAccountId == accountId).Select(period => (period.StartDate, period.EndDate)));

        Data.SavingsReturnPeriods.Add(new SavingsReturnPeriod
        {
            SavingsAccountId = accountId,
            StartDate = startDate,
            EndDate = endDate,
            RatePercent = ratePercent,
            TaxPercent = account.AccountType == SavingsAccountType.Bank ? taxPercent : 0m
        });

        await RecalculateSavingsAccountBalancesAsync(accountId, NormalizeMonth(startDate));
        await LogActivityAsync(
            BuildLogDescription(_localizer["nav.savings"], GetSavingsTabTitle(account.AccountType)),
            BuildLogActivity("log.verb.added", "log.entity.savingsPeriod", account.AccountName));
    }

    public async Task AddSavingsDepositAsync(Guid accountId, DateOnly date, decimal amount, bool isAdjustment = false)
    {
        var account = GetSavingsAccount(accountId);
        if (amount <= 0m)
        {
            throw new InvalidOperationException("Deposit amount must be greater than zero.");
        }

        if (NormalizeMonth(date) < NormalizeMonth(account.CreatedDate))
        {
            throw new InvalidOperationException("Deposit date cannot be earlier than the account start month.");
        }

        if (isAdjustment)
        {
            await AddSavingsBalanceAdjustmentAsync(account, date, amount);
            return;
        }

        _ = AddSavingsDepositExpenseEntry(account, date, amount);

        await RecalculateSavingsAccountBalancesAsync(account.Id, NormalizeMonth(date));
        await LogActivityAsync(
            BuildLogDescription(_localizer["nav.savings"], GetSavingsTabTitle(account.AccountType)),
            BuildLogActivity("log.verb.registered", "log.entity.deposit", account.AccountName));
    }

    public async Task AddSavingsWithdrawalAsync(Guid accountId, DateOnly date, decimal amount, bool isAdjustment = false)
    {
        var account = GetSavingsAccount(accountId);
        if (amount <= 0m)
        {
            throw new InvalidOperationException("Withdrawal amount must be greater than zero.");
        }

        if (NormalizeMonth(date) < NormalizeMonth(account.CreatedDate))
        {
            throw new InvalidOperationException("Withdrawal date cannot be earlier than the account start month.");
        }

        var balanceBeforeWithdrawal = GetSavingsBalanceBeforeOrAtMonth(account.Id, NormalizeMonth(date));
        if (amount > balanceBeforeWithdrawal)
        {
            throw new InvalidOperationException("Withdrawal amount cannot exceed the account balance.");
        }

        if (isAdjustment)
        {
            await AddSavingsBalanceAdjustmentAsync(account, date, -amount);
            return;
        }

        Data.IncomeRecords.Add(CreateOneTimeIncomeEntry(
            Guid.Empty,
            amount,
            date.Year,
            date.Month,
            GetSavingsSubcategory(account.AccountType),
            account.AccountName,
            account.Id,
            null));

        await RecalculateSavingsAccountBalancesAsync(account.Id, NormalizeMonth(date));
        await LogActivityAsync(
            BuildLogDescription(_localizer["nav.savings"], GetSavingsTabTitle(account.AccountType)),
            BuildLogActivity("log.verb.registered", "log.entity.withdrawal", account.AccountName));
    }

    public async Task AddAssetAsync(
        string name,
        string type,
        string description,
        decimal purchaseValue,
        decimal estimatedSaleValue,
        DateOnly acquisitionDate,
        bool registerAsPurchase)
    {
        var asset = new AssetItem
        {
            Name = name.Trim(),
            Type = type.Trim(),
            Description = description.Trim(),
            PurchaseValue = purchaseValue,
            EstimatedSaleValue = estimatedSaleValue,
            AcquisitionDate = acquisitionDate,
            SourceType = AssetSourceType.Manual
        };

        Data.Assets.Add(asset);

        if (registerAsPurchase)
        {
            Data.ExpenseRecords.Add(CreateOneTimeExpenseEntry(
                purchaseValue,
                acquisitionDate.Year,
                acquisitionDate.Month,
                ExpenseCategory.Assets,
                asset.Type,
                asset.Name,
                asset.Name,
                null,
                null,
                asset.Id));
        }

        await PersistAndNotifyAndLogAsync(
            BuildLogDescription(_localizer["nav.assets"]),
            BuildLogActivity("log.verb.added", "log.entity.asset", asset.Name));
    }

    public async Task UpdateAssetAsync(
        Guid assetId,
        string name,
        string type,
        string description,
        decimal purchaseValue,
        decimal estimatedSaleValue,
        DateOnly acquisitionDate)
    {
        var asset = GetAsset(assetId);
        asset.Name = name.Trim();
        asset.Type = type.Trim();
        asset.Description = description.Trim();
        asset.PurchaseValue = purchaseValue;
        asset.EstimatedSaleValue = estimatedSaleValue;
        asset.AcquisitionDate = acquisitionDate;

        await PersistAndNotifyAndLogAsync(
            BuildLogDescription(_localizer["nav.assets"]),
            BuildLogActivity("log.verb.updated", "log.entity.asset", asset.Name));
    }

    public async Task SellAssetAsync(Guid assetId, DateOnly saleDate, decimal saleAmount, Guid? bankSavingsAccountId)
    {
        var asset = GetAsset(assetId);
        if (asset.IsSold)
        {
            throw new InvalidOperationException("The asset has already been sold.");
        }

        if (saleAmount <= 0m)
        {
            throw new InvalidOperationException("Sale amount must be greater than zero.");
        }

        var incomeEntry = CreateOneTimeIncomeEntry(Guid.Empty, saleAmount, saleDate.Year, saleDate.Month, AssetSaleSubcategory, asset.Name, null, asset.Id);
        Data.IncomeRecords.Add(incomeEntry);

        if (bankSavingsAccountId.HasValue)
        {
            var bankAccount = GetSavingsAccount(bankSavingsAccountId.Value);
            if (bankAccount.AccountType != SavingsAccountType.Bank)
            {
                throw new InvalidOperationException("Direct deposit requires a bank savings account.");
            }

            Data.ExpenseRecords.Add(CreateSavingsDepositExpenseEntry(bankAccount, saleDate, saleAmount));
        }

        asset.IsSold = true;
        asset.SaleDate = saleDate;
        asset.SaleAmount = saleAmount;
        asset.SaleIncomeEntryId = incomeEntry.Id;

        if (bankSavingsAccountId.HasValue)
        {
            await RecalculateSavingsBalancesAsync();
            await LogActivityAsync(
                BuildLogDescription(_localizer["nav.assets"]),
                BuildLogActivity("log.verb.updated", "log.entity.assetSale", asset.Name));
            return;
        }

        await PersistAndNotifyAndLogAsync(
            BuildLogDescription(_localizer["nav.assets"]),
            BuildLogActivity("log.verb.updated", "log.entity.assetSale", asset.Name));
    }

    public async Task<int> CalculateSavingsReturnsAsync(DateOnly startDate, DateOnly endDate)
    {
        var normalizedStart = NormalizeMonth(startDate);
        var normalizedEnd = NormalizeMonth(endDate);

        if (normalizedEnd < normalizedStart)
        {
            (normalizedStart, normalizedEnd) = (normalizedEnd, normalizedStart);
        }

        Data.SavingsGeneratedReturns.RemoveAll(item =>
            NormalizeMonth(new DateOnly(item.Year, item.Month, 1)) >= normalizedStart &&
            NormalizeMonth(new DateOnly(item.Year, item.Month, 1)) <= normalizedEnd);

        var generatedCount = 0;

        foreach (var account in Data.SavingsAccounts.OrderBy(item => item.AccountType).ThenBy(item => item.AccountName))
        {
            var createdMonth = NormalizeMonth(account.CreatedDate);
            if (createdMonth > normalizedEnd)
            {
                continue;
            }

            var accountStart = normalizedStart < createdMonth ? createdMonth : normalizedStart;
            RemoveMonthlyBalancesFrom(account.Id, accountStart);

            var balance = accountStart == createdMonth
                ? account.OpeningBalance
                : GetBalanceAtOrBeforeMonth(account.Id, accountStart.AddMonths(-1));

            foreach (var month in EnumerateMonths(accountStart.Year, accountStart.Month, normalizedEnd.Year, normalizedEnd.Month))
            {
                var monthDate = month.ToDateOnly();
                balance += GetSavingsManualNetChangeForMonth(account.Id, monthDate);
                balance = Math.Max(0m, balance);

                var period = GetActiveSavingsReturnPeriods(account.Id, monthDate).FirstOrDefault();
                if (period is not null && balance > 0m)
                {
                    var grossReturn = Math.Round(balance * ConvertPercentageToDecimal(period.RatePercent) / 12m, 2, MidpointRounding.AwayFromZero);
                    var netReturn = account.AccountType == SavingsAccountType.Bank
                        ? Math.Round(grossReturn - (grossReturn * ConvertPercentageToDecimal(period.TaxPercent)), 2, MidpointRounding.AwayFromZero)
                        : grossReturn;

                    if (netReturn != 0m)
                    {
                        Data.SavingsGeneratedReturns.Add(new SavingsGeneratedReturn
                        {
                            SavingsAccountId = account.Id,
                            Year = month.Year,
                            Month = month.Month,
                            Amount = netReturn
                        });

                        balance += netReturn;
                        generatedCount++;
                    }
                }

                SetMonthlyBalance(account.Id, monthDate, balance);
            }
        }

        await PersistAndNotifyAsync();
        return generatedCount;
    }

    public async Task AddCreditAsync(string name, string provider, DateOnly startDate, decimal creditLimit, decimal remainingDebt, decimal monthlyInterestRate, bool resetAtEndOfMonth)
    {
        ValidateCreditValues(creditLimit, remainingDebt, monthlyInterestRate);

        var credit = new Credit
        {
            Name = name.Trim(),
            Provider = provider.Trim(),
            StartDate = startDate,
            CreditLimit = creditLimit,
            StartingRemainingDebt = remainingDebt,
            MonthlyInterestRate = monthlyInterestRate,
            ResetAtEndOfMonth = resetAtEndOfMonth
        };

        Data.Credits.Add(credit);
        SetMonthlyBalance(credit.Id, NormalizeMonth(startDate), remainingDebt);

        await PersistAndNotifyAndLogAsync(
            BuildLogDescription(_localizer["nav.credits"], _localizer["credits.manageTab"]),
            BuildLogActivity("log.verb.added", "log.entity.credit", name));
    }

    public async Task SaveHomeResidenceAsync(HomeResidenceType residenceType, int? purchaseYear, decimal? purchasePrice, decimal? currentMarketValue)
    {
        var isOwnedResidence = residenceType is HomeResidenceType.Condominium or HomeResidenceType.House;
        var existingResidence = Data.HomeResidence;

        Data.HomeResidence = new HomeResidence
        {
            Id = existingResidence?.Id ?? Guid.NewGuid(),
            ResidenceType = residenceType,
            PurchaseYear = isOwnedResidence ? purchaseYear : null,
            PurchasePrice = isOwnedResidence ? purchasePrice : null,
            CurrentMarketValue = isOwnedResidence ? currentMarketValue : null,
            AssetId = existingResidence?.AssetId
        };

        SyncHomeResidenceAsset();

        await PersistAndNotifyAndLogAsync(
            BuildLogDescription(_localizer["nav.housingTab"], _localizer["housing.homeResidence"]),
            BuildLogActivity("log.verb.saved", "log.entity.home", _localizer[$"housing.residenceType.{residenceType}"]));
    }

    public async Task AddCreditPurchaseAsync(Guid creditId, DateOnly purchaseDate, decimal amount, ExpenseCategory category, string subcategory, string description)
    {
        var credit = GetCredit(creditId);
        if (amount <= 0m)
        {
            throw new InvalidOperationException("Purchase amount must be greater than zero.");
        }

        var month = NormalizeMonth(purchaseDate);
        var debtBeforePurchase = GetCreditBalanceBeforeOrAtMonth(credit.Id, month);
        if (debtBeforePurchase + amount > credit.CreditLimit)
        {
            throw new InvalidOperationException("The purchase would exceed the credit limit.");
        }

        var normalizedSubcategory = subcategory.Trim();
        var normalizedDescription = description.Trim();
        Data.ExpenseRecords.Add(new ExpenseEntry
        {
            Amount = amount,
            Year = purchaseDate.Year,
            Month = purchaseDate.Month,
            Category = category,
            Subcategory = normalizedSubcategory,
            Metadata = CreditMetadata,
            Description = normalizedDescription,
            CreditId = credit.Id,
            CreditCostSource = CreditCostSource.ManualPurchase
        });

        await RecalculateCreditBalancesAsync(credit.Id, month);
        await LogActivityAsync(
            BuildLogDescription(_localizer["nav.credits"], _localizer["credits.manageTab"]),
            BuildLogActivity("log.verb.added", "log.entity.creditPurchase", credit.Name));
    }

    public async Task PostCreditMonthAsync(Guid creditId, DateOnly month, decimal amortizationAmount, bool payFullAmount)
    {
        var credit = GetCredit(creditId);
        var normalizedMonth = NormalizeMonth(month);
        if (IsCreditMonthPosted(credit.Id, normalizedMonth))
        {
            throw new InvalidOperationException("The credit month has already been posted.");
        }

        var currentDebt = GetCreditBalanceBeforeOrAtMonth(credit.Id, normalizedMonth);
        var amountToPost = payFullAmount ? currentDebt : Math.Min(amortizationAmount, currentDebt);

        if (amortizationAmount < 0m)
        {
            throw new InvalidOperationException("Amortization amount must not be negative.");
        }

        if (amortizationAmount > currentDebt && !payFullAmount)
        {
            throw new InvalidOperationException("Amortization amount cannot exceed the remaining debt.");
        }

        if (amountToPost > 0m)
        {
            Data.ExpenseRecords.Add(new ExpenseEntry
            {
                Amount = amountToPost,
                Year = month.Year,
                Month = month.Month,
                Category = ExpenseCategory.Credits,
                Subcategory = $"{credit.Name} - {AmortizationSubcategory}",
                Description = string.Empty,
                CreditId = credit.Id,
                CreditCostSource = CreditCostSource.ManualAmortization
            });
        }

        var debtAfterAmortization = Math.Max(0m, currentDebt - amountToPost);
        if (debtAfterAmortization > 0m && credit.MonthlyInterestRate > 0m)
        {
            var interestAmount = Math.Round(debtAfterAmortization * ConvertPercentageToDecimal(credit.MonthlyInterestRate) / 12m, 2, MidpointRounding.AwayFromZero);
            if (interestAmount > 0m)
            {
                Data.ExpenseRecords.Add(new ExpenseEntry
                {
                    Amount = interestAmount,
                    Year = month.Year,
                    Month = month.Month,
                    Category = ExpenseCategory.Credits,
                    Subcategory = $"{credit.Name} - {InterestSubcategory}",
                    Description = string.Empty,
                    CreditId = credit.Id,
                    CreditCostSource = CreditCostSource.GeneratedInterest
                });
            }
        }

        await RecalculateCreditBalancesAsync(credit.Id, normalizedMonth);
        await LogActivityAsync(
            BuildLogDescription(_localizer["nav.credits"], _localizer["credits.manageTab"]),
            BuildLogActivity("log.verb.posted", "log.entity.creditMonth", credit.Name));
    }

    public async Task<int> CalculateHousingLoanCostsAsync(DateOnly startDate, DateOnly endDate)
    {
        var generatedCount = RecalculateLoanBalances(ExpenseCategory.Housing, Data.HousingLoans, startDate, endDate, loan => loan.Id, loan => loan.Lender, _ => null);

        await PersistAndNotifyAsync();
        return generatedCount;
    }

    public async Task<int> CalculateTransportCostsAsync(DateOnly startDate, DateOnly endDate)
    {
        var generatedCount = RecalculateLoanBalances(
            ExpenseCategory.Transport,
            Data.TransportLoans,
            startDate,
            endDate,
            loan => loan.Id,
            loan => loan.Lender,
            loan => loan.VehicleId);

        var normalizedStart = NormalizeMonth(startDate);
        var normalizedEnd = NormalizeMonth(endDate);
        if (normalizedEnd < normalizedStart)
        {
            (normalizedStart, normalizedEnd) = (normalizedEnd, normalizedStart);
        }

        foreach (var month in EnumerateMonths(normalizedStart.Year, normalizedStart.Month, normalizedEnd.Year, normalizedEnd.Month))
        {
            var monthDate = month.ToDateOnly();

            foreach (var contract in GetActiveTransportLeasingContracts(monthDate))
            {
                var vehicleMetadata = GetTransportVehicleName(contract.VehicleId);
                Data.ExpenseRecords.Add(new ExpenseEntry
                {
                    Amount = contract.MonthlyCost,
                    Year = month.Year,
                    Month = month.Month,
                    Category = ExpenseCategory.Transport,
                    Subcategory = LeasingSubcategory,
                    Metadata = vehicleMetadata,
                    Description = string.Empty,
                    TransportLeasingContractId = contract.Id,
                    TransportVehicleId = contract.VehicleId
                });
                generatedCount++;
            }
        }

        await PersistAndNotifyAsync();
        return generatedCount;
    }

    public async Task<int> CalculateCreditsAsync(DateOnly startDate, DateOnly endDate)
    {
        var normalizedStart = NormalizeMonth(startDate);
        var normalizedEnd = NormalizeMonth(endDate);

        if (normalizedEnd < normalizedStart)
        {
            (normalizedStart, normalizedEnd) = (normalizedEnd, normalizedStart);
        }

        RemoveGeneratedCreditExpensesInRange(normalizedStart, normalizedEnd);

        var generatedCount = 0;

        foreach (var credit in Data.Credits.OrderBy(item => item.StartDate).ThenBy(item => item.Provider))
        {
            var balance = GetBalanceAtOrBeforeMonth(credit.Id, normalizedStart.AddMonths(-1));

            foreach (var month in EnumerateMonths(normalizedStart.Year, normalizedStart.Month, normalizedEnd.Year, normalizedEnd.Month))
            {
                var monthDate = month.ToDateOnly();
                if (NormalizeMonth(credit.StartDate) > monthDate)
                {
                    continue;
                }

                balance += Data.ExpenseRecords
                    .Where(expense =>
                        expense.CreditId == credit.Id &&
                        NormalizeMonth(new DateOnly(expense.Year, expense.Month, 1)) == monthDate &&
                        expense.CreditCostSource == CreditCostSource.ManualPurchase)
                    .Sum(expense => expense.Amount);

                balance -= Data.ExpenseRecords
                    .Where(expense =>
                        expense.CreditId == credit.Id &&
                        NormalizeMonth(new DateOnly(expense.Year, expense.Month, 1)) == monthDate &&
                        expense.CreditCostSource == CreditCostSource.ManualAmortization)
                    .Sum(expense => expense.Amount);
                balance = Math.Max(0m, balance);

                if (balance > 0m && credit.MonthlyInterestRate > 0m)
                {
                    var interestAmount = Math.Round(balance * ConvertPercentageToDecimal(credit.MonthlyInterestRate) / 12m, 2, MidpointRounding.AwayFromZero);
                    if (interestAmount > 0m)
                    {
                        Data.ExpenseRecords.Add(new ExpenseEntry
                        {
                            Amount = interestAmount,
                            Year = month.Year,
                            Month = month.Month,
                            Category = ExpenseCategory.Credits,
                            Subcategory = InterestSubcategory,
                            Description = string.Empty,
                            CreditId = credit.Id,
                            CreditCostSource = CreditCostSource.GeneratedInterest
                        });

                        balance += interestAmount;
                        generatedCount++;
                    }
                }

                if (credit.ResetAtEndOfMonth && balance > 0m)
                {
                    Data.ExpenseRecords.Add(new ExpenseEntry
                    {
                        Amount = balance,
                        Year = month.Year,
                        Month = month.Month,
                        Category = ExpenseCategory.Credits,
                        Subcategory = AmortizationSubcategory,
                        Description = string.Empty,
                        CreditId = credit.Id,
                        CreditCostSource = CreditCostSource.GeneratedResetAmortization
                    });
                    balance = 0m;
                    generatedCount++;
                }

                SetMonthlyBalance(credit.Id, monthDate, balance);
            }

            RefreshCreditDerivedValues(credit.Id);
        }

        await PersistAndNotifyAsync();
        return generatedCount;
    }

    private async Task RecalculateLoanCategoryCostsAsync(Guid loanId)
    {
        if (Data.HousingLoans.Any(loan => loan.Id == loanId))
        {
            await RecalculateHousingLoanCostsAsync();
            return;
        }

        if (Data.TransportLoans.Any(loan => loan.Id == loanId))
        {
            await RecalculateTransportCostsAsync();
        }
    }

    private async Task CalculateLoanCategoryCostsAsync(Guid loanId, DateOnly startDate, DateOnly endDate)
    {
        if (Data.HousingLoans.Any(loan => loan.Id == loanId))
        {
            await CalculateHousingLoanCostsAsync(startDate, endDate);
            return;
        }

        if (Data.TransportLoans.Any(loan => loan.Id == loanId))
        {
            await CalculateTransportCostsAsync(startDate, endDate);
        }
    }

    private async Task RecalculateHousingLoanCostsAsync()
    {
        if (TryGetHousingLoanCalculationRange(out var startDate, out var endDate))
        {
            await CalculateHousingLoanCostsAsync(startDate, endDate);
            return;
        }

        RemoveAllGeneratedExpenses(ExpenseCategory.Housing, includeLeasing: false);

        foreach (var loan in Data.HousingLoans)
        {
            RefreshLoanDerivedValues(loan.Id);
        }

        await PersistAndNotifyAsync();
    }

    private async Task RecalculateTransportCostsAsync()
    {
        if (TryGetTransportCalculationRange(out var startDate, out var endDate))
        {
            await CalculateTransportCostsAsync(startDate, endDate);
            return;
        }

        RemoveAllGeneratedExpenses(ExpenseCategory.Transport, includeLeasing: true);

        foreach (var loan in Data.TransportLoans)
        {
            RefreshTransportLoanDerivedValues(loan.Id);
        }

        await PersistAndNotifyAsync();
    }

    private async Task RecalculateCreditsForActiveRangeAsync()
    {
        if (TryGetCreditCalculationRange(out var startDate, out var endDate))
        {
            await CalculateCreditsAsync(startDate, endDate);
            return;
        }

        RemoveAllGeneratedCreditExpenses();

        foreach (var credit in Data.Credits)
        {
            RefreshCreditDerivedValues(credit.Id);
        }

        await PersistAndNotifyAsync();
    }

    public async Task<int> GenerateMissingSubscriptionExpensesAsync()
    {
        Data.ExpenseRecords.RemoveAll(expense => expense.SubscriptionDefinitionId.HasValue);

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

    public BudgetOverview GetBudgetOverview()
    {
        var closedMonths = GetLatestClosedMonths(12);
        if (closedMonths.Count == 0)
        {
            return new BudgetOverview();
        }

        decimal income = 0m;
        decimal housing = 0m;
        decimal food = 0m;
        decimal transport = 0m;
        decimal clothing = 0m;
        decimal savings = 0m;
        decimal other = 0m;
        decimal credits = 0m;

        foreach (var month in closedMonths)
        {
            var overview = GetMonthlyOverview(month.Year, month.Month);
            income += overview.Income;
            housing += overview.Housing;
            food += overview.Food;
            transport += overview.Transport;
            clothing += overview.Clothing;
            savings += overview.Savings;
            other += overview.Other;
            credits += overview.Credits;
        }

        var count = closedMonths.Count;
        var firstMonth = closedMonths[0];
        var lastMonth = closedMonths[^1];

        return new BudgetOverview
        {
            Income = income / count,
            Housing = housing / count,
            Food = food / count,
            Transport = transport / count,
            Clothing = clothing / count,
            Savings = savings / count,
            Other = other / count,
            Credits = credits / count,
            ClosedMonthCount = count,
            StartYear = firstMonth.Year,
            StartMonth = firstMonth.Month,
            EndYear = lastMonth.Year,
            EndMonth = lastMonth.Month
        };
    }

    public DashboardKeyMetrics GetCurrentDashboardKeyMetrics()
    {
        var currentMonth = GetCurrentMonth().ToDateOnly();
        var previousMonth = currentMonth.AddMonths(-1);
        var housingLoans = GetHousingLoanDebtForMonth(currentMonth);
        var credits = GetCreditsDebtForMonth(currentMonth);
        var propertyValue = GetPropertyValueForMonth(currentMonth);
        var savings = GetSavingsForMonth(currentMonth);
        var interest = GetInterestIncomeForMonth(currentMonth) - GetInterestCostsForMonth(currentMonth.Year, currentMonth.Month);
        var amortization = GetAmortizationCostsForMonth(currentMonth.Year, currentMonth.Month);
        var balance = CalculateBalanceForMonth(currentMonth);
        var previousBalance = CalculateBalanceForMonth(previousMonth);

        return new DashboardKeyMetrics
        {
            LoanToValueRatio = propertyValue > 0m ? housingLoans / propertyValue : 0m,
            HousingLoans = housingLoans,
            PropertyValue = propertyValue,
            Credits = credits,
            Savings = savings,
            Interest = interest,
            Amortization = amortization,
            Balance = balance,
            Change = balance - previousBalance
        };
    }

    public IReadOnlyList<ClosedMonth> GetClosedMonths() =>
        Data.ClosedMonths
            .OrderBy(month => month.Year)
            .ThenBy(month => month.Month)
            .ToList();

    public bool IsMonthClosed(DateOnly month)
    {
        var normalizedMonth = NormalizeMonth(month);
        return Data.ClosedMonths.Any(item => item.Year == normalizedMonth.Year && item.Month == normalizedMonth.Month);
    }

    public CloseMonthContext GetCloseMonthContext(DateOnly month)
    {
        var normalizedMonth = NormalizeMonth(month);

        return new CloseMonthContext
        {
            Month = normalizedMonth,
            IsClosed = IsMonthClosed(normalizedMonth),
            Surplus = GetMonthlyIncome(normalizedMonth.Year, normalizedMonth.Month) - GetMonthlyExpenses(normalizedMonth.Year, normalizedMonth.Month),
            BankAccounts = Data.SavingsAccounts
                .Where(account => account.AccountType == SavingsAccountType.Bank)
                .OrderBy(account => account.AccountName)
                .ToList(),
            FundAccounts = Data.SavingsAccounts
                .Where(account => account.AccountType == SavingsAccountType.Fund)
                .OrderBy(account => account.AccountName)
                .ToList()
        };
    }

    public async Task<bool> CloseMonthAsync(DateOnly month, CloseMonthSurplusAction surplusAction, Guid? savingsAccountId = null)
    {
        var normalizedMonth = NormalizeMonth(month);
        if (IsMonthClosed(normalizedMonth))
        {
            return false;
        }

        Guid? generatedExpenseId = null;
        var surplus = GetMonthlyIncome(normalizedMonth.Year, normalizedMonth.Month) - GetMonthlyExpenses(normalizedMonth.Year, normalizedMonth.Month);
        if (surplus <= 0m)
        {
            surplusAction = CloseMonthSurplusAction.DoNothing;
            savingsAccountId = null;
        }

        switch (surplusAction)
        {
            case CloseMonthSurplusAction.DoNothing:
                break;
            case CloseMonthSurplusAction.RegisterExpense:
                generatedExpenseId = await AddGeneratedOneTimeExpenseAsync(
                    surplus,
                    normalizedMonth.Year,
                    normalizedMonth.Month,
                    ExpenseCategory.Other,
                    UnspecifiedSubcategory,
                    string.Empty);
                break;
            case CloseMonthSurplusAction.DepositToBankSavings:
                generatedExpenseId = await AddSurplusDepositAsync(normalizedMonth, surplus, savingsAccountId, SavingsAccountType.Bank);
                break;
            case CloseMonthSurplusAction.DepositToFundSavings:
                generatedExpenseId = await AddSurplusDepositAsync(normalizedMonth, surplus, savingsAccountId, SavingsAccountType.Fund);
                break;
            default:
                throw new InvalidOperationException("The selected surplus action is not supported.");
        }

        if (IsMonthClosed(normalizedMonth))
        {
            return false;
        }

        Data.ClosedMonths.Add(new ClosedMonth
        {
            Year = normalizedMonth.Year,
            Month = normalizedMonth.Month,
            GeneratedExpenseId = generatedExpenseId
        });

        NormalizeClosedMonths();

        await PersistAndNotifyAndLogAsync(
            BuildLogDescription(_localizer["nav.start"], _localizer["start.monthlyReport"]),
            BuildLogActivity("log.verb.closed", "log.entity.closedMonth", $"{normalizedMonth:yyyy-MM}"));

        return true;
    }

    public async Task<bool> UndoCloseMonthAsync(DateOnly month)
    {
        var normalizedMonth = NormalizeMonth(month);
        var closedMonth = Data.ClosedMonths.FirstOrDefault(item => item.Year == normalizedMonth.Year && item.Month == normalizedMonth.Month);
        if (closedMonth is null)
        {
            return false;
        }

        if (closedMonth.GeneratedExpenseId.HasValue)
        {
            await RemoveExpenseAsync(closedMonth.GeneratedExpenseId.Value);
        }

        Data.ClosedMonths.RemoveAll(item => item.Year == normalizedMonth.Year && item.Month == normalizedMonth.Month);
        NormalizeClosedMonths();

        await PersistAndNotifyAndLogAsync(
            BuildLogDescription(_localizer["nav.start"], _localizer["start.monthlyReport"]),
            BuildLogActivity("log.verb.reopened", "log.entity.closedMonth", $"{normalizedMonth:yyyy-MM}"));

        return true;
    }

    public string GetExpenseSubcategoryDisplay(ExpenseEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var metadata = GetExpenseMetadataDisplay(entry.Metadata);
        var subcategory = GetLocalizedExpenseLabel(entry.Subcategory);

        return string.IsNullOrWhiteSpace(metadata)
            ? subcategory
            : $"{metadata} - {subcategory}";
    }

    public string GetExpenseDescriptionDisplay(ExpenseEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return string.IsNullOrWhiteSpace(entry.Description) || PreferSystemExpenseLabel(entry)
            ? GetExpenseSubcategoryDisplay(entry)
            : entry.Description.Trim();
    }

    public IReadOnlyList<string> GetExpenseSubcategorySuggestions(ExpenseCategory category) =>
        GetConfiguredSubcategorySuggestions(category)
            .Concat(Data.ExpenseRecords
                .Where(record => record.Category == category && !string.IsNullOrWhiteSpace(record.Subcategory))
                .Select(record => GetLocalizedConfiguredSubcategoryLabel(MapExpenseCategoryToMainCategory(category), record.Subcategory.Trim())))
            .Concat(Data.Subscriptions
                .Where(record => record.Category == category && !string.IsNullOrWhiteSpace(record.Subcategory))
                .Select(record => GetLocalizedConfiguredSubcategoryLabel(MapExpenseCategoryToMainCategory(category), record.Subcategory.Trim())))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value)
            .ToList();

    public IReadOnlyList<string> GetIncomeTypeSuggestions() =>
        Data.SubcategoryDefinitions
            .Where(definition => definition.MainCategory == SubcategoryMainCategory.Income)
            .Select(GetSubcategoryDisplay)
            .Concat(Data.IncomeRecords.Select(record => GetIncomeTypeDisplay(record.Type)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value)
            .ToList();

    public IReadOnlyList<SubcategoryDefinition> GetSubcategoryDefinitions(SubcategoryMainCategory mainCategory) =>
        Data.SubcategoryDefinitions
            .Where(definition => definition.MainCategory == mainCategory)
            .OrderBy(definition => GetSubcategoryDisplay(definition), StringComparer.CurrentCultureIgnoreCase)
            .Select(CloneSubcategoryDefinition)
            .ToList();

    public async Task SaveSubcategoryDefinitionsAsync(SubcategoryMainCategory mainCategory, IEnumerable<SubcategoryDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        var normalizedDefinitions = definitions
            .Select(definition => new SubcategoryDefinition
            {
                Id = definition.Id == Guid.Empty ? Guid.NewGuid() : definition.Id,
                Key = string.IsNullOrWhiteSpace(definition.Key) ? $"subcategory.custom.{Guid.NewGuid():N}" : definition.Key.Trim(),
                MainCategory = mainCategory,
                SwedishName = definition.SwedishName.Trim(),
                EnglishName = definition.EnglishName.Trim()
            })
            .Where(definition => !string.IsNullOrWhiteSpace(definition.SwedishName) && !string.IsNullOrWhiteSpace(definition.EnglishName))
            .GroupBy(definition => definition.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(definition => definition.SwedishName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        Data.SubcategoryDefinitions = Data.SubcategoryDefinitions
            .Where(definition => definition.MainCategory != mainCategory)
            .Concat(normalizedDefinitions)
            .ToList();

        NormalizeSubcategoryDefinitions();

        await PersistAndNotifyAndLogAsync(
            BuildLogDescription(_localizer["nav.settings"], _localizer["settings.tab.regional"]),
            BuildLogActivity("log.verb.saved", "log.entity.subcategory", _localizer[$"subcategoryMainCategory.{mainCategory}"]));
    }

    public string GetExpenseLabelDisplay(string subcategory) => GetLocalizedExpenseLabel(subcategory);

    public bool PreferSystemExpenseLabel(ExpenseEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return entry.LoanId.HasValue ||
               entry.TransportLeasingContractId.HasValue ||
               entry.CreditId.HasValue ||
               entry.SavingsAccountId.HasValue ||
               entry.AssetId.HasValue;
    }

    public string GetExpenseMetadataDisplay(string? metadata)
    {
        var trimmedMetadata = metadata?.Trim() ?? string.Empty;
        return string.Equals(trimmedMetadata, CreditMetadata, StringComparison.Ordinal)
            ? _localizer["credits.credit"]
            : trimmedMetadata;
    }

    public string GetIncomeTypeDisplay(string type)
    {
        if (TryGetConfiguredSubcategoryDisplay(SubcategoryMainCategory.Income, type, out var configuredDisplay))
        {
            return configuredDisplay;
        }

        var normalizedType = NormalizeSystemIncomeType(type);
        return normalizedType switch
        {
            SalaryIncomeType => _localizer["incomeType.Salary"],
            TaxRefundIncomeType => _localizer["incomeType.TaxRefund"],
            InheritanceIncomeType => _localizer["incomeType.Inheritance"],
            GiftIncomeType => _localizer["incomeType.Gift"],
            OtherIncomeType => _localizer["incomeType.Other"],
            AssetSaleSubcategory => _localizer["assets.assetSale"],
            "Bank" => _localizer["savings.bankTab"],
            "Fund" => _localizer["savings.fundsTab"],
            "Stock" => _localizer["savings.stocksTab"],
            _ => type.Trim()
        };
    }

    public string GetAssetNameDisplay(AssetItem asset)
    {
        ArgumentNullException.ThrowIfNull(asset);

        if (asset.SourceType == AssetSourceType.HomeResidence && Data.HomeResidence is not null && asset.HomeResidenceId == Data.HomeResidence.Id)
        {
            return GetHomeResidenceAssetNameDisplay(Data.HomeResidence);
        }

        return asset.Name;
    }

    public string GetAssetTypeDisplay(AssetItem asset)
    {
        ArgumentNullException.ThrowIfNull(asset);

        if (asset.SourceType == AssetSourceType.HomeResidence && Data.HomeResidence is not null && asset.HomeResidenceId == Data.HomeResidence.Id)
        {
            return GetHomeResidenceAssetTypeDisplay(Data.HomeResidence);
        }

        if (asset.SourceType == AssetSourceType.TransportVehicle && string.Equals(asset.Type, VehicleAssetType, StringComparison.Ordinal))
        {
            return _localizer["transport.vehicle"];
        }

        return asset.Type;
    }

    public string GetAssetDescriptionDisplay(AssetItem asset)
    {
        ArgumentNullException.ThrowIfNull(asset);

        if (asset.SourceType == AssetSourceType.HomeResidence && Data.HomeResidence is not null && asset.HomeResidenceId == Data.HomeResidence.Id)
        {
            return Data.HomeResidence.ResidenceType switch
            {
                HomeResidenceType.Condominium => $"{_localizer["housing.homeResidence"]} / {_localizer["housing.residenceType.condominium"]}",
                HomeResidenceType.House => $"{_localizer["housing.homeResidence"]} / {_localizer["housing.residenceType.house"]}",
                _ => _localizer["housing.homeResidence"]
            };
        }

        if (asset.SourceType == AssetSourceType.TransportVehicle && asset.TransportVehicleId.HasValue)
        {
            var vehicle = Data.TransportVehicles.FirstOrDefault(item => item.Id == asset.TransportVehicleId.Value);
            if (vehicle is not null)
            {
                return $"{vehicle.ModelYear} {_localizer[$"transport.vehicleType.{vehicle.VehicleType}"]} / {_localizer[$"transport.fuelType.{vehicle.FuelType}"]}";
            }
        }

        return asset.Description;
    }

    public MonthlyOverview GetMonthlyOverview(int year, int month)
    {
        var income = Data.IncomeRecords.Where(entry => entry.Year == year && entry.Month == month).Sum(entry => entry.Amount);
        var expenses = Data.ExpenseRecords.Where(entry => entry.Year == year && entry.Month == month).ToList();

        return new MonthlyOverview
        {
            Year = year,
            Month = month,
            Housing = expenses.Where(entry => entry.Category == ExpenseCategory.Housing).Sum(entry => entry.Amount),
            Income = income,
            Food = expenses.Where(entry => entry.Category == ExpenseCategory.Food).Sum(entry => entry.Amount),
            Transport = expenses.Where(entry => entry.Category == ExpenseCategory.Transport).Sum(entry => entry.Amount),
            Credits = expenses.Where(entry => entry.Category == ExpenseCategory.Credits).Sum(entry => entry.Amount),
            Savings = expenses.Where(entry => entry.Category == ExpenseCategory.Savings).Sum(entry => entry.Amount),
            Clothing = expenses.Where(entry => entry.Category == ExpenseCategory.Clothing).Sum(entry => entry.Amount),
            Other = expenses.Where(entry => entry.Category == ExpenseCategory.Other).Sum(entry => entry.Amount)
        };
    }

    public IReadOnlyList<MonthlyReportRow> GetMonthlyReportRows()
    {
        var (startDate, endDate) = GetDefaultMonthlyReportRange();
        return GetMonthlyReportRows(startDate, endDate);
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

        var rows = new List<MonthlyReportRow>();

        foreach (var month in months)
        {
            var monthDate = month.ToDateOnly();
            var income = GetMonthlyIncome(month.Year, month.Month);
            var expenses = GetMonthlyExpenses(month.Year, month.Month);
            var housingLoans = GetHousingLoanDebtForMonth(monthDate);
            var credits = GetCreditsDebtForMonth(monthDate);
            var savings = GetSavingsForMonth(monthDate);
            var interest = GetInterestIncomeForMonth(monthDate) - GetInterestCostsForMonth(month.Year, month.Month);
            var amortization = GetAmortizationCostsForMonth(month.Year, month.Month);
            var balance = income - expenses;

            rows.Add(new MonthlyReportRow
            {
                Period = $"{month.Year:0000}/{month.Month:00}",
                Year = month.Year,
                Month = month.Month,
                Income = income,
                Expenses = expenses,
                HousingLoans = housingLoans,
                Credits = credits,
                Savings = savings,
                Interest = interest,
                Amortization = amortization,
                Balance = balance,
                IsClosed = IsMonthClosed(monthDate)
            });
        }

        return rows.OrderByDescending(row => row.Period, StringComparer.Ordinal).ToList();
    }

    public async Task<string> ExportAsync() => await _localStorageService.BackupAsync(BuildExportFileName("budget-advisor-backup"), Data);

    public async Task ImportAsync(string json)
    {
        var data = await _localStorageService.RestoreAsync<ApplicationData>(json);
        if (data is null)
        {
            throw new InvalidOperationException("The backup file is invalid.");
        }

        ApplyLoadedData(data, initializeUndoState: false);
        await PersistAndNotifyAsync();
    }

    private static string BuildExportFileName(string baseName) =>
        $"{baseName}-{DateTime.Now:yyyyMMddHHmm}.json";

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
        Data.ThemeMode = AppThemeNames.Normalize(themeMode);
        await PersistAndNotifyAsync();
    }

    public async Task SetUpcomingExpensesPreferencesAsync(int months, decimal? minimumAmount)
    {
        Data.UpcomingExpensesMonths = months;
        Data.UpcomingExpensesMinimumAmount = minimumAmount.HasValue && minimumAmount.Value > 0m
            ? minimumAmount.Value
            : null;

        NormalizeUpcomingExpensesPreferences();
        await PersistAndNotifyAsync();
    }

    public async Task SetLatestImportedExpenseDateAsync(DateOnly latestImportedExpenseDate)
    {
        if (Data.LatestImportedExpenseDate.HasValue && Data.LatestImportedExpenseDate.Value >= latestImportedExpenseDate)
        {
            return;
        }

        Data.LatestImportedExpenseDate = latestImportedExpenseDate;
        await PersistAndNotifyAsync();
    }

    public async Task SaveImportedExpenseCategorySuggestionsAsync(IEnumerable<ImportedExpenseCategorySuggestion> suggestions)
    {
        ArgumentNullException.ThrowIfNull(suggestions);

        var mergedSuggestions = Data.ImportedExpenseCategorySuggestions
            .Concat(suggestions)
            .Where(suggestion =>
                !string.IsNullOrWhiteSpace(suggestion.Description) &&
                !string.IsNullOrWhiteSpace(suggestion.Subcategory))
            .Select(suggestion => new ImportedExpenseCategorySuggestion
            {
                Description = suggestion.Description.Trim(),
                Category = suggestion.Category,
                Subcategory = suggestion.Subcategory.Trim()
            })
            .DistinctBy(suggestion => new
            {
                Description = suggestion.Description,
                suggestion.Category,
                Subcategory = suggestion.Subcategory
            })
            .OrderBy(suggestion => suggestion.Description, StringComparer.OrdinalIgnoreCase)
            .ThenBy(suggestion => suggestion.Category)
            .ThenBy(suggestion => suggestion.Subcategory, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (mergedSuggestions.SequenceEqual(Data.ImportedExpenseCategorySuggestions, ImportedExpenseCategorySuggestionComparer.Instance))
        {
            return;
        }

        Data.ImportedExpenseCategorySuggestions = mergedSuggestions;
        await PersistAndNotifyAsync();
    }

    public async Task ClearImportedExpenseCategorySuggestionsAsync()
    {
        if (Data.ImportedExpenseCategorySuggestions.Count == 0)
        {
            return;
        }

        Data.ImportedExpenseCategorySuggestions.Clear();
        await PersistAndNotifyAsync();
    }

    public async Task<DataPruningSummary> PruneDataAsync(DateOnly cutoffDate)
    {
        var summary = _dataPruningService.Prune(Data, cutoffDate);
        await PersistAndNotifyAsync();
        await LogActivityAsync(
            BuildLogDescription(_localizer["nav.settings"], _localizer["settings.tab.data"]),
            BuildLogActivity("log.verb.pruned", "log.entity.data", cutoffDate.ToString("yyyy-MM-dd")));
        return summary;
    }

    public async Task<bool> UndoLastChangeAsync()
    {
        var snapshotJson = _undoService.ConsumeUndoSnapshot();
        if (string.IsNullOrWhiteSpace(snapshotJson))
        {
            return false;
        }

        var restoredData = _localStorageService.Deserialize<ApplicationData>(snapshotJson);
        if (restoredData is null)
        {
            return false;
        }

        Data = restoredData;
        NormalizeData();
        await PersistAndNotifyAsync(captureUndoSnapshot: false);
        return true;
    }

    public async Task RemoveExpenseAsync(Guid expenseId)
    {
        var expense = Data.ExpenseRecords.FirstOrDefault(item => item.Id == expenseId);
        if (expense is null)
        {
            return;
        }

        Data.ExpenseRecords.RemoveAll(item => item.Id == expenseId);

        if (expense.SavingsAccountId.HasValue)
        {
            await RecalculateSavingsBalancesAsync();
            await LogActivityAsync(
                BuildLogDescription(_localizer["nav.expenses"], _localizer["expenses.expensesTab"]),
                BuildLogActivity("log.verb.deleted", "log.entity.expense", GetExpenseActivitySubject(expense.Subcategory, GetExpenseDescriptionDisplay(expense))));
            return;
        }

        await PersistAndNotifyAsync();
    }

    public async Task RemoveSubscriptionAsync(Guid subscriptionId)
    {
        var subscription = Data.Subscriptions.FirstOrDefault(item => item.Id == subscriptionId);
        Data.Subscriptions.RemoveAll(item => item.Id == subscriptionId);
        Data.ExpenseRecords.RemoveAll(expense => expense.SubscriptionDefinitionId == subscriptionId);
        await RegenerateSubscriptionExpensesAsync();
        await LogActivityAsync(
            BuildLogDescription(_localizer["nav.expenses"], _localizer["expenses.subscriptionAndDefinitions"]),
            BuildLogActivity("log.verb.deleted", "log.entity.recurringExpense", subscription?.Name ?? subscriptionId.ToString()));
    }

    public async Task<bool> ArchiveSubscriptionAsync(Guid subscriptionId)
    {
        var subscription = Data.Subscriptions.FirstOrDefault(item => item.Id == subscriptionId);
        if (subscription is null)
        {
            return false;
        }

        Data.Subscriptions.RemoveAll(item => item.Id == subscriptionId);
        await PersistAndNotifyAndLogAsync(
            BuildLogDescription(_localizer["nav.expenses"], _localizer["expenses.subscriptionAndDefinitions"]),
            BuildLogActivity("log.verb.deleted", "log.entity.recurringExpense", subscription.Name));
        return true;
    }

    public async Task RemoveHousingDefinitionAsync(Guid definitionId)
    {
        var definition = Data.HousingDefinitions.FirstOrDefault(item => item.Id == definitionId);
        Data.HousingDefinitions.RemoveAll(item => item.Id == definitionId);
        Data.ExpenseRecords.RemoveAll(expense => expense.HousingDefinitionId == definitionId);
        await PersistAndNotifyAndLogAsync(
            BuildLogDescription(_localizer["nav.housingTab"], _localizer["housing.recurringCostsTab"]),
            BuildLogActivity("log.verb.deleted", "log.entity.housingDefinition", definition?.Description ?? definitionId.ToString()));
    }

    public async Task<bool> ArchiveHousingDefinitionAsync(Guid definitionId)
    {
        var definition = Data.HousingDefinitions.FirstOrDefault(item => item.Id == definitionId);
        if (definition is null)
        {
            return false;
        }

        Data.HousingDefinitions.RemoveAll(item => item.Id == definitionId);
        await PersistAndNotifyAndLogAsync(
            BuildLogDescription(_localizer["nav.housingTab"], _localizer["housing.recurringCostsTab"]),
            BuildLogActivity("log.verb.deleted", "log.entity.housingDefinition", definition.Description));
        return true;
    }

    public async Task RemoveTransportDefinitionAsync(Guid definitionId)
    {
        var definition = Data.TransportDefinitions.FirstOrDefault(item => item.Id == definitionId);
        Data.TransportDefinitions.RemoveAll(item => item.Id == definitionId);
        Data.ExpenseRecords.RemoveAll(expense => expense.TransportDefinitionId == definitionId);
        await PersistAndNotifyAndLogAsync(
            BuildLogDescription(_localizer["nav.transport"], _localizer["transport.recurringCostsTab"]),
            BuildLogActivity("log.verb.deleted", "log.entity.transportDefinition", definition?.Subcategory ?? definitionId.ToString()));
    }

    public async Task<bool> ArchiveTransportDefinitionAsync(Guid definitionId)
    {
        var definition = Data.TransportDefinitions.FirstOrDefault(item => item.Id == definitionId);
        if (definition is null)
        {
            return false;
        }

        Data.TransportDefinitions.RemoveAll(item => item.Id == definitionId);
        await PersistAndNotifyAndLogAsync(
            BuildLogDescription(_localizer["nav.transport"], _localizer["transport.recurringCostsTab"]),
            BuildLogActivity("log.verb.deleted", "log.entity.transportDefinition", string.IsNullOrWhiteSpace(definition.Description) ? definition.Subcategory : definition.Description));
        return true;
    }

    public async Task RemoveHousingLoanAsync(Guid loanId)
    {
        var loan = Data.HousingLoans.FirstOrDefault(item => item.Id == loanId);
        Data.HousingLoans.RemoveAll(item => item.Id == loanId);
        Data.LoanInterestBindingPeriods.RemoveAll(period => period.LoanId == loanId);
        Data.LoanAmortizationPlans.RemoveAll(plan => plan.LoanId == loanId);
        Data.ExpenseRecords.RemoveAll(expense => expense.LoanId == loanId);
        RemoveAllMonthlyBalances(loanId);
        await RecalculateHousingLoanCostsAsync();
        await LogActivityAsync(
            BuildLogDescription(_localizer["nav.housingTab"], _localizer["housing.loansTab"]),
            BuildLogActivity("log.verb.deleted", "log.entity.housingLoan", loan?.Name ?? loanId.ToString()));
    }

    public async Task RemoveLoanInterestBindingPeriodAsync(Guid periodId)
    {
        var period = Data.LoanInterestBindingPeriods.FirstOrDefault(item => item.Id == periodId);
        if (period is null)
        {
            return;
        }

        Data.LoanInterestBindingPeriods.RemoveAll(item => item.Id == periodId);
        await RecalculateLoanCategoryCostsAsync(period.LoanId);
        await LogActivityAsync(
            BuildLogDescription(
                Data.HousingLoans.Any(loan => loan.Id == period.LoanId) ? _localizer["nav.housingTab"] : _localizer["nav.transport"],
                Data.HousingLoans.Any(loan => loan.Id == period.LoanId) ? _localizer["housing.loansTab"] : _localizer["transport.loansTab"]),
            BuildLogActivity("log.verb.deleted", "log.entity.bindingPeriod", FormatMonthRange(period.StartMonth, period.EndMonth)));
    }

    public async Task RemoveLoanAmortizationPlanAsync(Guid planId)
    {
        var plan = Data.LoanAmortizationPlans.FirstOrDefault(item => item.Id == planId);
        if (plan is null)
        {
            return;
        }

        Data.LoanAmortizationPlans.RemoveAll(item => item.Id == planId);
        Data.ExpenseRecords.RemoveAll(expense => expense.LoanAmortizationPlanId == planId);
        await RecalculateLoanCategoryCostsAsync(plan.LoanId);
        await LogActivityAsync(
            BuildLogDescription(
                Data.HousingLoans.Any(loan => loan.Id == plan.LoanId) ? _localizer["nav.housingTab"] : _localizer["nav.transport"],
                Data.HousingLoans.Any(loan => loan.Id == plan.LoanId) ? _localizer["housing.loansTab"] : _localizer["transport.loansTab"]),
            BuildLogActivity("log.verb.deleted", "log.entity.amortizationPlan", FormatMonthRange(plan.StartDate, plan.EndDate)));
    }

    public async Task RemoveTransportLoanAsync(Guid loanId)
    {
        var loan = Data.TransportLoans.FirstOrDefault(item => item.Id == loanId);
        Data.TransportLoans.RemoveAll(item => item.Id == loanId);
        Data.LoanInterestBindingPeriods.RemoveAll(period => period.LoanId == loanId);
        Data.LoanAmortizationPlans.RemoveAll(plan => plan.LoanId == loanId);
        Data.ExpenseRecords.RemoveAll(expense => expense.LoanId == loanId && expense.Category == ExpenseCategory.Transport);
        RemoveAllMonthlyBalances(loanId);
        await RecalculateTransportCostsAsync();
        await LogActivityAsync(
            BuildLogDescription(_localizer["nav.transport"], _localizer["transport.loansTab"]),
            BuildLogActivity("log.verb.deleted", "log.entity.transportLoan", loan?.Lender ?? loanId.ToString()));
    }

    public async Task RemoveTransportLeasingContractAsync(Guid contractId)
    {
        var contract = Data.TransportLeasingContracts.FirstOrDefault(item => item.Id == contractId);
        Data.TransportLeasingContracts.RemoveAll(item => item.Id == contractId);
        Data.ExpenseRecords.RemoveAll(expense => expense.TransportLeasingContractId == contractId);
        await RecalculateTransportCostsAsync();
        await LogActivityAsync(
            BuildLogDescription(_localizer["nav.transport"], _localizer["transport.leasingTab"]),
            BuildLogActivity("log.verb.deleted", "log.entity.leasingContract", contract?.Id.ToString() ?? contractId.ToString()));
    }

    public async Task RemoveTransportVehicleAsync(Guid vehicleId)
    {
        var vehicle = Data.TransportVehicles.FirstOrDefault(item => item.Id == vehicleId);
        if (vehicle is null)
        {
            return;
        }

        var isInUse =
            Data.TransportLoans.Any(loan => loan.VehicleId == vehicleId) ||
            Data.TransportLeasingContracts.Any(contract => contract.VehicleId == vehicleId) ||
            Data.TransportDefinitions.Any(definition => definition.VehicleId == vehicleId) ||
            Data.ExpenseRecords.Any(expense => expense.TransportVehicleId == vehicleId);

        if (isInUse)
        {
            throw new InvalidOperationException("The vehicle is still linked to transport records.");
        }

        Data.Assets.RemoveAll(asset =>
            asset.TransportVehicleId == vehicleId ||
            (vehicle.AssetId.HasValue && asset.Id == vehicle.AssetId.Value));

        Data.TransportVehicles.RemoveAll(item => item.Id == vehicleId);
        await PersistAndNotifyAndLogAsync(
            BuildLogDescription(_localizer["nav.transport"], _localizer["transport.vehiclesTab"]),
            BuildLogActivity("log.verb.deleted", "log.entity.vehicle", vehicle.Name));
    }

    public async Task RemoveCreditAsync(Guid creditId)
    {
        var credit = Data.Credits.FirstOrDefault(item => item.Id == creditId);
        Data.Credits.RemoveAll(item => item.Id == creditId);
        Data.ExpenseRecords.RemoveAll(expense => expense.CreditId == creditId);
        RemoveAllMonthlyBalances(creditId);
        await PersistAndNotifyAndLogAsync(
            BuildLogDescription(_localizer["nav.credits"], _localizer["credits.manageTab"]),
            BuildLogActivity("log.verb.deleted", "log.entity.credit", credit?.Name ?? creditId.ToString()));
    }

    public async Task RemoveSavingsAccountAsync(Guid accountId)
    {
        var account = Data.SavingsAccounts.FirstOrDefault(item => item.Id == accountId);
        Data.SavingsAccounts.RemoveAll(item => item.Id == accountId);
        Data.SavingsReturnPeriods.RemoveAll(period => period.SavingsAccountId == accountId);
        Data.SavingsBalanceAdjustments.RemoveAll(item => item.SavingsAccountId == accountId);
        Data.SavingsGeneratedReturns.RemoveAll(item => item.SavingsAccountId == accountId);
        Data.ExpenseRecords.RemoveAll(expense => expense.SavingsAccountId == accountId);
        Data.IncomeRecords.RemoveAll(income => income.SavingsAccountId == accountId);
        RemoveAllMonthlyBalances(accountId);
        await RecalculateSavingsBalancesAsync();
        if (account is not null)
        {
            await LogActivityAsync(
                BuildLogDescription(_localizer["nav.savings"], GetSavingsTabTitle(account.AccountType)),
                BuildLogActivity("log.verb.deleted", "log.entity.savingsAccount", account.AccountName));
        }
    }

    public async Task RemoveSavingsReturnPeriodAsync(Guid periodId)
    {
        var period = Data.SavingsReturnPeriods.FirstOrDefault(item => item.Id == periodId);
        if (period is null)
        {
            return;
        }

        Data.SavingsReturnPeriods.RemoveAll(item => item.Id == periodId);
        await RecalculateSavingsBalancesAsync();
        var account = GetSavingsAccount(period.SavingsAccountId);
        await LogActivityAsync(
            BuildLogDescription(_localizer["nav.savings"], GetSavingsTabTitle(account.AccountType)),
            BuildLogActivity("log.verb.deleted", "log.entity.savingsPeriod", account.AccountName));
    }

    public async Task RemoveIncomeAsync(Guid incomeId, bool removeSeries)
    {
        var income = Data.IncomeRecords.FirstOrDefault(entry => entry.Id == incomeId);
        if (income is null)
        {
            return;
        }

        var activity = removeSeries && income.SeriesId.HasValue
            ? BuildLogActivity("log.verb.deleted", "log.entity.incomeSeries", BuildIncomeLogSubject(income.MemberId, income.Type))
            : BuildLogActivity("log.verb.deleted", "log.entity.income", BuildIncomeLogSubject(income.MemberId, income.Type));

        if (removeSeries && income.SeriesId.HasValue)
        {
            Data.IncomeRecords.RemoveAll(entry => entry.SeriesId == income.SeriesId);
            Data.SalaryIncomePeriods.RemoveAll(period => period.SeriesId == income.SeriesId.Value);
        }
        else
        {
            Data.IncomeRecords.RemoveAll(entry => entry.Id == incomeId);
        }

        if (income.SeriesId.HasValue && !Data.IncomeRecords.Any(entry => entry.SeriesId == income.SeriesId))
        {
            Data.SalaryIncomePeriods.RemoveAll(period => period.SeriesId == income.SeriesId.Value);
        }

        if (income.SavingsAccountId.HasValue)
        {
            await RecalculateSavingsBalancesAsync();
            await LogActivityAsync(
                BuildLogDescription(_localizer["nav.household"], _localizer["household.incomesTab"]),
                activity);
            return;
        }

        await PersistAndNotifyAndLogAsync(
            BuildLogDescription(_localizer["nav.household"], _localizer["household.incomesTab"]),
            activity);
    }

    public IReadOnlyList<SalaryIncomePeriodDisplay> GetSalaryIncomePeriods()
    {
        return Data.SalaryIncomePeriods
            .Select(period => new SalaryIncomePeriodDisplay
            {
                Id = period.Id,
                SeriesId = period.SeriesId,
                MemberId = period.MemberId,
                MemberName = string.IsNullOrWhiteSpace(period.MemberName)
                    ? GetMemberNameOrUnknown(period.MemberId)
                    : period.MemberName.Trim(),
                MonthlyAmount = period.MonthlyAmount,
                StartYear = period.StartYear,
                StartMonth = period.StartMonth,
                EndYear = period.EndYear,
                EndMonth = period.EndMonth
            })
            .OrderByDescending(period => period.StartYear)
            .ThenByDescending(period => period.StartMonth)
            .ThenBy(period => period.MemberName)
            .ToList();
    }

    public async Task RemoveSalaryIncomePeriodAsync(Guid periodId)
    {
        var period = Data.SalaryIncomePeriods.FirstOrDefault(item => item.Id == periodId);
        if (period is null)
        {
            return;
        }

        Data.SalaryIncomePeriods.RemoveAll(item => item.Id == periodId);
        Data.IncomeRecords.RemoveAll(entry => entry.SeriesId == period.SeriesId);

        await PersistAndNotifyAndLogAsync(
            BuildLogDescription(_localizer["nav.household"], _localizer["household.salaryTab"]),
            BuildLogActivity("log.verb.deleted", "log.entity.salaryPeriod", period.MemberName));
    }

    private async Task RegenerateSubscriptionExpensesAsync()
    {
        await GenerateMissingSubscriptionExpensesAsync();
    }

    public string FormatCurrency(decimal amount)
    {
        return amount.ToString("C", CreateNumberFormat());
    }

    private async Task PersistAndNotifyAsync(bool captureUndoSnapshot = true)
    {
        if (captureUndoSnapshot)
        {
            _undoService.CaptureBeforeSave();
        }

        var json = _localStorageService.Serialize(Data);
        await _localStorageService.SaveJsonAsync(ApplicationDataKey, json);
        _undoService.CommitSavedState(json);
        Changed?.Invoke();
    }

    private async Task PersistAndNotifyAndLogAsync(string description, string activity)
    {
        await PersistAndNotifyAsync();
        await LogActivityAsync(description, activity);
    }

    private async Task LogActivityAsync(string description, string activity)
    {
        await _applicationLogService.AddEntryAsync(description, activity, _localizer["log.status.ok"]);
    }

    private string BuildLogDescription(params string[] parts) =>
        string.Join("-", parts.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part.Trim()));

    private string BuildLogActivity(string verbKey, string entityKey, string? subject = null)
    {
        var parts = new List<string>
        {
            _localizer[verbKey],
            _localizer[entityKey]
        };

        if (!string.IsNullOrWhiteSpace(subject))
        {
            parts.Add(subject.Trim());
        }

        return string.Join(" ", parts);
    }

    private string BuildIncomeLogSubject(Guid memberId, string type) =>
        $"{GetMemberNameOrUnknown(memberId)} / {GetIncomeTypeDisplay(type)}";

    private string GetExpenseActivitySubject(string subcategory, string description) =>
        string.IsNullOrWhiteSpace(description)
            ? GetLocalizedExpenseLabel(subcategory)
            : description.Trim();

    private string FormatMonthRange(DateOnly startDate, DateOnly endDate) =>
        $"{NormalizeMonth(startDate):yyyy-MM} - {NormalizeMonth(endDate):yyyy-MM}";

    private string GetSavingsTabTitle(SavingsAccountType accountType) => accountType switch
    {
        SavingsAccountType.Bank => _localizer["savings.bankTab"],
        SavingsAccountType.Fund => _localizer["savings.fundsTab"],
        SavingsAccountType.Stock => _localizer["savings.stocksTab"],
        _ => _localizer["savings.bankTab"]
    };

    private void NormalizeUpcomingExpensesPreferences()
    {
        Data.UpcomingExpensesMonths = Data.UpcomingExpensesMonths switch
        {
            1 or 3 or 6 or 12 or ApplicationData.UpcomingExpensesShowAllMonths => Data.UpcomingExpensesMonths,
            _ => 3
        };

        if (Data.UpcomingExpensesMinimumAmount <= 0m)
        {
            Data.UpcomingExpensesMinimumAmount = null;
        }
    }

    private void NormalizeData()
    {
        Data.Members ??= [];
        Data.IncomeRecords ??= [];
        Data.SalaryIncomePeriods ??= [];
        Data.SubcategoryDefinitions ??= [];
        Data.ImportedExpenseCategorySuggestions ??= [];
        Data.ExpenseRecords ??= [];
        Data.Subscriptions ??= [];
        Data.HousingDefinitions ??= [];
        Data.HousingLoans ??= [];
        Data.HomeResidence ??= null;
        Data.TransportDefinitions ??= [];
        Data.TransportVehicles ??= [];
        Data.TransportLoans ??= [];
        Data.LoanInterestBindingPeriods ??= [];
        Data.LoanAmortizationPlans ??= [];
        Data.MonthlyBalances ??= [];
        Data.ClosedMonths ??= [];
        Data.TransportLeasingContracts ??= [];
        Data.Credits ??= [];
        Data.Debts ??= [];
        Data.SavingsAccounts ??= [];
        Data.SavingsReturnPeriods ??= [];
        Data.SavingsBalanceAdjustments ??= [];
        Data.SavingsGeneratedReturns ??= [];
        Data.Savings ??= [];
        Data.Assets ??= [];

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
            Data.ThemeMode = AppThemeNames.Standard;
        }
        else
        {
            Data.ThemeMode = AppThemeNames.Normalize(Data.ThemeMode);
        }

        NormalizeUpcomingExpensesPreferences();

        foreach (var income in Data.IncomeRecords)
        {
            if (string.IsNullOrWhiteSpace(income.Type))
            {
                income.Type = OtherIncomeType;
            }

            income.Metadata ??= string.Empty;
        }

        foreach (var salaryPeriod in Data.SalaryIncomePeriods)
        {
            salaryPeriod.MemberName = string.IsNullOrWhiteSpace(salaryPeriod.MemberName)
                ? GetMemberNameOrUnknown(salaryPeriod.MemberId)
                : salaryPeriod.MemberName.Trim();
        }

        foreach (var loan in Data.HousingLoans)
        {
            loan.Name = string.IsNullOrWhiteSpace(loan.Name) ? loan.Lender : loan.Name.Trim();
            RefreshLoanDerivedValues(loan.Id);
        }

        foreach (var loan in Data.TransportLoans)
        {
            RefreshTransportLoanDerivedValues(loan.Id);
        }

        foreach (var expense in Data.ExpenseRecords)
        {
            expense.Metadata ??= string.Empty;
        }

        NormalizeSubcategoryDefinitions();
        NormalizeImportedExpenseCategorySuggestions();
        NormalizeClosedMonths();

        foreach (var asset in Data.Assets)
        {
            asset.Name = asset.Name?.Trim() ?? string.Empty;
            asset.Type = string.IsNullOrWhiteSpace(asset.Type)
                ? asset.TransportVehicleId.HasValue ? VehicleAssetType : asset.HomeResidenceId.HasValue ? HousingAssetType : OtherIncomeType
                : asset.Type.Trim();
            asset.Description ??= string.Empty;

            if (asset.PurchaseValue == 0m && asset.Amount > 0m)
            {
                asset.PurchaseValue = asset.Amount;
            }

            if (asset.Amount == 0m && asset.PurchaseValue > 0m)
            {
                asset.Amount = asset.PurchaseValue;
            }

            if (asset.EstimatedSaleValue == 0m && asset.PurchaseValue > 0m)
            {
                asset.EstimatedSaleValue = asset.PurchaseValue;
            }

            if (asset.AcquisitionDate == default)
            {
                asset.AcquisitionDate = DateOnly.FromDateTime(DateTime.Today);
            }

            if (asset.HomeResidenceId.HasValue)
            {
                asset.SourceType = AssetSourceType.HomeResidence;
            }
            else if (asset.TransportVehicleId.HasValue)
            {
                asset.SourceType = AssetSourceType.TransportVehicle;
            }
        }

        if (Data.HomeResidence is not null && Data.HomeResidence.Id == Guid.Empty)
        {
            Data.HomeResidence.Id = Guid.NewGuid();
        }

        SyncHomeResidenceAssetInMemory();

        foreach (var credit in Data.Credits)
        {
            credit.Name = string.IsNullOrWhiteSpace(credit.Name) ? credit.Provider : credit.Name.Trim();
            RefreshCreditDerivedValues(credit.Id);
        }

        foreach (var account in Data.SavingsAccounts)
        {
            if (string.IsNullOrWhiteSpace(account.AccountName))
            {
                account.AccountName = DefaultSavingsAccountName;
            }
        }

        MigrateLegacyLoanBindingPeriods();
        RebuildMonthlyBalancesInMemory();
    }

    private void ApplyLoadedData(ApplicationData data, bool initializeUndoState)
    {
        Data = data;
        NormalizeData();

        if (initializeUndoState)
        {
            _undoService.InitializeCurrentState(_localStorageService.Serialize(Data));
        }
    }

    public decimal GetLoanCurrentBalance(Guid loanId) => GetCurrentOrLatestBalance(loanId);

    public decimal GetCreditCurrentBalance(Guid creditId) => GetCurrentOrLatestBalance(creditId);

    public decimal GetSavingsAccountCurrentBalance(Guid accountId) => GetCurrentOrLatestBalance(accountId);

    public decimal GetCreditInterestCostsForMonth(DateOnly month)
    {
        var normalizedMonth = NormalizeMonth(month);
        return Data.ExpenseRecords
            .Where(expense =>
                expense.CreditId.HasValue &&
                NormalizeMonth(new DateOnly(expense.Year, expense.Month, 1)) == normalizedMonth &&
                expense.CreditCostSource == CreditCostSource.GeneratedInterest)
            .Sum(expense => expense.Amount);
    }

    public IReadOnlyList<DateOnly> GetUnpostedCreditMonths(Guid creditId)
    {
        var credit = GetCredit(creditId);
        var startMonth = NormalizeMonth(credit.StartDate);
        var currentMonth = GetCurrentMonth().ToDateOnly();

        return EnumerateMonths(startMonth.Year, startMonth.Month, currentMonth.Year, currentMonth.Month)
            .Select(month => month.ToDateOnly())
            .Where(month => !IsCreditMonthPosted(creditId, month))
            .OrderBy(month => month)
            .ToList();
    }

    public IReadOnlyList<MonthlyBalance> GetMonthlyBalances(Guid entityId) =>
        Data.MonthlyBalances
            .Where(item => item.EntityId == entityId)
            .OrderByDescending(item => item.Year)
            .ThenByDescending(item => item.Month)
            .ToList();

    private void MigrateLegacyLoanBindingPeriods()
    {
        foreach (var period in Data.LoanInterestBindingPeriods)
        {
            if (period.StartMonth == default)
            {
                var legacyStart = period.StartDate == default ? DateOnly.FromDateTime(DateTime.Today) : period.StartDate;
                period.StartMonth = NormalizeMonth(legacyStart);
            }

            if (period.EndMonth == default)
            {
                var legacyEnd = period.EndDate == default ? period.StartMonth : period.EndDate;
                period.EndMonth = NormalizeMonth(legacyEnd);
            }

            period.StartDate = period.StartMonth;
            period.EndDate = period.EndMonth;
        }
    }

    private void RebuildMonthlyBalancesInMemory()
    {
        Data.MonthlyBalances.RemoveAll(_ => true);

        foreach (var loan in Data.HousingLoans)
        {
            SetMonthlyBalance(loan.Id, NormalizeMonth(loan.LoanStartDate), loan.StartingRemainingDebt);
        }

        foreach (var loan in Data.TransportLoans)
        {
            SetMonthlyBalance(loan.Id, NormalizeMonth(loan.LoanStartDate), loan.StartingRemainingDebt);
        }

        foreach (var credit in Data.Credits)
        {
            SetMonthlyBalance(credit.Id, NormalizeMonth(credit.StartDate), credit.StartingRemainingDebt);
        }

        foreach (var account in Data.SavingsAccounts)
        {
            SetMonthlyBalance(account.Id, NormalizeMonth(account.CreatedDate), account.OpeningBalance);
        }

        if (TryGetHousingLoanCalculationRange(out var housingStart, out var housingEnd))
        {
            _ = RecalculateLoanBalances(ExpenseCategory.Housing, Data.HousingLoans, housingStart, housingEnd, loan => loan.Id, loan => loan.Lender, _ => null);
        }

        if (TryGetTransportCalculationRange(out var transportStart, out var transportEnd))
        {
            _ = RecalculateLoanBalances(ExpenseCategory.Transport, Data.TransportLoans, transportStart, transportEnd, loan => loan.Id, loan => loan.Lender, loan => loan.VehicleId);
        }

        foreach (var credit in Data.Credits)
        {
            var start = NormalizeMonth(credit.StartDate);
            RemoveMonthlyBalancesFrom(credit.Id, start);
            var balance = credit.StartingRemainingDebt;
            foreach (var month in EnumerateMonths(start.Year, start.Month, GetGenerationLimit().Year, GetGenerationLimit().Month))
            {
                var monthDate = month.ToDateOnly();
                balance += Data.ExpenseRecords
                    .Where(expense =>
                        expense.CreditId == credit.Id &&
                        NormalizeMonth(new DateOnly(expense.Year, expense.Month, 1)) == monthDate &&
                        expense.CreditCostSource is CreditCostSource.ManualPurchase or CreditCostSource.GeneratedInterest)
                    .Sum(expense => expense.Amount);
                balance -= Data.ExpenseRecords
                    .Where(expense =>
                        expense.CreditId == credit.Id &&
                        NormalizeMonth(new DateOnly(expense.Year, expense.Month, 1)) == monthDate &&
                        expense.CreditCostSource is CreditCostSource.ManualAmortization or CreditCostSource.GeneratedResetAmortization)
                    .Sum(expense => expense.Amount);
                SetMonthlyBalance(credit.Id, monthDate, Math.Max(0m, balance));
            }
        }

        foreach (var account in Data.SavingsAccounts)
        {
            var start = NormalizeMonth(account.CreatedDate);
            RemoveMonthlyBalancesFrom(account.Id, start);
            var balance = account.OpeningBalance;
            foreach (var month in EnumerateMonths(start.Year, start.Month, GetGenerationLimit().Year, GetGenerationLimit().Month))
            {
                var monthDate = month.ToDateOnly();
                balance += GetSavingsManualNetChangeForMonth(account.Id, monthDate);
                balance += Data.SavingsGeneratedReturns
                    .Where(item => item.SavingsAccountId == account.Id && item.Year == month.Year && item.Month == month.Month)
                    .Sum(item => item.Amount);
                SetMonthlyBalance(account.Id, monthDate, Math.Max(0m, balance));
            }
        }
    }

    private void SetMonthlyBalance(Guid entityId, DateOnly month, decimal balance)
    {
        var normalizedMonth = NormalizeMonth(month);
        Data.MonthlyBalances.RemoveAll(item =>
            item.EntityId == entityId &&
            item.Year == normalizedMonth.Year &&
            item.Month == normalizedMonth.Month);

        Data.MonthlyBalances.Add(new MonthlyBalance
        {
            EntityId = entityId,
            Year = normalizedMonth.Year,
            Month = normalizedMonth.Month,
            Balance = Math.Max(0m, balance)
        });
    }

    private void RemoveMonthlyBalancesFrom(Guid entityId, DateOnly startMonth)
    {
        var normalizedMonth = NormalizeMonth(startMonth);
        Data.MonthlyBalances.RemoveAll(item =>
            item.EntityId == entityId &&
            NormalizeMonth(new DateOnly(item.Year, item.Month, 1)) >= normalizedMonth);
    }

    private void RemoveMonthlyBalancesInRange(Guid entityId, DateOnly startMonth, DateOnly endMonth)
    {
        var normalizedStart = NormalizeMonth(startMonth);
        var normalizedEnd = NormalizeMonth(endMonth);
        Data.MonthlyBalances.RemoveAll(item =>
            item.EntityId == entityId &&
            NormalizeMonth(new DateOnly(item.Year, item.Month, 1)) >= normalizedStart &&
            NormalizeMonth(new DateOnly(item.Year, item.Month, 1)) <= normalizedEnd);
    }

    private void RemoveAllMonthlyBalances(Guid entityId)
    {
        Data.MonthlyBalances.RemoveAll(item => item.EntityId == entityId);
    }

    private decimal GetCurrentOrLatestBalance(Guid entityId)
    {
        var currentMonth = GetCurrentMonth().ToDateOnly();
        var current = Data.MonthlyBalances.FirstOrDefault(item =>
            item.EntityId == entityId &&
            item.Year == currentMonth.Year &&
            item.Month == currentMonth.Month);

        if (current is not null)
        {
            return current.Balance;
        }

        return GetBalanceAtOrBeforeMonth(entityId, currentMonth);
    }

    private decimal GetBalanceAtOrBeforeMonth(Guid entityId, DateOnly month)
    {
        var normalizedMonth = NormalizeMonth(month);
        return Data.MonthlyBalances
            .Where(item =>
                item.EntityId == entityId &&
                NormalizeMonth(new DateOnly(item.Year, item.Month, 1)) <= normalizedMonth)
            .OrderByDescending(item => item.Year)
            .ThenByDescending(item => item.Month)
            .Select(item => item.Balance)
            .FirstOrDefault();
    }

    private decimal GetBalanceAtMonth(Guid entityId, DateOnly month)
    {
        var normalizedMonth = NormalizeMonth(month);
        return Data.MonthlyBalances
            .Where(item =>
                item.EntityId == entityId &&
                item.Year == normalizedMonth.Year &&
                item.Month == normalizedMonth.Month)
            .Select(item => item.Balance)
            .FirstOrDefault();
    }

    private decimal GetLatestBalanceBeforeMonth(Guid entityId, DateOnly month)
    {
        var normalizedMonth = NormalizeMonth(month);
        return Data.MonthlyBalances
            .Where(item =>
                item.EntityId == entityId &&
                NormalizeMonth(new DateOnly(item.Year, item.Month, 1)) < normalizedMonth)
            .OrderByDescending(item => item.Year)
            .ThenByDescending(item => item.Month)
            .Select(item => item.Balance)
            .FirstOrDefault();
    }

    private DateOnly GetLoanStartMonth(Guid loanId)
    {
        var housingLoan = Data.HousingLoans.FirstOrDefault(item => item.Id == loanId);
        if (housingLoan is not null)
        {
            return NormalizeMonth(housingLoan.LoanStartDate);
        }

        var transportLoan = Data.TransportLoans.FirstOrDefault(item => item.Id == loanId);
        if (transportLoan is not null)
        {
            return NormalizeMonth(transportLoan.LoanStartDate);
        }

        return GetGenerationLimit();
    }

    private decimal GetLoanStartingBalance(Guid loanId)
    {
        var housingLoan = Data.HousingLoans.FirstOrDefault(item => item.Id == loanId);
        if (housingLoan is not null)
        {
            return housingLoan.StartingRemainingDebt;
        }

        var transportLoan = Data.TransportLoans.FirstOrDefault(item => item.Id == loanId);
        return transportLoan?.StartingRemainingDebt ?? 0m;
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
        numberFormat.CurrencyDecimalDigits = 0;
        numberFormat.CurrencyGroupSizes = [3];
        numberFormat.CurrencyPositivePattern = Data.CurrencyCode == "SEK" ? 3 : 0;
        numberFormat.CurrencyNegativePattern = Data.CurrencyCode == "SEK" ? 8 : 1;
        return numberFormat;
    }

    private IEnumerable<ExpenseEntry> GetScopedExpenseEntries(ExpenseQueryScope scope) =>
        scope switch
        {
            ExpenseQueryScope.Housing => Data.ExpenseRecords.Where(entry => entry.Category == ExpenseCategory.Housing),
            ExpenseQueryScope.Transport => Data.ExpenseRecords.Where(entry => entry.Category == ExpenseCategory.Transport),
            ExpenseQueryScope.Credits => Data.ExpenseRecords.Where(entry => entry.Category == ExpenseCategory.Credits),
            ExpenseQueryScope.Savings => Data.ExpenseRecords.Where(entry => entry.Category == ExpenseCategory.Savings),
            ExpenseQueryScope.Assets => Data.ExpenseRecords.Where(entry => entry.Category == ExpenseCategory.Assets),
            ExpenseQueryScope.ExpensesOnly => Data.ExpenseRecords.Where(entry =>
                entry.Category != ExpenseCategory.Housing &&
                entry.Category != ExpenseCategory.Transport &&
                entry.Category != ExpenseCategory.Credits &&
                entry.Category != ExpenseCategory.Savings &&
                entry.Category != ExpenseCategory.Assets),
            _ => Data.ExpenseRecords
        };

    private IEnumerable<ExpenseEntry> ApplyExpenseTableFilter(IEnumerable<ExpenseEntry> source, ExpenseTableFilter filter, bool includeCategoryFilter, bool includeSubcategoryFilter)
    {
        var query = source;

        if (!string.IsNullOrWhiteSpace(filter.Name))
        {
            var nameFilter = filter.Name.Trim();
            query = query.Where(entry =>
                GetExpenseDescriptionDisplay(entry).Contains(nameFilter, StringComparison.OrdinalIgnoreCase) ||
                entry.Metadata.Contains(nameFilter, StringComparison.OrdinalIgnoreCase) ||
                GetExpenseSubcategoryDisplay(entry).Contains(nameFilter, StringComparison.OrdinalIgnoreCase) ||
                entry.Subcategory.Contains(nameFilter, StringComparison.OrdinalIgnoreCase));
        }

        DateOnly? normalizedStart = filter.StartDate.HasValue ? NormalizeMonth(filter.StartDate.Value) : null;
        DateOnly? normalizedEnd = filter.EndDate.HasValue ? NormalizeMonth(filter.EndDate.Value) : null;
        if (normalizedStart.HasValue && normalizedEnd.HasValue && normalizedEnd < normalizedStart)
        {
            (normalizedStart, normalizedEnd) = (normalizedEnd, normalizedStart);
        }

        if (normalizedStart.HasValue)
        {
            query = query.Where(entry => NormalizeMonth(new DateOnly(entry.Year, entry.Month, 1)) >= normalizedStart.Value);
        }

        if (normalizedEnd.HasValue)
        {
            query = query.Where(entry => NormalizeMonth(new DateOnly(entry.Year, entry.Month, 1)) <= normalizedEnd.Value);
        }

        if (includeCategoryFilter && !string.IsNullOrWhiteSpace(filter.CategoryFilter))
        {
            var categoryFilter = filter.CategoryFilter.Trim();
            if (Enum.TryParse<ExpenseCategory>(categoryFilter, ignoreCase: true, out var parsedCategory))
            {
                query = query.Where(entry => entry.Category == parsedCategory);
            }
        }

        if (includeSubcategoryFilter && !string.IsNullOrWhiteSpace(filter.SubcategoryFilter))
        {
            var subcategoryFilter = filter.SubcategoryFilter.Trim();
            query = query.Where(entry => string.Equals(entry.Subcategory.Trim(), subcategoryFilter, StringComparison.OrdinalIgnoreCase));
        }

        return query;
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

    private static IEnumerable<MonthKey> EnumerateOccurrences(int startYear, int startMonth, int endYear, int endMonth, int intervalMonths)
    {
        var current = new DateOnly(startYear, startMonth, 1);
        var end = new DateOnly(endYear, endMonth, 1);

        while (current <= end)
        {
            yield return new MonthKey(current.Year, current.Month);
            current = current.AddMonthsSafe(intervalMonths);
        }
    }

    private static DateOnly NormalizeMonth(DateOnly date) => new(date.Year, date.Month, 1);

    private static bool ScopeUsesSubcategoryFilterOnly(ExpenseQueryScope scope) =>
        scope is ExpenseQueryScope.Housing or ExpenseQueryScope.Transport or ExpenseQueryScope.Credits or ExpenseQueryScope.Savings or ExpenseQueryScope.Assets;

    private static string BuildCombinedCategoryFilterValue(ExpenseCategory category, string subcategory) =>
        $"{category}|{subcategory.Trim()}";

    private static bool CoversMonth(DateOnly startDate, DateOnly endDate, DateOnly month) =>
        NormalizeMonth(startDate) <= month && month <= NormalizeMonth(endDate);

    private static bool OverlapsByCoveredMonth(DateOnly startDate, DateOnly endDate, DateOnly otherStartDate, DateOnly otherEndDate) =>
        NormalizeMonth(startDate) <= NormalizeMonth(otherEndDate) && NormalizeMonth(otherStartDate) <= NormalizeMonth(endDate);

    private bool TryGetHousingLoanCalculationRange(out DateOnly startDate, out DateOnly endDate)
    {
        var startCandidates = Data.HousingLoans
            .Select(loan => loan.LoanStartDate)
            .Concat(Data.LoanInterestBindingPeriods
                .Where(period => Data.HousingLoans.Any(loan => loan.Id == period.LoanId))
                .Select(period => period.StartMonth))
            .ToList();

        if (startCandidates.Count == 0)
        {
            startDate = default;
            endDate = default;
            return false;
        }

        var endCandidates = Data.LoanInterestBindingPeriods
            .Where(period => Data.HousingLoans.Any(loan => loan.Id == period.LoanId))
            .Select(period => period.EndMonth)
            .ToList();

        if (endCandidates.Count == 0)
        {
            startDate = default;
            endDate = default;
            return false;
        }

        startDate = NormalizeMonth(startCandidates.Min());
        endDate = NormalizeMonth(endCandidates.Max());
        return true;
    }

    private bool TryGetTransportCalculationRange(out DateOnly startDate, out DateOnly endDate)
    {
        var transportLoanIds = Data.TransportLoans.Select(loan => loan.Id).ToHashSet();

        var startCandidates = Data.TransportLoans
            .Select(loan => loan.LoanStartDate)
            .Concat(Data.LoanInterestBindingPeriods
                .Where(period => transportLoanIds.Contains(period.LoanId))
                .Select(period => period.StartMonth))
            .Concat(Data.TransportLeasingContracts.Select(contract => contract.StartDate))
            .ToList();

        if (startCandidates.Count == 0)
        {
            startDate = default;
            endDate = default;
            return false;
        }

        var endCandidates = Data.LoanInterestBindingPeriods
            .Where(period => transportLoanIds.Contains(period.LoanId))
            .Select(period => period.EndMonth)
            .Concat(Data.TransportLeasingContracts.Select(contract => contract.EndDate))
            .ToList();

        if (endCandidates.Count == 0)
        {
            startDate = default;
            endDate = default;
            return false;
        }

        startDate = NormalizeMonth(startCandidates.Min());
        endDate = NormalizeMonth(endCandidates.Max());
        return true;
    }

    private bool TryGetCreditCalculationRange(out DateOnly startDate, out DateOnly endDate)
    {
        var startCandidates = Data.Credits
            .Select(credit => credit.StartDate)
            .Concat(Data.ExpenseRecords
                .Where(expense => expense.CreditId.HasValue)
                .Select(expense => new DateOnly(expense.Year, expense.Month, 1)))
            .ToList();

        if (startCandidates.Count == 0)
        {
            startDate = default;
            endDate = default;
            return false;
        }

        var endCandidates = Data.ExpenseRecords
            .Where(expense => expense.CreditId.HasValue)
            .Select(expense => new DateOnly(expense.Year, expense.Month, 1))
            .Append(GetGenerationLimit())
            .ToList();

        startDate = NormalizeMonth(startCandidates.Min());
        endDate = NormalizeMonth(endCandidates.Max());
        return true;
    }

    private void ValidateAnyLoanExists(Guid loanId)
    {
        var housingExists = Data.HousingLoans.Any(loan => loan.Id == loanId);
        var transportExists = Data.TransportLoans.Any(loan => loan.Id == loanId);
        if (!housingExists && !transportExists)
        {
            throw new InvalidOperationException("The selected loan does not exist.");
        }
    }

    private void RefreshAnyLoanDerivedValues(Guid loanId)
    {
        if (Data.HousingLoans.Any(loan => loan.Id == loanId))
        {
            RefreshLoanDerivedValues(loanId);
        }

        if (Data.TransportLoans.Any(loan => loan.Id == loanId))
        {
            RefreshTransportLoanDerivedValues(loanId);
        }
    }

    private static void ValidateNoMonthOverlap(DateOnly startDate, DateOnly endDate, IEnumerable<(DateOnly StartDate, DateOnly EndDate)> existingRanges)
    {
        foreach (var range in existingRanges)
        {
            if (OverlapsByCoveredMonth(startDate, endDate, range.StartDate, range.EndDate))
            {
                throw new InvalidOperationException("The selected month range overlaps an existing period.");
            }
        }
    }

    private ExpenseEntry CreateLoanExpenseEntry(
        Guid loanId,
        string lender,
        ExpenseCategory category,
        MonthKey month,
        string subcategory,
        decimal amount,
        Guid? interestBindingPeriodId,
        Guid? amortizationPlanId,
        string? metadata,
        Guid? transportVehicleId,
        bool loanBalanceNeutral = false) => new()
        {
            Amount = amount,
            Year = month.Year,
            Month = month.Month,
            Category = category,
            Subcategory = subcategory,
            Metadata = metadata ?? string.Empty,
            Description = string.Empty,
            LoanId = loanId,
            LoanInterestBindingPeriodId = interestBindingPeriodId,
            LoanAmortizationPlanId = amortizationPlanId,
            LoanBalanceNeutral = loanBalanceNeutral,
            TransportVehicleId = category == ExpenseCategory.Transport ? transportVehicleId : null
        };

    private int RecalculateLoanBalances<TLoan>(
        ExpenseCategory category,
        IEnumerable<TLoan> loans,
        DateOnly startDate,
        DateOnly endDate,
        Func<TLoan, Guid> getLoanId,
        Func<TLoan, string> getLender,
        Func<TLoan, Guid?> getVehicleId)
        where TLoan : class
    {
        var normalizedStart = NormalizeMonth(startDate);
        var normalizedEnd = NormalizeMonth(endDate);

        if (normalizedEnd < normalizedStart)
        {
            (normalizedStart, normalizedEnd) = (normalizedEnd, normalizedStart);
        }

        RemoveGeneratedExpensesInRange(category, normalizedStart, normalizedEnd, includeLeasing: false);

        var generatedCount = 0;

        foreach (var loan in loans)
        {
            var loanId = getLoanId(loan);
            var loanStartMonth = GetLoanStartMonth(loanId);
            if (loanStartMonth > normalizedEnd)
            {
                continue;
            }

            var calculationStart = normalizedStart < loanStartMonth ? loanStartMonth : normalizedStart;
            var lender = getLender(loan);
            var vehicleId = getVehicleId(loan);
            var metadata = category == ExpenseCategory.Transport ? GetTransportVehicleName(vehicleId) : null;

            RemoveMonthlyBalancesInRange(loanId, calculationStart, normalizedEnd);

            foreach (var month in EnumerateMonths(calculationStart.Year, calculationStart.Month, normalizedEnd.Year, normalizedEnd.Month))
            {
                var monthDate = month.ToDateOnly();
                var period = GetActiveLoanInterestBindingPeriods(loanId, monthDate).FirstOrDefault();
                var openingBalance = GetLatestBalanceBeforeMonth(loanId, monthDate);
                if (openingBalance == 0m)
                {
                    openingBalance = GetBalanceAtMonth(loanId, monthDate);
                }

                if (openingBalance == 0m)
                {
                    openingBalance = GetLoanStartingBalance(loanId);
                }

                var balance = openingBalance;
                var amortizationAmount = period is null ? 0m : Math.Min(balance, period.MonthlyAmortization);

                if (amortizationAmount > 0m)
                {
                    Data.ExpenseRecords.Add(CreateLoanExpenseEntry(
                        loanId,
                        lender,
                        category,
                        month,
                        AmortizationSubcategory,
                        amortizationAmount,
                        period?.Id,
                        null,
                        metadata,
                        vehicleId));
                    generatedCount++;
                }

                balance = Math.Max(0m, balance - amortizationAmount);

                if (period is not null && balance > 0m && period.InterestRate > 0m)
                {
                    var monthlyInterestCost = Math.Round(balance * ConvertPercentageToDecimal(period.InterestRate) / 12m, 2, MidpointRounding.AwayFromZero);
                    if (monthlyInterestCost > 0m)
                    {
                        Data.ExpenseRecords.Add(CreateLoanExpenseEntry(
                            loanId,
                            lender,
                            category,
                            month,
                            InterestSubcategory,
                            monthlyInterestCost,
                            period.Id,
                            null,
                            metadata,
                            vehicleId));
                        generatedCount++;
                    }
                }

                SetMonthlyBalance(loanId, monthDate, balance);
            }

            RefreshAnyLoanDerivedValues(loanId);
        }

        return generatedCount;
    }

    private IReadOnlyList<LoanInterestBindingPeriod> GetActiveLoanInterestBindingPeriods(Guid loanId, DateOnly month) =>
        Data.LoanInterestBindingPeriods
            .Where(period => period.LoanId == loanId && CoversMonth(period.StartMonth, period.EndMonth, month))
            .OrderBy(period => period.StartMonth)
            .ToList();

    private void RemoveGeneratedExpensesInRange(ExpenseCategory category, DateOnly startDate, DateOnly endDate, bool includeLeasing)
    {
        Data.ExpenseRecords.RemoveAll(expense =>
            expense.LoanId.HasValue &&
            !expense.LoanBalanceNeutral &&
            expense.Category == category &&
            expense.Year >= startDate.Year &&
            expense.Year <= endDate.Year &&
            NormalizeMonth(new DateOnly(expense.Year, expense.Month, 1)) >= startDate &&
            NormalizeMonth(new DateOnly(expense.Year, expense.Month, 1)) <= endDate &&
            expense.Subcategory is InterestSubcategory or AmortizationSubcategory);

        if (!includeLeasing)
        {
            return;
        }

        Data.ExpenseRecords.RemoveAll(expense =>
            expense.TransportLeasingContractId.HasValue &&
            expense.Category == category &&
            NormalizeMonth(new DateOnly(expense.Year, expense.Month, 1)) >= startDate &&
            NormalizeMonth(new DateOnly(expense.Year, expense.Month, 1)) <= endDate &&
            expense.Subcategory == LeasingSubcategory);
    }

    private void RemoveAllGeneratedExpenses(ExpenseCategory category, bool includeLeasing)
    {
        Data.ExpenseRecords.RemoveAll(expense =>
            expense.LoanId.HasValue &&
            !expense.LoanBalanceNeutral &&
            expense.Category == category &&
            expense.Subcategory is InterestSubcategory or AmortizationSubcategory);

        if (!includeLeasing)
        {
            return;
        }

        Data.ExpenseRecords.RemoveAll(expense =>
            expense.TransportLeasingContractId.HasValue &&
            expense.Category == category &&
            expense.Subcategory == LeasingSubcategory);
    }

    private void RefreshLoanDerivedValues(Guid loanId)
    {
        var loan = Data.HousingLoans.FirstOrDefault(item => item.Id == loanId);
        if (loan is null)
        {
            return;
        }

        var todayMonth = NormalizeMonth(DateOnly.FromDateTime(DateTime.Today));
        loan.CurrentAmortization = GetActiveLoanInterestBindingPeriods(loan.Id, todayMonth)
            .Sum(period => period.MonthlyAmortization);
    }

    private void RefreshTransportLoanDerivedValues(Guid loanId)
    {
        var loan = Data.TransportLoans.FirstOrDefault(item => item.Id == loanId);
        if (loan is null)
        {
            return;
        }

        var todayMonth = NormalizeMonth(DateOnly.FromDateTime(DateTime.Today));
        loan.CurrentAmortization = GetActiveLoanInterestBindingPeriods(loan.Id, todayMonth)
            .Sum(period => period.MonthlyAmortization);
    }

    private Credit GetCredit(Guid creditId) =>
        Data.Credits.FirstOrDefault(item => item.Id == creditId)
        ?? throw new InvalidOperationException("The selected credit does not exist.");

    private static void ValidateCreditValues(decimal creditLimit, decimal remainingDebt, decimal monthlyInterestRate)
    {
        if (creditLimit < 0m || remainingDebt < 0m || monthlyInterestRate < 0m)
        {
            throw new InvalidOperationException("Credit values must not be negative.");
        }

        if (remainingDebt > creditLimit)
        {
            throw new InvalidOperationException("Remaining debt cannot exceed the credit limit.");
        }
    }

    private TransportVehicle GetTransportVehicle(Guid vehicleId) =>
        Data.TransportVehicles.FirstOrDefault(item => item.Id == vehicleId)
        ?? throw new InvalidOperationException("The selected vehicle does not exist.");

    private AssetItem GetAsset(Guid assetId) =>
        Data.Assets.FirstOrDefault(item => item.Id == assetId)
        ?? throw new InvalidOperationException("The selected asset does not exist.");

    private string GetTransportVehicleName(Guid? vehicleId)
    {
        if (!vehicleId.HasValue)
        {
            return string.Empty;
        }

        return Data.TransportVehicles.FirstOrDefault(item => item.Id == vehicleId.Value)?.Name?.Trim() ?? string.Empty;
    }

    private string NormalizeExpenseMetadata(ExpenseCategory category, Guid? transportVehicleId, string? metadata)
    {
        if (category == ExpenseCategory.Transport && transportVehicleId.HasValue)
        {
            return GetTransportVehicleName(transportVehicleId);
        }

        return metadata?.Trim() ?? string.Empty;
    }

    private IncomeEntry CreateOneTimeIncomeEntry(Guid memberId, decimal amount, int year, int month, string type, string? metadata, Guid? savingsAccountId, Guid? assetId) => new()
    {
        MemberId = memberId,
        Amount = amount,
        Year = year,
        Month = month,
        Type = type.Trim(),
        Metadata = metadata?.Trim() ?? string.Empty,
        SavingsAccountId = savingsAccountId,
        AssetId = assetId
    };

    private ExpenseEntry CreateOneTimeExpenseEntry(
        decimal amount,
        int year,
        int month,
        ExpenseCategory category,
        string subcategory,
        string description,
        string? metadata,
        Guid? transportVehicleId,
        Guid? savingsAccountId,
        Guid? assetId) => new()
        {
            Amount = amount,
            Year = year,
            Month = month,
            Category = category,
            Subcategory = subcategory.Trim(),
            Metadata = NormalizeExpenseMetadata(category, transportVehicleId, metadata),
            Description = description.Trim(),
            TransportVehicleId = category == ExpenseCategory.Transport ? transportVehicleId : null,
            SavingsAccountId = category == ExpenseCategory.Savings ? savingsAccountId : null,
            AssetId = category == ExpenseCategory.Assets ? assetId : null
        };

    private ExpenseEntry CreateSavingsDepositExpenseEntry(SavingsAccount account, DateOnly date, decimal amount) =>
        CreateOneTimeExpenseEntry(
            amount,
            date.Year,
            date.Month,
            ExpenseCategory.Savings,
            GetSavingsSubcategory(account.AccountType),
            account.AccountName,
            account.AccountName,
            null,
            account.Id,
            null);

    private static string BuildTransportVehicleAssetDescription(TransportVehicle vehicle) =>
        $"{vehicle.ModelYear} {vehicle.VehicleType} / {vehicle.FuelType}";

    private static string BuildHomeResidenceAssetName(HomeResidence residence) =>
        residence.ResidenceType switch
        {
            HomeResidenceType.Condominium => "Home Condominium",
            HomeResidenceType.House => "Home House",
            _ => "Home Residence"
        };

    private static string BuildHomeResidenceAssetType(HomeResidence residence) =>
        residence.ResidenceType switch
        {
            HomeResidenceType.Condominium => "Condominium",
            HomeResidenceType.House => "House",
            _ => "Housing"
        };

    private static string BuildHomeResidenceAssetDescription(HomeResidence residence) =>
        residence.ResidenceType switch
        {
            HomeResidenceType.Condominium => "Primary home residence condominium",
            HomeResidenceType.House => "Primary home residence house",
            _ => "Primary home residence"
        };

    private AssetItem CreateTransportVehicleAsset(TransportVehicle vehicle) => new()
    {
        Name = vehicle.Name,
        Type = "Vehicle",
        Description = BuildTransportVehicleAssetDescription(vehicle),
        PurchaseValue = vehicle.PurchasePrice ?? vehicle.EstimatedSaleValue ?? 0m,
        Amount = vehicle.PurchasePrice ?? vehicle.EstimatedSaleValue ?? 0m,
        EstimatedSaleValue = vehicle.EstimatedSaleValue ?? 0m,
        AcquisitionDate = new DateOnly(Math.Max(1, vehicle.ModelYear), 1, 1),
        SourceType = AssetSourceType.TransportVehicle,
        TransportVehicleId = vehicle.Id
    };

    private void SyncLinkedAssetSource(AssetItem asset)
    {
        if (asset.SourceType == AssetSourceType.TransportVehicle && asset.TransportVehicleId.HasValue)
        {
            var vehicle = Data.TransportVehicles.FirstOrDefault(item => item.Id == asset.TransportVehicleId.Value);
            if (vehicle is not null)
            {
                asset.Name = vehicle.Name;
                asset.Type = "Vehicle";
                asset.Description = BuildTransportVehicleAssetDescription(vehicle);
            }
        }
        else if (asset.SourceType == AssetSourceType.HomeResidence && asset.HomeResidenceId.HasValue)
        {
            var residence = Data.HomeResidence;
            if (residence is not null && residence.Id == asset.HomeResidenceId.Value)
            {
                asset.Name = BuildHomeResidenceAssetName(residence);
                asset.Type = BuildHomeResidenceAssetType(residence);
                asset.Description = BuildHomeResidenceAssetDescription(residence);
            }
        }
    }

    private void SyncHomeResidenceAsset() => SyncHomeResidenceAssetInMemory();

    private void SyncHomeResidenceAssetInMemory()
    {
        var residence = Data.HomeResidence;
        if (residence is null)
        {
            return;
        }

        var isOwnedResidence = residence.ResidenceType is HomeResidenceType.Condominium or HomeResidenceType.House;
        if (!isOwnedResidence)
        {
            if (residence.AssetId.HasValue)
            {
                Data.Assets.RemoveAll(asset => asset.Id == residence.AssetId.Value || asset.HomeResidenceId == residence.Id);
                residence.AssetId = null;
            }

            return;
        }

        var acquisitionDate = new DateOnly(Math.Max(1, residence.PurchaseYear ?? DateTime.Today.Year), 1, 1);
        var purchaseValue = residence.PurchasePrice ?? 0m;
        var estimatedSaleValue = residence.CurrentMarketValue ?? 0m;

        AssetItem? asset = null;
        if (residence.AssetId.HasValue)
        {
            asset = Data.Assets.FirstOrDefault(item => item.Id == residence.AssetId.Value);
        }

        asset ??= Data.Assets.FirstOrDefault(item => item.HomeResidenceId == residence.Id);

        if (asset is null)
        {
            asset = new AssetItem
            {
                HomeResidenceId = residence.Id,
                SourceType = AssetSourceType.HomeResidence
            };

            Data.Assets.Add(asset);
        }

        residence.AssetId = asset.Id;
        asset.Name = BuildHomeResidenceAssetName(residence);
        asset.Type = BuildHomeResidenceAssetType(residence);
        asset.Description = BuildHomeResidenceAssetDescription(residence);
        asset.PurchaseValue = purchaseValue;
        asset.Amount = purchaseValue;
        asset.EstimatedSaleValue = estimatedSaleValue;
        asset.AcquisitionDate = acquisitionDate;
        asset.SourceType = AssetSourceType.HomeResidence;
        asset.HomeResidenceId = residence.Id;
    }

    private static decimal ConvertPercentageToDecimal(decimal percentageRate) => percentageRate / 100m;

    private string NormalizeSystemIncomeType(string type)
    {
        var trimmedType = type?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedType))
        {
            return OtherIncomeType;
        }

        return trimmedType switch
        {
            var value when MatchesLocalizedOrStableValue(value, SalaryIncomeType, "incomeType.Salary") => SalaryIncomeType,
            var value when MatchesLocalizedOrStableValue(value, TaxRefundIncomeType, "incomeType.TaxRefund") => TaxRefundIncomeType,
            var value when MatchesLocalizedOrStableValue(value, InheritanceIncomeType, "incomeType.Inheritance") => InheritanceIncomeType,
            var value when MatchesLocalizedOrStableValue(value, GiftIncomeType, "incomeType.Gift") => GiftIncomeType,
            var value when MatchesLocalizedOrStableValue(value, OtherIncomeType, "incomeType.Other") => OtherIncomeType,
            var value when MatchesLocalizedOrStableValue(value, AssetSaleSubcategory, "assets.assetSale") => AssetSaleSubcategory,
            var value when MatchesLocalizedOrStableValue(value, "Bank", "savings.bankTab") => "Bank",
            var value when MatchesLocalizedOrStableValue(value, "Fund", "savings.fundsTab") => "Fund",
            var value when MatchesLocalizedOrStableValue(value, "Stock", "savings.stocksTab") => "Stock",
            _ => trimmedType
        };
    }

    private string GetLocalizedExpenseLabel(string subcategory)
    {
        var trimmedSubcategory = subcategory?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedSubcategory))
        {
            return string.Empty;
        }

        if (TryGetConfiguredSubcategoryDisplay(null, trimmedSubcategory, out var configuredDisplay))
        {
            return configuredDisplay;
        }

        if (string.Equals(trimmedSubcategory, InterestSubcategory, StringComparison.Ordinal))
        {
            return _localizer["table.interest"];
        }

        if (string.Equals(trimmedSubcategory, AmortizationSubcategory, StringComparison.Ordinal))
        {
            return _localizer["table.amortization"];
        }

        if (string.Equals(trimmedSubcategory, LeasingSubcategory, StringComparison.Ordinal))
        {
            return _localizer["transport.leasingTab"];
        }

        if (string.Equals(trimmedSubcategory, "Bank", StringComparison.Ordinal))
        {
            return _localizer["savings.bankTab"];
        }

        if (string.Equals(trimmedSubcategory, "Fund", StringComparison.Ordinal))
        {
            return _localizer["savings.fundsTab"];
        }

        if (string.Equals(trimmedSubcategory, "Stock", StringComparison.Ordinal))
        {
            return _localizer["savings.stocksTab"];
        }

        if (string.Equals(trimmedSubcategory, UnspecifiedSubcategory, StringComparison.Ordinal))
        {
            return _localizer["start.unspecified"];
        }

        if (trimmedSubcategory.EndsWith($" - {InterestSubcategory}", StringComparison.Ordinal))
        {
            return $"{trimmedSubcategory[..^($" - {InterestSubcategory}".Length)]} - {_localizer["table.interest"]}";
        }

        if (trimmedSubcategory.EndsWith($" - {AmortizationSubcategory}", StringComparison.Ordinal))
        {
            return $"{trimmedSubcategory[..^($" - {AmortizationSubcategory}".Length)]} - {_localizer["table.amortization"]}";
        }

        if (trimmedSubcategory.EndsWith($" - {LeasingSubcategory}", StringComparison.Ordinal))
        {
            return $"{trimmedSubcategory[..^($" - {LeasingSubcategory}".Length)]} - {_localizer["transport.leasingTab"]}";
        }

        return trimmedSubcategory;
    }

    private string GetHomeResidenceAssetNameDisplay(HomeResidence residence) =>
        residence.ResidenceType switch
        {
            HomeResidenceType.Condominium => $"{_localizer["housing.homeResidence"]} {_localizer["housing.residenceType.condominium"]}",
            HomeResidenceType.House => $"{_localizer["housing.homeResidence"]} {_localizer["housing.residenceType.house"]}",
            _ => _localizer["housing.homeResidence"]
        };

    private string GetHomeResidenceAssetTypeDisplay(HomeResidence residence) =>
        residence.ResidenceType switch
        {
            HomeResidenceType.Condominium => _localizer["housing.residenceType.condominium"],
            HomeResidenceType.House => _localizer["housing.residenceType.house"],
            _ => _localizer["nav.housingTab"]
        };

    private bool MatchesLocalizedOrStableValue(string value, string stableValue, string localizationKey) =>
        string.Equals(value, stableValue, StringComparison.Ordinal) ||
        string.Equals(value, _localizer[localizationKey], StringComparison.Ordinal);

    private static IReadOnlyList<SubcategoryDefinition> GetDefaultSubcategoryDefinitions() =>
    [
        new() { Key = "subcategory.income.salary", MainCategory = SubcategoryMainCategory.Income, SwedishName = "Lön", EnglishName = "Salary" },
        new() { Key = "subcategory.income.inheritance", MainCategory = SubcategoryMainCategory.Income, SwedishName = "Arv", EnglishName = "Inheritance" },
        new() { Key = "subcategory.income.interest", MainCategory = SubcategoryMainCategory.Income, SwedishName = "Ränta", EnglishName = "Interest" },
        new() { Key = "subcategory.income.gambling_winnings", MainCategory = SubcategoryMainCategory.Income, SwedishName = "Spelvinst", EnglishName = "Gambling winnings" },
        new() { Key = "subcategory.income.dividend", MainCategory = SubcategoryMainCategory.Income, SwedishName = "Utdelning", EnglishName = "Dividend" },
        new() { Key = "subcategory.income.withdrawal", MainCategory = SubcategoryMainCategory.Income, SwedishName = "Uttag", EnglishName = "Withdrawal" },
        new() { Key = "subcategory.food.grocery", MainCategory = SubcategoryMainCategory.Food, SwedishName = "Matbutik", EnglishName = "Grocery store" },
        new() { Key = "subcategory.food.delivery", MainCategory = SubcategoryMainCategory.Food, SwedishName = "Hemkörning", EnglishName = "Delivery" },
        new() { Key = "subcategory.food.restaurant", MainCategory = SubcategoryMainCategory.Food, SwedishName = "Restaurang", EnglishName = "Restaurant" },
        new() { Key = "subcategory.food.lunch", MainCategory = SubcategoryMainCategory.Food, SwedishName = "Lunch", EnglishName = "Lunch" },
        new() { Key = "subcategory.food.breakfast", MainCategory = SubcategoryMainCategory.Food, SwedishName = "Frukost", EnglishName = "Breakfast" },
        new() { Key = "subcategory.food.coffee_break", MainCategory = SubcategoryMainCategory.Food, SwedishName = "Fika", EnglishName = "Coffee break" },
        new() { Key = "subcategory.housing.rent", MainCategory = SubcategoryMainCategory.Housing, SwedishName = "Hyra", EnglishName = "Rent" },
        new() { Key = "subcategory.housing.monthly_fee", MainCategory = SubcategoryMainCategory.Housing, SwedishName = "Månadsavgift", EnglishName = "Monthly fee" },
        new() { Key = "subcategory.housing.electricity", MainCategory = SubcategoryMainCategory.Housing, SwedishName = "El", EnglishName = "Electricity" },
        new() { Key = "subcategory.housing.heating", MainCategory = SubcategoryMainCategory.Housing, SwedishName = "Uppvärmning", EnglishName = "Heating" },
        new() { Key = "subcategory.housing.garbage_collection", MainCategory = SubcategoryMainCategory.Housing, SwedishName = "Sophämtning", EnglishName = "Garbage collection" },
        new() { Key = "subcategory.housing.water", MainCategory = SubcategoryMainCategory.Housing, SwedishName = "Vatten", EnglishName = "Water" },
        new() { Key = "subcategory.housing.amortization", MainCategory = SubcategoryMainCategory.Housing, SwedishName = "Amortering", EnglishName = "Amortization" },
        new() { Key = "subcategory.housing.interest", MainCategory = SubcategoryMainCategory.Housing, SwedishName = "Ränta", EnglishName = "Interest" },
        new() { Key = "subcategory.housing.home_insurance", MainCategory = SubcategoryMainCategory.Housing, SwedishName = "Hemförsäkring", EnglishName = "Home insurance" },
        new() { Key = "subcategory.housing.broadband", MainCategory = SubcategoryMainCategory.Housing, SwedishName = "Bredband", EnglishName = "Broadband" },
        new() { Key = "subcategory.housing.maintenance", MainCategory = SubcategoryMainCategory.Housing, SwedishName = "Underhåll", EnglishName = "Maintenance" },
        new() { Key = "subcategory.housing.operating_costs", MainCategory = SubcategoryMainCategory.Housing, SwedishName = "Driftkostnader", EnglishName = "Operating costs" },
        new() { Key = "subcategory.housing.alarm", MainCategory = SubcategoryMainCategory.Housing, SwedishName = "Larm", EnglishName = "Alarm" },
        new() { Key = "subcategory.transport.petrol", MainCategory = SubcategoryMainCategory.Transport, SwedishName = "Bensin", EnglishName = "Petrol" },
        new() { Key = "subcategory.transport.diesel", MainCategory = SubcategoryMainCategory.Transport, SwedishName = "Diesel", EnglishName = "Diesel" },
        new() { Key = "subcategory.transport.electricity", MainCategory = SubcategoryMainCategory.Transport, SwedishName = "El", EnglishName = "Electricity" },
        new() { Key = "subcategory.transport.car_insurance", MainCategory = SubcategoryMainCategory.Transport, SwedishName = "Bilförsäkring", EnglishName = "Car insurance" },
        new() { Key = "subcategory.transport.vehicle_tax", MainCategory = SubcategoryMainCategory.Transport, SwedishName = "Fordonskatt", EnglishName = "Vehicle tax" },
        new() { Key = "subcategory.transport.service", MainCategory = SubcategoryMainCategory.Transport, SwedishName = "Service", EnglishName = "Service" },
        new() { Key = "subcategory.transport.repair", MainCategory = SubcategoryMainCategory.Transport, SwedishName = "Reparation", EnglishName = "Repair" },
        new() { Key = "subcategory.transport.tires", MainCategory = SubcategoryMainCategory.Transport, SwedishName = "Däck", EnglishName = "Tires" },
        new() { Key = "subcategory.transport.parking", MainCategory = SubcategoryMainCategory.Transport, SwedishName = "Parkering", EnglishName = "Parking" },
        new() { Key = "subcategory.transport.public_transport", MainCategory = SubcategoryMainCategory.Transport, SwedishName = "Kollektivtrafik", EnglishName = "Public transport" },
        new() { Key = "subcategory.transport.train", MainCategory = SubcategoryMainCategory.Transport, SwedishName = "Tåg", EnglishName = "Train" },
        new() { Key = "subcategory.transport.flight", MainCategory = SubcategoryMainCategory.Transport, SwedishName = "Flyg", EnglishName = "Flight" },
        new() { Key = "subcategory.transport.amortization", MainCategory = SubcategoryMainCategory.Transport, SwedishName = "Amortering", EnglishName = "Amortization" },
        new() { Key = "subcategory.transport.interest", MainCategory = SubcategoryMainCategory.Transport, SwedishName = "Ränta", EnglishName = "Interest" },
        new() { Key = "subcategory.clothing.everyday_clothes", MainCategory = SubcategoryMainCategory.Clothing, SwedishName = "Vardagskläder", EnglishName = "Everyday clothes" },
        new() { Key = "subcategory.clothing.shoes", MainCategory = SubcategoryMainCategory.Clothing, SwedishName = "Skor", EnglishName = "Shoes" },
        new() { Key = "subcategory.clothing.jacket", MainCategory = SubcategoryMainCategory.Clothing, SwedishName = "Jacka", EnglishName = "Jacket" },
        new() { Key = "subcategory.clothing.shirt_sweater", MainCategory = SubcategoryMainCategory.Clothing, SwedishName = "Tröja/Skjorta", EnglishName = "Sweater/Shirt" },
        new() { Key = "subcategory.clothing.trousers", MainCategory = SubcategoryMainCategory.Clothing, SwedishName = "Byxa", EnglishName = "Trousers" },
        new() { Key = "subcategory.clothing.underwear", MainCategory = SubcategoryMainCategory.Clothing, SwedishName = "Underkläder", EnglishName = "Underwear" },
        new() { Key = "subcategory.clothing.childrens_clothes", MainCategory = SubcategoryMainCategory.Clothing, SwedishName = "Barnkläder", EnglishName = "Children's clothes" },
        new() { Key = "subcategory.other.charity", MainCategory = SubcategoryMainCategory.Other, SwedishName = "Välgörenhet", EnglishName = "Charity" },
        new() { Key = "subcategory.other.cloud_services", MainCategory = SubcategoryMainCategory.Other, SwedishName = "Molntjänster", EnglishName = "Cloud services" },
        new() { Key = "subcategory.other.home", MainCategory = SubcategoryMainCategory.Other, SwedishName = "Hemmet", EnglishName = "Home" },
        new() { Key = "subcategory.other.insurance", MainCategory = SubcategoryMainCategory.Other, SwedishName = "Försäkring", EnglishName = "Insurance" },
        new() { Key = "subcategory.other.hobby", MainCategory = SubcategoryMainCategory.Other, SwedishName = "Hobby", EnglishName = "Hobby" },
        new() { Key = "subcategory.other.tv_mobile_telephony", MainCategory = SubcategoryMainCategory.Other, SwedishName = "TV och mobiltelefoni", EnglishName = "TV and mobile telephony" },
        new() { Key = "subcategory.other.pets", MainCategory = SubcategoryMainCategory.Other, SwedishName = "Husdjur", EnglishName = "Pets" },
        new() { Key = "subcategory.other.self_care", MainCategory = SubcategoryMainCategory.Other, SwedishName = "Egenvård", EnglishName = "Self care" },
        new() { Key = "subcategory.other.betting", MainCategory = SubcategoryMainCategory.Other, SwedishName = "Tips, hästar och vadslagning", EnglishName = "Betting, horse racing and wagering" },
        new() { Key = "subcategory.other.soda_snacks_candy", MainCategory = SubcategoryMainCategory.Other, SwedishName = "Läsk, snacks och godis", EnglishName = "Soft drinks, snacks and candy" },
        new() { Key = "subcategory.other.healthcare", MainCategory = SubcategoryMainCategory.Other, SwedishName = "Hälsa och vård", EnglishName = "Health and care" },
        new() { Key = "subcategory.other.computers_accessories", MainCategory = SubcategoryMainCategory.Other, SwedishName = "Dator och tillbehör", EnglishName = "Computer and accessories" },
        new() { Key = "subcategory.other.audio_video", MainCategory = SubcategoryMainCategory.Other, SwedishName = "Ljud och Bild", EnglishName = "Audio and video" },
        new() { Key = "subcategory.other.education", MainCategory = SubcategoryMainCategory.Other, SwedishName = "Utbildning", EnglishName = "Education" },
        new() { Key = "subcategory.other.gifts", MainCategory = SubcategoryMainCategory.Other, SwedishName = "Present", EnglishName = "Gift" },
        new() { Key = "subcategory.other.travel", MainCategory = SubcategoryMainCategory.Other, SwedishName = "Resor", EnglishName = "Travel" },
        new() { Key = "subcategory.other.entertainment", MainCategory = SubcategoryMainCategory.Other, SwedishName = "Nöjen", EnglishName = "Entertainment" },
        new() { Key = "subcategory.other.unspecified", MainCategory = SubcategoryMainCategory.Other, SwedishName = "Ospecificerat", EnglishName = "Unspecified" }
    ];

    private void NormalizeSubcategoryDefinitions()
    {
        var shouldSeedDefaults = !Data.HasInitializedSubcategoryDefinitions && Data.SubcategoryDefinitions.Count == 0;
        var defaultDefinitions = GetDefaultSubcategoryDefinitions();

        var definitions = Data.SubcategoryDefinitions
            .Where(definition => definition.MainCategory != 0)
            .Select(definition => new SubcategoryDefinition
            {
                Id = definition.Id == Guid.Empty ? Guid.NewGuid() : definition.Id,
                Key = string.IsNullOrWhiteSpace(definition.Key) ? $"subcategory.custom.{Guid.NewGuid():N}" : definition.Key.Trim(),
                MainCategory = definition.MainCategory,
                SwedishName = definition.SwedishName?.Trim() ?? string.Empty,
                EnglishName = definition.EnglishName?.Trim() ?? string.Empty
            })
            .Where(definition => !string.IsNullOrWhiteSpace(definition.SwedishName) && !string.IsNullOrWhiteSpace(definition.EnglishName))
            .GroupBy(definition => definition.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        if (shouldSeedDefaults)
        {
            foreach (var defaultDefinition in defaultDefinitions)
            {
                definitions.Add(CloneSubcategoryDefinition(defaultDefinition));
            }
        }
        else
        {
            definitions.RemoveAll(definition => string.Equals(definition.Key, "subcategory.other.streaming", StringComparison.OrdinalIgnoreCase));

            foreach (var defaultDefinition in defaultDefinitions.Where(definition =>
                         !definitions.Any(existing => string.Equals(existing.Key, definition.Key, StringComparison.OrdinalIgnoreCase))))
            {
                definitions.Add(CloneSubcategoryDefinition(defaultDefinition));
            }
        }

        Data.SubcategoryDefinitions = definitions
            .OrderBy(definition => definition.MainCategory)
            .ThenBy(definition => definition.SwedishName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        Data.HasInitializedSubcategoryDefinitions = true;
    }

    private IReadOnlyList<string> GetConfiguredSubcategorySuggestions(ExpenseCategory category)
    {
        var mainCategory = MapExpenseCategoryToMainCategory(category);
        if (!mainCategory.HasValue)
        {
            return [];
        }

        return Data.SubcategoryDefinitions
            .Where(definition => definition.MainCategory == mainCategory.Value)
            .Select(GetSubcategoryDisplay)
            .ToList();
    }

    private bool TryGetConfiguredSubcategoryDisplay(SubcategoryMainCategory? mainCategory, string value, out string display)
    {
        var trimmedValue = value?.Trim() ?? string.Empty;
        var definition = Data.SubcategoryDefinitions.FirstOrDefault(item =>
            (!mainCategory.HasValue || item.MainCategory == mainCategory.Value) &&
            (string.Equals(item.SwedishName, trimmedValue, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(item.EnglishName, trimmedValue, StringComparison.OrdinalIgnoreCase)));

        if (definition is null)
        {
            display = trimmedValue;
            return false;
        }

        display = GetSubcategoryDisplay(definition);
        return true;
    }

    private string GetLocalizedConfiguredSubcategoryLabel(SubcategoryMainCategory? mainCategory, string value) =>
        TryGetConfiguredSubcategoryDisplay(mainCategory, value, out var display)
            ? display
            : value.Trim();

    private string GetSubcategoryDisplay(SubcategoryDefinition definition) =>
        string.Equals(_localizer.CurrentLanguage, "sv", StringComparison.OrdinalIgnoreCase)
            ? definition.SwedishName
            : definition.EnglishName;

    private static SubcategoryDefinition CloneSubcategoryDefinition(SubcategoryDefinition definition) => new()
    {
        Id = definition.Id,
        Key = definition.Key,
        MainCategory = definition.MainCategory,
        SwedishName = definition.SwedishName,
        EnglishName = definition.EnglishName
    };

    private void NormalizeImportedExpenseCategorySuggestions()
    {
        Data.ImportedExpenseCategorySuggestions = Data.ImportedExpenseCategorySuggestions
            .Where(suggestion =>
                !string.IsNullOrWhiteSpace(suggestion.Description) &&
                !string.IsNullOrWhiteSpace(suggestion.Subcategory))
            .Select(suggestion => new ImportedExpenseCategorySuggestion
            {
                Description = suggestion.Description.Trim(),
                Category = suggestion.Category,
                Subcategory = suggestion.Subcategory.Trim()
            })
            .DistinctBy(suggestion => new
            {
                Description = suggestion.Description,
                suggestion.Category,
                Subcategory = suggestion.Subcategory
            })
            .OrderBy(suggestion => suggestion.Description, StringComparer.OrdinalIgnoreCase)
            .ThenBy(suggestion => suggestion.Category)
            .ThenBy(suggestion => suggestion.Subcategory, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private sealed class ImportedExpenseCategorySuggestionComparer : IEqualityComparer<ImportedExpenseCategorySuggestion>
    {
        public static ImportedExpenseCategorySuggestionComparer Instance { get; } = new();

        public bool Equals(ImportedExpenseCategorySuggestion? x, ImportedExpenseCategorySuggestion? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            return string.Equals(x.Description, y.Description, StringComparison.Ordinal) &&
                   x.Category == y.Category &&
                   string.Equals(x.Subcategory, y.Subcategory, StringComparison.Ordinal);
        }

        public int GetHashCode(ImportedExpenseCategorySuggestion obj) =>
            HashCode.Combine(obj.Description, obj.Category, obj.Subcategory);
    }

    private static SubcategoryMainCategory? MapExpenseCategoryToMainCategory(ExpenseCategory category) => category switch
    {
        ExpenseCategory.Food => SubcategoryMainCategory.Food,
        ExpenseCategory.Housing => SubcategoryMainCategory.Housing,
        ExpenseCategory.Transport => SubcategoryMainCategory.Transport,
        ExpenseCategory.Clothing => SubcategoryMainCategory.Clothing,
        ExpenseCategory.Other => SubcategoryMainCategory.Other,
        _ => null
    };

    private void RemoveGeneratedCreditExpensesInRange(DateOnly startDate, DateOnly endDate)
    {
        Data.ExpenseRecords.RemoveAll(expense =>
            expense.Category == ExpenseCategory.Credits &&
            expense.CreditId.HasValue &&
            NormalizeMonth(new DateOnly(expense.Year, expense.Month, 1)) >= startDate &&
            NormalizeMonth(new DateOnly(expense.Year, expense.Month, 1)) <= endDate &&
            expense.CreditCostSource is CreditCostSource.GeneratedInterest or CreditCostSource.GeneratedResetAmortization);
    }

    private void RemoveAllGeneratedCreditExpenses()
    {
        Data.ExpenseRecords.RemoveAll(expense =>
            expense.Category == ExpenseCategory.Credits &&
            expense.CreditId.HasValue &&
            expense.CreditCostSource is CreditCostSource.GeneratedInterest or CreditCostSource.GeneratedResetAmortization);
    }

    private void RefreshCreditDerivedValues(Guid creditId)
    {
        _ = Data.Credits.FirstOrDefault(item => item.Id == creditId);
    }

    private bool IsCreditMonthPosted(Guid creditId, DateOnly month)
    {
        var normalizedMonth = NormalizeMonth(month);
        return Data.ExpenseRecords.Any(expense =>
            expense.CreditId == creditId &&
            NormalizeMonth(new DateOnly(expense.Year, expense.Month, 1)) == normalizedMonth &&
            expense.CreditCostSource is CreditCostSource.ManualAmortization or CreditCostSource.GeneratedInterest or CreditCostSource.GeneratedResetAmortization);
    }

    private async Task RecalculateCreditBalancesAsync(Guid creditId, DateOnly startDate)
    {
        var credit = GetCredit(creditId);
        var normalizedStart = NormalizeMonth(startDate);
        var creditStartMonth = NormalizeMonth(credit.StartDate);
        if (normalizedStart < creditStartMonth)
        {
            normalizedStart = creditStartMonth;
        }

        RemoveMonthlyBalancesFrom(credit.Id, normalizedStart);

        var balance = normalizedStart == creditStartMonth
            ? credit.StartingRemainingDebt
            : GetBalanceAtOrBeforeMonth(credit.Id, normalizedStart.AddMonths(-1));

        foreach (var month in EnumerateMonths(normalizedStart.Year, normalizedStart.Month, GetGenerationLimit().Year, GetGenerationLimit().Month))
        {
            var monthDate = month.ToDateOnly();
            balance += Data.ExpenseRecords
                .Where(expense =>
                    expense.CreditId == credit.Id &&
                    NormalizeMonth(new DateOnly(expense.Year, expense.Month, 1)) == monthDate &&
                    expense.CreditCostSource is CreditCostSource.ManualPurchase or CreditCostSource.GeneratedInterest)
                .Sum(expense => expense.Amount);

            balance -= Data.ExpenseRecords
                .Where(expense =>
                    expense.CreditId == credit.Id &&
                    NormalizeMonth(new DateOnly(expense.Year, expense.Month, 1)) == monthDate &&
                    expense.CreditCostSource is CreditCostSource.ManualAmortization or CreditCostSource.GeneratedResetAmortization)
                .Sum(expense => expense.Amount);

            balance = Math.Max(0m, balance);
            SetMonthlyBalance(credit.Id, monthDate, balance);
        }

        await PersistAndNotifyAsync();
    }

    private IReadOnlyList<TransportLeasingContract> GetActiveTransportLeasingContracts(DateOnly month) =>
        Data.TransportLeasingContracts
            .Where(contract => CoversMonth(contract.StartDate, contract.EndDate, month))
            .OrderBy(contract => contract.StartDate)
            .ToList();

    private static IReadOnlyList<decimal> DistributeAcrossMonths(decimal yearlyAmount)
    {
        var baseAmount = Math.Round(yearlyAmount / 12m, 2, MidpointRounding.AwayFromZero);
        var distributed = Enumerable.Repeat(baseAmount, 12).ToArray();
        distributed[^1] += yearlyAmount - distributed.Sum();
        return distributed;
    }

    private HouseholdMember GetMember(Guid memberId) =>
        Data.Members.FirstOrDefault(member => member.Id == memberId)
        ?? throw new InvalidOperationException("Member was not found.");

    private string GetMemberNameOrUnknown(Guid memberId) =>
        Data.Members.FirstOrDefault(member => member.Id == memberId)?.Name ?? _localizer["common.unknown"];

    private decimal GetMonthlyIncome(int year, int month) =>
        Data.IncomeRecords
            .Where(entry => entry.Year == year && entry.Month == month)
            .Sum(entry => entry.Amount);

    private decimal GetMonthlyExpenses(int year, int month) =>
        Data.ExpenseRecords
            .Where(entry => entry.Year == year && entry.Month == month)
            .Sum(entry => entry.Amount);

    private decimal GetHousingLoanDebtForMonth(DateOnly reportMonth) =>
        Data.HousingLoans.Sum(loan => GetBalanceAtOrBeforeMonth(loan.Id, NormalizeMonth(reportMonth)));

    private decimal GetCreditsDebtForMonth(DateOnly reportMonth)
    {
        var transportLoanDebt = Data.TransportLoans
            .Sum(loan => GetBalanceAtOrBeforeMonth(loan.Id, NormalizeMonth(reportMonth)));

        var creditDebt = Data.Credits
            .Sum(credit => GetBalanceAtOrBeforeMonth(credit.Id, NormalizeMonth(reportMonth)));

        return transportLoanDebt + creditDebt;
    }

    private decimal GetSavingsForMonth(DateOnly reportMonth)
    {
        return Data.SavingsAccounts.Sum(account => GetBalanceAtOrBeforeMonth(account.Id, NormalizeMonth(reportMonth)));
    }

    private decimal GetPropertyValueForMonth(DateOnly reportMonth)
    {
        _ = reportMonth;

        var residence = Data.HomeResidence;
        if (residence is null)
        {
            return 0m;
        }

        return residence.ResidenceType is HomeResidenceType.Condominium or HomeResidenceType.House
            ? Math.Max(0m, residence.CurrentMarketValue ?? 0m)
            : 0m;
    }

    private decimal GetInterestIncomeForMonth(DateOnly reportMonth)
    {
        _ = reportMonth;
        return 0m;
    }

    private decimal GetInterestCostsForMonth(int year, int month) =>
        Data.ExpenseRecords
            .Where(entry =>
                entry.Year == year &&
                entry.Month == month &&
                (string.Equals(entry.Subcategory, InterestSubcategory, StringComparison.Ordinal) ||
                 entry.Subcategory.EndsWith($" - {InterestSubcategory}", StringComparison.Ordinal)))
            .Sum(entry => entry.Amount);

    private decimal GetAmortizationCostsForMonth(int year, int month) =>
        Data.ExpenseRecords
            .Where(entry =>
                entry.Year == year &&
                entry.Month == month &&
                (string.Equals(entry.Subcategory, AmortizationSubcategory, StringComparison.Ordinal) ||
                 entry.Subcategory.EndsWith($" - {AmortizationSubcategory}", StringComparison.Ordinal)))
            .Sum(entry => entry.Amount);

    private decimal CalculateBalanceForMonth(DateOnly reportMonth) =>
        GetSavingsForMonth(reportMonth) - (GetHousingLoanDebtForMonth(reportMonth) + GetCreditsDebtForMonth(reportMonth));

    private SavingsAccount GetSavingsAccount(Guid accountId) =>
        Data.SavingsAccounts.FirstOrDefault(item => item.Id == accountId)
        ?? throw new InvalidOperationException("The selected savings account does not exist.");

    private static string GetSavingsSubcategory(SavingsAccountType accountType) =>
        accountType switch
        {
            SavingsAccountType.Bank => "Bank",
            SavingsAccountType.Fund => "Fund",
            _ => "Stock"
        };

    private async Task RecalculateSavingsBalancesAsync()
    {
        if (TryGetSavingsCalculationRange(out var startDate, out var endDate))
        {
            await CalculateSavingsReturnsAsync(startDate, endDate);
            return;
        }

        Data.SavingsGeneratedReturns.RemoveAll(_ => true);
        Data.MonthlyBalances.RemoveAll(balance => Data.SavingsAccounts.All(account => account.Id != balance.EntityId));

        await PersistAndNotifyAsync();
    }

    private async Task RecalculateSavingsAccountBalancesAsync(Guid accountId, DateOnly affectedMonth)
    {
        var account = GetSavingsAccount(accountId);
        var normalizedStart = NormalizeMonth(affectedMonth);
        var accountStartMonth = NormalizeMonth(account.CreatedDate);
        if (normalizedStart < accountStartMonth)
        {
            normalizedStart = accountStartMonth;
        }

        var endCandidates = Data.SavingsReturnPeriods
            .Where(period => period.SavingsAccountId == accountId)
            .Select(period => NormalizeMonth(period.EndDate))
            .Concat(Data.SavingsBalanceAdjustments
                .Where(item => item.SavingsAccountId == accountId)
                .Select(item => NormalizeMonth(new DateOnly(item.Year, item.Month, 1))))
            .Concat(Data.ExpenseRecords
                .Where(entry => entry.SavingsAccountId == accountId)
                .Select(entry => NormalizeMonth(new DateOnly(entry.Year, entry.Month, 1))))
            .Concat(Data.IncomeRecords
                .Where(entry => entry.SavingsAccountId == accountId)
                .Select(entry => NormalizeMonth(new DateOnly(entry.Year, entry.Month, 1))))
            .Append(GetGenerationLimit())
            .ToList();

        var normalizedEnd = endCandidates.Count == 0 ? GetGenerationLimit() : endCandidates.Max();

        Data.SavingsGeneratedReturns.RemoveAll(item =>
            item.SavingsAccountId == accountId &&
            NormalizeMonth(new DateOnly(item.Year, item.Month, 1)) >= normalizedStart &&
            NormalizeMonth(new DateOnly(item.Year, item.Month, 1)) <= normalizedEnd);

        RemoveMonthlyBalancesFrom(accountId, normalizedStart);

        var balance = normalizedStart == accountStartMonth
            ? account.OpeningBalance
            : GetBalanceAtOrBeforeMonth(accountId, normalizedStart.AddMonths(-1));

        foreach (var month in EnumerateMonths(normalizedStart.Year, normalizedStart.Month, normalizedEnd.Year, normalizedEnd.Month))
        {
            var monthDate = month.ToDateOnly();
            balance += GetSavingsManualNetChangeForMonth(accountId, monthDate);
            balance = Math.Max(0m, balance);

            var period = GetActiveSavingsReturnPeriods(accountId, monthDate).FirstOrDefault();
            if (period is not null && balance > 0m)
            {
                var grossReturn = Math.Round(balance * ConvertPercentageToDecimal(period.RatePercent) / 12m, 2, MidpointRounding.AwayFromZero);
                var netReturn = account.AccountType == SavingsAccountType.Bank
                    ? Math.Round(grossReturn - (grossReturn * ConvertPercentageToDecimal(period.TaxPercent)), 2, MidpointRounding.AwayFromZero)
                    : grossReturn;

                if (netReturn != 0m)
                {
                    Data.SavingsGeneratedReturns.Add(new SavingsGeneratedReturn
                    {
                        SavingsAccountId = account.Id,
                        Year = month.Year,
                        Month = month.Month,
                        Amount = netReturn
                    });

                    balance += netReturn;
                }
            }

            SetMonthlyBalance(account.Id, monthDate, balance);
        }

        await PersistAndNotifyAsync();
    }

    private bool TryGetSavingsCalculationRange(out DateOnly startDate, out DateOnly endDate)
    {
        var startCandidates = Data.SavingsAccounts
            .Select(account => account.CreatedDate)
            .Concat(Data.SavingsReturnPeriods.Select(period => period.StartDate))
            .Concat(Data.SavingsBalanceAdjustments.Select(item => new DateOnly(item.Year, item.Month, 1)))
            .Concat(Data.ExpenseRecords
                .Where(entry => entry.SavingsAccountId.HasValue)
                .Select(entry => new DateOnly(entry.Year, entry.Month, 1)))
            .Concat(Data.IncomeRecords
                .Where(entry => entry.SavingsAccountId.HasValue)
                .Select(entry => new DateOnly(entry.Year, entry.Month, 1)))
            .ToList();

        if (startCandidates.Count == 0)
        {
            startDate = default;
            endDate = default;
            return false;
        }

        var endCandidates = Data.SavingsReturnPeriods
            .Select(period => period.EndDate)
            .Concat(Data.SavingsBalanceAdjustments.Select(item => new DateOnly(item.Year, item.Month, 1)))
            .Concat(Data.ExpenseRecords
                .Where(entry => entry.SavingsAccountId.HasValue)
                .Select(entry => new DateOnly(entry.Year, entry.Month, 1)))
            .Concat(Data.IncomeRecords
                .Where(entry => entry.SavingsAccountId.HasValue)
                .Select(entry => new DateOnly(entry.Year, entry.Month, 1)))
            .Append(GetGenerationLimit())
            .ToList();

        startDate = NormalizeMonth(startCandidates.Min());
        endDate = NormalizeMonth(endCandidates.Max());
        return true;
    }

    private IReadOnlyList<SavingsReturnPeriod> GetActiveSavingsReturnPeriods(Guid accountId, DateOnly month) =>
        Data.SavingsReturnPeriods
            .Where(period => period.SavingsAccountId == accountId && CoversMonth(period.StartDate, period.EndDate, month))
            .OrderBy(period => period.StartDate)
            .ToList();

    private decimal GetSavingsBalanceBeforeMonth(Guid accountId, DateOnly month)
        => GetBalanceAtOrBeforeMonth(accountId, NormalizeMonth(month).AddMonths(-1));

    private decimal GetSavingsBalanceBeforeOrAtMonth(Guid accountId, DateOnly month)
        => GetBalanceAtOrBeforeMonth(accountId, NormalizeMonth(month));

    private decimal GetSavingsBalanceForMonth(Guid accountId, DateOnly month) =>
        GetSavingsBalanceBeforeOrAtMonth(accountId, month);

    private decimal GetCreditBalanceBeforeOrAtMonth(Guid creditId, DateOnly month) =>
        GetBalanceAtOrBeforeMonth(creditId, NormalizeMonth(month));

    private decimal GetSavingsManualNetChangeUntil(Guid accountId, DateOnly inclusiveMonth)
    {
        var deposits = Data.ExpenseRecords
            .Where(entry =>
                entry.SavingsAccountId == accountId &&
                entry.Category == ExpenseCategory.Savings &&
                NormalizeMonth(new DateOnly(entry.Year, entry.Month, 1)) <= inclusiveMonth)
            .Sum(entry => entry.Amount);

        var withdrawals = Data.IncomeRecords
            .Where(entry =>
                entry.SavingsAccountId == accountId &&
                NormalizeMonth(new DateOnly(entry.Year, entry.Month, 1)) <= inclusiveMonth)
            .Sum(entry => entry.Amount);

        var adjustments = Data.SavingsBalanceAdjustments
            .Where(item =>
                item.SavingsAccountId == accountId &&
                NormalizeMonth(new DateOnly(item.Year, item.Month, 1)) <= inclusiveMonth)
            .Sum(item => item.Amount);

        return deposits - withdrawals + adjustments;
    }

    private decimal GetSavingsManualNetChangeForMonth(Guid accountId, DateOnly month)
    {
        var normalizedMonth = NormalizeMonth(month);
        var deposits = Data.ExpenseRecords
            .Where(entry =>
                entry.SavingsAccountId == accountId &&
                entry.Category == ExpenseCategory.Savings &&
                NormalizeMonth(new DateOnly(entry.Year, entry.Month, 1)) == normalizedMonth)
            .Sum(entry => entry.Amount);

        var withdrawals = Data.IncomeRecords
            .Where(entry =>
                entry.SavingsAccountId == accountId &&
                NormalizeMonth(new DateOnly(entry.Year, entry.Month, 1)) == normalizedMonth)
            .Sum(entry => entry.Amount);

        var adjustments = Data.SavingsBalanceAdjustments
            .Where(item =>
                item.SavingsAccountId == accountId &&
                NormalizeMonth(new DateOnly(item.Year, item.Month, 1)) == normalizedMonth)
            .Sum(item => item.Amount);

        return deposits - withdrawals + adjustments;
    }

    private IReadOnlyList<ClosedMonth> GetLatestClosedMonths(int count) =>
        Data.ClosedMonths
            .OrderByDescending(month => month.Year)
            .ThenByDescending(month => month.Month)
            .Take(count)
            .OrderBy(month => month.Year)
            .ThenBy(month => month.Month)
            .ToList();

    private async Task<Guid> AddGeneratedOneTimeExpenseAsync(decimal amount, int year, int month, ExpenseCategory category, string subcategory, string description, string? metadata = null, Guid? transportVehicleId = null, Guid? savingsAccountId = null, Guid? assetId = null)
    {
        var entry = AddOneTimeExpenseEntry(amount, year, month, category, subcategory, description, metadata, transportVehicleId, savingsAccountId, assetId);

        await PersistAndNotifyAndLogAsync(
            BuildLogDescription(_localizer["nav.expenses"], _localizer["expenses.expensesTab"]),
            BuildLogActivity("log.verb.added", "log.entity.expense", GetExpenseActivitySubject(subcategory, description)));

        return entry.Id;
    }

    private ExpenseEntry AddOneTimeExpenseEntry(decimal amount, int year, int month, ExpenseCategory category, string subcategory, string description, string? metadata = null, Guid? transportVehicleId = null, Guid? savingsAccountId = null, Guid? assetId = null)
    {
        var entry = CreateOneTimeExpenseEntry(amount, year, month, category, subcategory, description, metadata, transportVehicleId, savingsAccountId, assetId);
        Data.ExpenseRecords.Add(entry);
        return entry;
    }

    private async Task<Guid> AddSurplusDepositAsync(DateOnly month, decimal surplus, Guid? accountId, SavingsAccountType expectedAccountType)
    {
        if (!accountId.HasValue)
        {
            throw new InvalidOperationException("A savings account must be selected.");
        }

        var account = GetSavingsAccount(accountId.Value);
        if (account.AccountType != expectedAccountType)
        {
            throw new InvalidOperationException("The selected savings account type is not valid for this action.");
        }

        return await AddGeneratedSavingsDepositAsync(account.Id, month, surplus);
    }

    private async Task<Guid> AddGeneratedSavingsDepositAsync(Guid accountId, DateOnly date, decimal amount)
    {
        var account = GetSavingsAccount(accountId);
        var entry = AddSavingsDepositExpenseEntry(account, date, amount);

        await RecalculateSavingsAccountBalancesAsync(account.Id, NormalizeMonth(date));
        await LogActivityAsync(
            BuildLogDescription(_localizer["nav.savings"], GetSavingsTabTitle(account.AccountType)),
            BuildLogActivity("log.verb.registered", "log.entity.deposit", account.AccountName));

        return entry.Id;
    }

    private ExpenseEntry AddSavingsDepositExpenseEntry(SavingsAccount account, DateOnly date, decimal amount)
    {
        var entry = CreateSavingsDepositExpenseEntry(account, date, amount);
        Data.ExpenseRecords.Add(entry);
        return entry;
    }

    private async Task AddSavingsBalanceAdjustmentAsync(SavingsAccount account, DateOnly date, decimal amount)
    {
        Data.SavingsBalanceAdjustments.Add(new SavingsBalanceAdjustment
        {
            SavingsAccountId = account.Id,
            Year = date.Year,
            Month = date.Month,
            Amount = amount
        });

        await RecalculateSavingsAccountBalancesAsync(account.Id, NormalizeMonth(date));
        await LogActivityAsync(
            BuildLogDescription(_localizer["nav.savings"], GetSavingsTabTitle(account.AccountType)),
            BuildLogActivity("log.verb.registered", "log.entity.balanceAdjustment", account.AccountName));
    }

    private static IReadOnlyList<decimal> DistributeEvenlyAcrossPeriods(decimal totalAmount, int periodCount)
    {
        if (periodCount <= 0)
        {
            throw new InvalidOperationException("At least one period is required for distribution.");
        }

        if (totalAmount <= 0m)
        {
            return Enumerable.Repeat(0m, periodCount).ToList();
        }

        var equalAmount = totalAmount / periodCount;
        var amounts = Enumerable.Repeat(equalAmount, periodCount).ToList();
        amounts[^1] = totalAmount - amounts.Take(periodCount - 1).Sum();
        return amounts;
    }

    private void NormalizeClosedMonths()
    {
        Data.ClosedMonths = Data.ClosedMonths
            .Where(month => month.Year > 0 && month.Month is >= 1 and <= 12)
            .GroupBy(month => new { month.Year, month.Month })
            .Select(group => new ClosedMonth
            {
                Year = group.Key.Year,
                Month = group.Key.Month
            })
            .OrderBy(month => month.Year)
            .ThenBy(month => month.Month)
            .ToList();
    }

    private decimal GetLoanDebtForMonth(DateOnly loanStartDate, decimal startingRemainingDebt, Guid loanId, ExpenseCategory category, DateOnly reportMonth) =>
        GetBalanceAtOrBeforeMonth(loanId, NormalizeMonth(reportMonth));

    private MonthKey GetLatestMonth()
    {
        var currentMonth = GetCurrentMonth().ToDateOnly();
        return GetAllMonths()
            .Where(month => month.ToDateOnly() <= currentMonth)
            .Distinct()
            .OrderByDescending(month => month.ToDateOnly())
            .FirstOrDefault(GetCurrentMonth());
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

        foreach (var balance in Data.MonthlyBalances)
        {
            yield return new MonthKey(balance.Year, balance.Month);
        }
    }

    private (DateOnly StartDate, DateOnly EndDate) GetDefaultMonthlyReportRange()
    {
        var currentMonth = GetCurrentMonth().ToDateOnly();
        var earliestRelevantMonth = GetAllMonths()
            .Select(month => month.ToDateOnly())
            .Where(month => month <= currentMonth)
            .DefaultIfEmpty(currentMonth)
            .Min();

        var earliestAllowedMonth = currentMonth.AddMonths(-11);
        var startDate = earliestRelevantMonth < earliestAllowedMonth
            ? earliestAllowedMonth
            : earliestRelevantMonth;

        return (startDate, currentMonth);
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
