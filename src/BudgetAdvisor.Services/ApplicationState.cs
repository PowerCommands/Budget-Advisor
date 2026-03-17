using BudgetAdvisor.Domain.Enums;
using BudgetAdvisor.Domain.Models;
using BudgetAdvisor.Services.Extensions;
using System.Globalization;

namespace BudgetAdvisor.Services;

public sealed class ApplicationState
{
    private const string ApplicationDataKey = "budget-advisor.application-data";
    private const string SalaryIncomeType = "Salary";
    private const string TaxRefundIncomeType = "Tax refund";
    private const string InheritanceIncomeType = "Inheritance";
    private const string GiftIncomeType = "Gift";
    private const string OtherIncomeType = "Other";
    private const string InterestSubcategory = "Interest";
    private const string AmortizationSubcategory = "Amortization";
    private const string LeasingSubcategory = "Leasing";
    private const string AssetSaleSubcategory = "Asset Sale";
    private const string CreditMetadata = "Credit";
    private const string VehicleAssetType = "Vehicle";
    private const string HousingAssetType = "Housing";
    private const string DefaultSavingsAccountName = "Savings";

    private readonly LocalStorageService _localStorageService;
    private readonly LocalizationService _localizer;

    public event Action? Changed;

    public ApplicationData Data { get; private set; } = new();

    public ApplicationState(LocalStorageService localStorageService, LocalizationService localizer)
    {
        _localStorageService = localStorageService;
        _localizer = localizer;
    }

    public async Task InitializeAsync()
    {
        Data = await _localStorageService.LoadAsync<ApplicationData>(ApplicationDataKey) ?? new ApplicationData();
        NormalizeData();
    }

    public IReadOnlyList<ExpenseEntry> GetFilteredExpenseEntries(ExpenseTableFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        return ApplyExpenseTableFilter(GetScopedExpenseEntries(filter.Scope), filter, includeCategoryFilter: true)
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

        var subcategoryOnly = ScopeUsesSubcategoryFilterOnly(filter.Scope);

        return ApplyExpenseTableFilter(GetScopedExpenseEntries(filter.Scope), filter, includeCategoryFilter: false)
            .Select(entry => new ExpenseFilterCategoryOption
            {
                Value = subcategoryOnly
                    ? entry.Subcategory.Trim()
                    : BuildCombinedCategoryFilterValue(entry.Category, entry.Subcategory),
                Category = entry.Category,
                Subcategory = entry.Subcategory.Trim()
            })
            .Where(option => !string.IsNullOrWhiteSpace(option.Subcategory))
            .DistinctBy(option => option.Value)
            .OrderBy(option => option.Category)
            .ThenBy(option => option.Subcategory)
            .ToList();
    }

    public async Task AddMemberAsync(string name)
    {
        Data.Members.Add(new HouseholdMember { Name = name.Trim() });
        await PersistAndNotifyAsync();
    }

    public async Task AddOneTimeIncomeAsync(Guid memberId, decimal amount, int year, int month, string type, string? metadata = null, Guid? savingsAccountId = null, Guid? assetId = null)
    {
        Data.IncomeRecords.Add(CreateOneTimeIncomeEntry(memberId, amount, year, month, NormalizeSystemIncomeType(type), metadata, savingsAccountId, assetId));

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
                Type = SalaryIncomeType,
                SeriesId = seriesId
            });
        }

        await PersistAndNotifyAsync();
    }

    public async Task AddYearlyIncomeAsync(Guid memberId, decimal amount, int year, string type)
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
                Type = NormalizeSystemIncomeType(type),
                SeriesId = seriesId
            });
        }

        await PersistAndNotifyAsync();
    }

    public async Task AddOneTimeExpenseAsync(decimal amount, int year, int month, ExpenseCategory category, string subcategory, string description, string? metadata = null, Guid? transportVehicleId = null, Guid? savingsAccountId = null, Guid? assetId = null)
    {
        Data.ExpenseRecords.Add(CreateOneTimeExpenseEntry(amount, year, month, category, subcategory, description, metadata, transportVehicleId, savingsAccountId, assetId));

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

        await RegenerateSubscriptionExpensesAsync();
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

        await PersistAndNotifyAsync();
    }

    public async Task AddTransportDefinitionAsync(decimal amount, int intervalMonths, int startYear, int startMonth, int endYear, int endMonth, string subcategory, Guid vehicleId)
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
                Description = definition.Subcategory,
                TransportDefinitionId = definition.Id,
                TransportVehicleId = vehicleId
            });
        }

        await PersistAndNotifyAsync();
    }

    public async Task AddHousingLoanAsync(string name, string lender, DateOnly loanStartDate, decimal initialLoanAmount, decimal remainingDebt)
    {
        Data.HousingLoans.Add(new HousingLoan
        {
            Name = name.Trim(),
            Lender = lender.Trim(),
            LoanStartDate = loanStartDate,
            InitialLoanAmount = initialLoanAmount,
            StartingRemainingDebt = remainingDebt,
            RemainingDebt = remainingDebt,
            CurrentAmortization = 0m
        });

        await RecalculateHousingLoanCostsAsync();
    }

    public async Task AddLoanInterestBindingPeriodAsync(Guid loanId, DateOnly startDate, DateOnly endDate, decimal interestRate)
    {
        ValidateAnyLoanExists(loanId);
        ValidateNoMonthOverlap(
            startDate,
            endDate,
            Data.LoanInterestBindingPeriods.Where(period => period.LoanId == loanId).Select(period => (period.StartDate, period.EndDate)));

        Data.LoanInterestBindingPeriods.Add(new LoanInterestBindingPeriod
        {
            LoanId = loanId,
            StartDate = startDate,
            EndDate = endDate,
            InterestRate = interestRate
        });

        await RecalculateLoanCategoryCostsAsync(loanId);
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
        if (effectiveStartDate < NormalizeMonth(period.StartDate))
        {
            effectiveStartDate = NormalizeMonth(period.StartDate);
        }

        var effectiveEndDate = NormalizeMonth(period.EndDate);
        if (effectiveStartDate > effectiveEndDate)
        {
            return;
        }

        await CalculateHousingLoanCostsAsync(effectiveStartDate, effectiveEndDate);
    }

    public async Task AddTransportLoanAsync(Guid vehicleId, string lender, DateOnly loanStartDate, decimal initialLoanAmount, decimal remainingDebt)
    {
        _ = GetTransportVehicle(vehicleId);

        Data.TransportLoans.Add(new TransportLoan
        {
            VehicleId = vehicleId,
            Lender = lender.Trim(),
            LoanStartDate = loanStartDate,
            InitialLoanAmount = initialLoanAmount,
            StartingRemainingDebt = remainingDebt,
            RemainingDebt = remainingDebt,
            CurrentAmortization = 0m
        });

        await RecalculateTransportCostsAsync();
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
        await PersistAndNotifyAsync();
    }

    public async Task AddSavingsAccountAsync(SavingsAccountType accountType, string providerName, string accountName, decimal openingBalance)
    {
        var account = new SavingsAccount
        {
            AccountType = accountType,
            ProviderName = providerName.Trim(),
            AccountName = accountName.Trim(),
            CreatedDate = DateOnly.FromDateTime(DateTime.Today),
            OpeningBalance = openingBalance,
            CurrentBalance = openingBalance
        };

        Data.SavingsAccounts.Add(account);
        await PersistAndNotifyAsync();
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

        await RecalculateSavingsBalancesAsync();
    }

    public async Task AddSavingsDepositAsync(Guid accountId, DateOnly date, decimal amount)
    {
        var account = GetSavingsAccount(accountId);
        if (amount <= 0m)
        {
            throw new InvalidOperationException("Deposit amount must be greater than zero.");
        }

        Data.ExpenseRecords.Add(CreateSavingsDepositExpenseEntry(account, date, amount));

        await RecalculateSavingsBalancesAsync();
    }

    public async Task AddSavingsWithdrawalAsync(Guid accountId, DateOnly date, decimal amount)
    {
        var account = GetSavingsAccount(accountId);
        if (amount <= 0m)
        {
            throw new InvalidOperationException("Withdrawal amount must be greater than zero.");
        }

        var balanceBeforeWithdrawal = GetSavingsBalanceBeforeOrAtMonth(account.Id, NormalizeMonth(date));
        if (amount > balanceBeforeWithdrawal)
        {
            throw new InvalidOperationException("Withdrawal amount cannot exceed the account balance.");
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

        await RecalculateSavingsBalancesAsync();
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

        await PersistAndNotifyAsync();
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

        await PersistAndNotifyAsync();
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
            return;
        }

        await PersistAndNotifyAsync();
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
            var balance = GetSavingsBalanceBeforeMonth(account.Id, normalizedStart);

            foreach (var month in EnumerateMonths(normalizedStart.Year, normalizedStart.Month, normalizedEnd.Year, normalizedEnd.Month))
            {
                var monthDate = month.ToDateOnly();
                if (NormalizeMonth(account.CreatedDate) > monthDate)
                {
                    continue;
                }

                balance += GetSavingsManualNetChangeForMonth(account.Id, monthDate);

                var period = GetActiveSavingsReturnPeriods(account.Id, monthDate).FirstOrDefault();
                if (period is null || balance <= 0m)
                {
                    continue;
                }

                var grossReturn = Math.Round(balance * ConvertPercentageToDecimal(period.RatePercent) / 12m, 2, MidpointRounding.AwayFromZero);
                var netReturn = account.AccountType == SavingsAccountType.Bank
                    ? Math.Round(grossReturn - (grossReturn * ConvertPercentageToDecimal(period.TaxPercent)), 2, MidpointRounding.AwayFromZero)
                    : grossReturn;

                if (netReturn == 0m)
                {
                    continue;
                }

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

            RefreshSavingsAccountBalance(account.Id);
        }

        await PersistAndNotifyAsync();
        return generatedCount;
    }

    public async Task AddCreditAsync(string name, string provider, DateOnly startDate, decimal creditLimit, decimal remainingDebt, decimal monthlyInterestRate, bool resetAtEndOfMonth)
    {
        ValidateCreditValues(creditLimit, remainingDebt, monthlyInterestRate);

        Data.Credits.Add(new Credit
        {
            Name = name.Trim(),
            Provider = provider.Trim(),
            StartDate = startDate,
            CreditLimit = creditLimit,
            StartingRemainingDebt = remainingDebt,
            RemainingDebt = remainingDebt,
            MonthlyInterestRate = monthlyInterestRate,
            ResetAtEndOfMonth = resetAtEndOfMonth
        });

        await PersistAndNotifyAsync();
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

        await PersistAndNotifyAsync();
    }

    public async Task AddCreditPurchaseAsync(Guid creditId, DateOnly purchaseDate, decimal amount, ExpenseCategory category, string subcategory)
    {
        var credit = GetCredit(creditId);
        if (amount <= 0m)
        {
            throw new InvalidOperationException("Purchase amount must be greater than zero.");
        }

        if (credit.RemainingDebt + amount > credit.CreditLimit)
        {
            throw new InvalidOperationException("The purchase would exceed the credit limit.");
        }

        var normalizedSubcategory = subcategory.Trim();
        Data.ExpenseRecords.Add(new ExpenseEntry
        {
            Amount = amount,
            Year = purchaseDate.Year,
            Month = purchaseDate.Month,
            Category = category,
            Subcategory = normalizedSubcategory,
            Metadata = CreditMetadata,
            Description = normalizedSubcategory,
            CreditId = credit.Id,
            CreditCostSource = CreditCostSource.ManualPurchase
        });

        credit.RemainingDebt += amount;
        await PersistAndNotifyAsync();
    }

    public async Task PostCreditMonthAsync(Guid creditId, DateOnly month, decimal amortizationAmount, bool payFullAmount)
    {
        var credit = GetCredit(creditId);
        var amountToPost = payFullAmount ? credit.RemainingDebt : Math.Min(amortizationAmount, credit.RemainingDebt);

        if (amortizationAmount < 0m)
        {
            throw new InvalidOperationException("Amortization amount must not be negative.");
        }

        if (amortizationAmount > credit.RemainingDebt && !payFullAmount)
        {
            throw new InvalidOperationException("Amortization amount cannot exceed the remaining debt.");
        }

        if (amountToPost > 0m)
        {
            credit.RemainingDebt = Math.Max(0m, credit.RemainingDebt - amountToPost);

            Data.ExpenseRecords.Add(new ExpenseEntry
            {
                Amount = amountToPost,
                Year = month.Year,
                Month = month.Month,
                Category = ExpenseCategory.Credits,
                Subcategory = $"{credit.Name} - {AmortizationSubcategory}",
                Description = $"{credit.Name} - {AmortizationSubcategory}",
                CreditId = credit.Id,
                CreditCostSource = CreditCostSource.ManualAmortization
            });
        }

        if (credit.RemainingDebt > 0m && credit.MonthlyInterestRate > 0m)
        {
            var interestAmount = Math.Round(credit.RemainingDebt * ConvertPercentageToDecimal(credit.MonthlyInterestRate) / 12m, 2, MidpointRounding.AwayFromZero);
            if (interestAmount > 0m)
            {
                Data.ExpenseRecords.Add(new ExpenseEntry
                {
                    Amount = interestAmount,
                    Year = month.Year,
                    Month = month.Month,
                    Category = ExpenseCategory.Credits,
                    Subcategory = $"{credit.Name} - {InterestSubcategory}",
                    Description = $"{credit.Name} - {InterestSubcategory}",
                    CreditId = credit.Id,
                    CreditCostSource = CreditCostSource.GeneratedInterest
                });

                credit.RemainingDebt += interestAmount;
            }
        }

        await PersistAndNotifyAsync();
    }

    public async Task<int> CalculateHousingLoanCostsAsync(DateOnly startDate, DateOnly endDate)
    {
        var normalizedStart = NormalizeMonth(startDate);
        var normalizedEnd = NormalizeMonth(endDate);

        if (normalizedEnd < normalizedStart)
        {
            (normalizedStart, normalizedEnd) = (normalizedEnd, normalizedStart);
        }

        RemoveGeneratedExpensesInRange(ExpenseCategory.Housing, normalizedStart, normalizedEnd, includeLeasing: false);

        var generatedCount = 0;

        foreach (var loan in Data.HousingLoans)
        {
            var debtForMonth = GetOpeningDebtForLoan(loan.Id, loan.StartingRemainingDebt, ExpenseCategory.Housing, normalizedStart);

            foreach (var month in EnumerateMonths(normalizedStart.Year, normalizedStart.Month, normalizedEnd.Year, normalizedEnd.Month))
            {
                var monthDate = month.ToDateOnly();

                foreach (var period in GetActiveLoanInterestBindingPeriods(loan.Id, monthDate))
                {
                    var monthlyInterestCost = Math.Round(debtForMonth * ConvertPercentageToDecimal(period.InterestRate) / 12m, 2, MidpointRounding.AwayFromZero);
                    Data.ExpenseRecords.Add(CreateLoanExpenseEntry(
                        loan.Id,
                        loan.Lender,
                        ExpenseCategory.Housing,
                        month,
                        InterestSubcategory,
                        monthlyInterestCost,
                        period.Id,
                        null,
                        null,
                        null));
                    generatedCount++;
                }

                foreach (var plan in GetActiveLoanAmortizationPlans(loan.Id, monthDate))
                {
                    var amortizationAmount = Math.Min(debtForMonth, plan.MonthlyAmortizationAmount);
                    Data.ExpenseRecords.Add(CreateLoanExpenseEntry(
                        loan.Id,
                        loan.Lender,
                        ExpenseCategory.Housing,
                        month,
                        AmortizationSubcategory,
                        amortizationAmount,
                        null,
                        plan.Id,
                        null,
                        null));

                    debtForMonth = Math.Max(0m, debtForMonth - amortizationAmount);
                    generatedCount++;
                }
            }

            RefreshLoanDerivedValues(loan.Id);
        }

        await PersistAndNotifyAsync();
        return generatedCount;
    }

    public async Task<int> CalculateTransportCostsAsync(DateOnly startDate, DateOnly endDate)
    {
        var normalizedStart = NormalizeMonth(startDate);
        var normalizedEnd = NormalizeMonth(endDate);

        if (normalizedEnd < normalizedStart)
        {
            (normalizedStart, normalizedEnd) = (normalizedEnd, normalizedStart);
        }

        RemoveGeneratedExpensesInRange(ExpenseCategory.Transport, normalizedStart, normalizedEnd, includeLeasing: true);

        var generatedCount = 0;

        foreach (var loan in Data.TransportLoans)
        {
            var debtForMonth = GetOpeningDebtForLoan(loan.Id, loan.StartingRemainingDebt, ExpenseCategory.Transport, normalizedStart);
            var vehicleMetadata = GetTransportVehicleName(loan.VehicleId);

            foreach (var month in EnumerateMonths(normalizedStart.Year, normalizedStart.Month, normalizedEnd.Year, normalizedEnd.Month))
            {
                var monthDate = month.ToDateOnly();

                foreach (var period in GetActiveLoanInterestBindingPeriods(loan.Id, monthDate))
                {
                    var monthlyInterestCost = Math.Round(debtForMonth * ConvertPercentageToDecimal(period.InterestRate) / 12m, 2, MidpointRounding.AwayFromZero);
                    Data.ExpenseRecords.Add(CreateLoanExpenseEntry(
                        loan.Id,
                        loan.Lender,
                        ExpenseCategory.Transport,
                        month,
                        InterestSubcategory,
                        monthlyInterestCost,
                        period.Id,
                        null,
                        vehicleMetadata,
                        loan.VehicleId));
                    generatedCount++;
                }

                foreach (var plan in GetActiveLoanAmortizationPlans(loan.Id, monthDate))
                {
                    var amortizationAmount = Math.Min(debtForMonth, plan.MonthlyAmortizationAmount);
                    Data.ExpenseRecords.Add(CreateLoanExpenseEntry(
                        loan.Id,
                        loan.Lender,
                        ExpenseCategory.Transport,
                        month,
                        AmortizationSubcategory,
                        amortizationAmount,
                        null,
                        plan.Id,
                        vehicleMetadata,
                        loan.VehicleId));

                    debtForMonth = Math.Max(0m, debtForMonth - amortizationAmount);
                    generatedCount++;
                }
            }

            RefreshTransportLoanDerivedValues(loan.Id);
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
                    Description = LeasingSubcategory,
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
            foreach (var month in EnumerateMonths(normalizedStart.Year, normalizedStart.Month, normalizedEnd.Year, normalizedEnd.Month))
            {
                var monthDate = month.ToDateOnly();
                var debtForMonth = GetCreditDebtBeforeResetForMonth(credit, monthDate);

                if (debtForMonth > 0m && credit.MonthlyInterestRate > 0m)
                {
                    var interestAmount = Math.Round(debtForMonth * ConvertPercentageToDecimal(credit.MonthlyInterestRate) / 12m, 2, MidpointRounding.AwayFromZero);
                    if (interestAmount > 0m)
                    {
                        Data.ExpenseRecords.Add(new ExpenseEntry
                        {
                            Amount = interestAmount,
                            Year = month.Year,
                            Month = month.Month,
                            Category = ExpenseCategory.Credits,
                            Subcategory = InterestSubcategory,
                            Description = $"{credit.Provider.Trim()} {InterestSubcategory}",
                            CreditId = credit.Id,
                            CreditCostSource = CreditCostSource.GeneratedInterest
                        });

                        debtForMonth += interestAmount;
                        generatedCount++;
                    }
                }

                if (credit.ResetAtEndOfMonth && debtForMonth > 0m)
                {
                    Data.ExpenseRecords.Add(new ExpenseEntry
                    {
                        Amount = debtForMonth,
                        Year = month.Year,
                        Month = month.Month,
                        Category = ExpenseCategory.Credits,
                        Subcategory = AmortizationSubcategory,
                        Description = $"{credit.Provider.Trim()} {AmortizationSubcategory}",
                        CreditId = credit.Id,
                        CreditCostSource = CreditCostSource.GeneratedResetAmortization
                    });
                    generatedCount++;
                }
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

    public string GetExpenseSubcategoryDisplay(ExpenseEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var metadata = GetExpenseMetadataDisplay(entry.Metadata);
        var subcategory = GetLocalizedExpenseLabel(entry.Subcategory);

        return string.IsNullOrWhiteSpace(metadata)
            ? subcategory
            : $"{metadata} - {subcategory}";
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
            var monthDate = month.ToDateOnly();
            var income = GetMonthlyIncome(month.Year, month.Month);
            var expenses = GetMonthlyExpenses(month.Year, month.Month);
            var housingLoans = GetHousingLoanDebtForMonth(monthDate);
            var credits = GetCreditsDebtForMonth(monthDate);
            var savings = GetSavingsForMonth(monthDate);
            var interest = GetInterestIncomeForMonth(monthDate) - GetInterestCostsForMonth(month.Year, month.Month);
            var amortization = GetAmortizationCostsForMonth(month.Year, month.Month);
            balance = CalculateBalanceForMonth(monthDate);

            rows.Add(new MonthlyReportRow
            {
                Period = $"{month.Year:0000}/{month.Month:00}",
                Income = income,
                Expenses = expenses,
                HousingLoans = housingLoans,
                Credits = credits,
                Savings = savings,
                Interest = interest,
                Amortization = amortization,
                Balance = balance
            });
        }

        return rows.OrderByDescending(row => row.Period, StringComparer.Ordinal).ToList();
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
        var expense = Data.ExpenseRecords.FirstOrDefault(item => item.Id == expenseId);
        if (expense is null)
        {
            return;
        }

        Data.ExpenseRecords.RemoveAll(item => item.Id == expenseId);

        if (expense.SavingsAccountId.HasValue)
        {
            await RecalculateSavingsBalancesAsync();
            return;
        }

        await PersistAndNotifyAsync();
    }

    public async Task RemoveSubscriptionAsync(Guid subscriptionId)
    {
        Data.Subscriptions.RemoveAll(subscription => subscription.Id == subscriptionId);
        Data.ExpenseRecords.RemoveAll(expense => expense.SubscriptionDefinitionId == subscriptionId);
        await RegenerateSubscriptionExpensesAsync();
    }

    public async Task RemoveHousingDefinitionAsync(Guid definitionId)
    {
        Data.HousingDefinitions.RemoveAll(definition => definition.Id == definitionId);
        Data.ExpenseRecords.RemoveAll(expense => expense.HousingDefinitionId == definitionId);
        await PersistAndNotifyAsync();
    }

    public async Task RemoveTransportDefinitionAsync(Guid definitionId)
    {
        Data.TransportDefinitions.RemoveAll(definition => definition.Id == definitionId);
        Data.ExpenseRecords.RemoveAll(expense => expense.TransportDefinitionId == definitionId);
        await PersistAndNotifyAsync();
    }

    public async Task RemoveHousingLoanAsync(Guid loanId)
    {
        Data.HousingLoans.RemoveAll(loan => loan.Id == loanId);
        Data.LoanInterestBindingPeriods.RemoveAll(period => period.LoanId == loanId);
        Data.LoanAmortizationPlans.RemoveAll(plan => plan.LoanId == loanId);
        Data.ExpenseRecords.RemoveAll(expense => expense.LoanId == loanId);
        await RecalculateHousingLoanCostsAsync();
    }

    public async Task RemoveLoanInterestBindingPeriodAsync(Guid periodId)
    {
        var period = Data.LoanInterestBindingPeriods.FirstOrDefault(item => item.Id == periodId);
        if (period is null)
        {
            return;
        }

        Data.LoanInterestBindingPeriods.RemoveAll(item => item.Id == periodId);
        Data.ExpenseRecords.RemoveAll(expense => expense.LoanInterestBindingPeriodId == periodId);
        await RecalculateLoanCategoryCostsAsync(period.LoanId);
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
    }

    public async Task RemoveTransportLoanAsync(Guid loanId)
    {
        Data.TransportLoans.RemoveAll(loan => loan.Id == loanId);
        Data.LoanInterestBindingPeriods.RemoveAll(period => period.LoanId == loanId);
        Data.LoanAmortizationPlans.RemoveAll(plan => plan.LoanId == loanId);
        Data.ExpenseRecords.RemoveAll(expense => expense.LoanId == loanId && expense.Category == ExpenseCategory.Transport);
        await RecalculateTransportCostsAsync();
    }

    public async Task RemoveTransportLeasingContractAsync(Guid contractId)
    {
        Data.TransportLeasingContracts.RemoveAll(contract => contract.Id == contractId);
        Data.ExpenseRecords.RemoveAll(expense => expense.TransportLeasingContractId == contractId);
        await RecalculateTransportCostsAsync();
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
        await PersistAndNotifyAsync();
    }

    public async Task RemoveCreditAsync(Guid creditId)
    {
        Data.Credits.RemoveAll(credit => credit.Id == creditId);
        Data.ExpenseRecords.RemoveAll(expense => expense.CreditId == creditId);
        await PersistAndNotifyAsync();
    }

    public async Task RemoveSavingsAccountAsync(Guid accountId)
    {
        Data.SavingsAccounts.RemoveAll(account => account.Id == accountId);
        Data.SavingsReturnPeriods.RemoveAll(period => period.SavingsAccountId == accountId);
        Data.SavingsGeneratedReturns.RemoveAll(item => item.SavingsAccountId == accountId);
        Data.ExpenseRecords.RemoveAll(expense => expense.SavingsAccountId == accountId);
        Data.IncomeRecords.RemoveAll(income => income.SavingsAccountId == accountId);
        await RecalculateSavingsBalancesAsync();
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

        if (income.SavingsAccountId.HasValue)
        {
            await RecalculateSavingsBalancesAsync();
            return;
        }

        await PersistAndNotifyAsync();
    }

    private async Task RegenerateSubscriptionExpensesAsync()
    {
        await GenerateMissingSubscriptionExpensesAsync();
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
        Data.Members ??= [];
        Data.IncomeRecords ??= [];
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
        Data.TransportLeasingContracts ??= [];
        Data.Credits ??= [];
        Data.Debts ??= [];
        Data.SavingsAccounts ??= [];
        Data.SavingsReturnPeriods ??= [];
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
            Data.ThemeMode = "Primary";
        }

        foreach (var income in Data.IncomeRecords)
        {
            if (string.IsNullOrWhiteSpace(income.Type))
            {
                income.Type = OtherIncomeType;
            }

            income.Metadata ??= string.Empty;
        }

        foreach (var loan in Data.HousingLoans)
        {
            loan.Name = string.IsNullOrWhiteSpace(loan.Name) ? loan.Lender : loan.Name.Trim();
            if (loan.RemainingDebt < 0m)
            {
                loan.RemainingDebt = 0m;
            }

            RefreshLoanDerivedValues(loan.Id);
        }

        foreach (var loan in Data.TransportLoans)
        {
            if (loan.RemainingDebt < 0m)
            {
                loan.RemainingDebt = 0m;
            }

            RefreshTransportLoanDerivedValues(loan.Id);
        }

        foreach (var expense in Data.ExpenseRecords)
        {
            expense.Metadata ??= string.Empty;
        }

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
            if (credit.RemainingDebt < 0m)
            {
                credit.RemainingDebt = 0m;
            }

            RefreshCreditDerivedValues(credit.Id);
        }

        foreach (var account in Data.SavingsAccounts)
        {
            if (string.IsNullOrWhiteSpace(account.AccountName))
            {
                account.AccountName = DefaultSavingsAccountName;
            }

            RefreshSavingsAccountBalance(account.Id);
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

    private IEnumerable<ExpenseEntry> ApplyExpenseTableFilter(IEnumerable<ExpenseEntry> source, ExpenseTableFilter filter, bool includeCategoryFilter)
    {
        var query = source;

        if (!string.IsNullOrWhiteSpace(filter.Name))
        {
            var nameFilter = filter.Name.Trim();
            query = query.Where(entry =>
                entry.Description.Contains(nameFilter, StringComparison.OrdinalIgnoreCase) ||
                entry.Metadata.Contains(nameFilter, StringComparison.OrdinalIgnoreCase) ||
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

        if (!includeCategoryFilter || string.IsNullOrWhiteSpace(filter.CategoryFilter))
        {
            return query;
        }

        var categoryFilter = filter.CategoryFilter.Trim();

        return ScopeUsesSubcategoryFilterOnly(filter.Scope)
            ? query.Where(entry => string.Equals(entry.Subcategory.Trim(), categoryFilter, StringComparison.OrdinalIgnoreCase))
            : query.Where(entry => string.Equals(
                BuildCombinedCategoryFilterValue(entry.Category, entry.Subcategory),
                categoryFilter,
                StringComparison.OrdinalIgnoreCase));
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
                .Select(period => period.StartDate))
            .Concat(Data.LoanAmortizationPlans
                .Where(plan => Data.HousingLoans.Any(loan => loan.Id == plan.LoanId))
                .Select(plan => plan.StartDate))
            .ToList();

        var endCandidates = Data.LoanInterestBindingPeriods
            .Where(period => Data.HousingLoans.Any(loan => loan.Id == period.LoanId))
            .Select(period => period.EndDate)
            .Concat(Data.LoanAmortizationPlans
                .Where(plan => Data.HousingLoans.Any(loan => loan.Id == plan.LoanId))
                .Select(plan => plan.EndDate))
            .ToList();

        if (startCandidates.Count == 0 || endCandidates.Count == 0)
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
                .Select(period => period.StartDate))
            .Concat(Data.LoanAmortizationPlans
                .Where(plan => transportLoanIds.Contains(plan.LoanId))
                .Select(plan => plan.StartDate))
            .Concat(Data.TransportLeasingContracts.Select(contract => contract.StartDate))
            .ToList();

        var endCandidates = Data.LoanInterestBindingPeriods
            .Where(period => transportLoanIds.Contains(period.LoanId))
            .Select(period => period.EndDate)
            .Concat(Data.LoanAmortizationPlans
                .Where(plan => transportLoanIds.Contains(plan.LoanId))
                .Select(plan => plan.EndDate))
            .Concat(Data.TransportLeasingContracts.Select(contract => contract.EndDate))
            .ToList();

        if (startCandidates.Count == 0 || endCandidates.Count == 0)
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
        Guid? transportVehicleId) => new()
        {
            Amount = amount,
            Year = month.Year,
            Month = month.Month,
            Category = category,
            Subcategory = subcategory,
            Metadata = metadata ?? string.Empty,
            Description = $"{lender.Trim()} {subcategory}",
            LoanId = loanId,
            LoanInterestBindingPeriodId = interestBindingPeriodId,
            LoanAmortizationPlanId = amortizationPlanId,
            TransportVehicleId = category == ExpenseCategory.Transport ? transportVehicleId : null
        };

    private IReadOnlyList<LoanInterestBindingPeriod> GetActiveLoanInterestBindingPeriods(Guid loanId, DateOnly month) =>
        Data.LoanInterestBindingPeriods
            .Where(period => period.LoanId == loanId && CoversMonth(period.StartDate, period.EndDate, month))
            .OrderBy(period => period.StartDate)
            .ToList();

    private IReadOnlyList<LoanAmortizationPlan> GetActiveLoanAmortizationPlans(Guid loanId, DateOnly month) =>
        Data.LoanAmortizationPlans
            .Where(plan => plan.LoanId == loanId && CoversMonth(plan.StartDate, plan.EndDate, month))
            .OrderBy(plan => plan.StartDate)
            .ToList();

    private void RemoveGeneratedExpensesInRange(ExpenseCategory category, DateOnly startDate, DateOnly endDate, bool includeLeasing)
    {
        Data.ExpenseRecords.RemoveAll(expense =>
            expense.LoanId.HasValue &&
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

    private decimal GetOpeningDebtForLoan(Guid loanId, decimal startingRemainingDebt, ExpenseCategory category, DateOnly startDate)
    {
        var amortizedBeforeStart = Data.ExpenseRecords
            .Where(expense =>
                expense.LoanId == loanId &&
                expense.Category == category &&
                expense.LoanAmortizationPlanId.HasValue &&
                NormalizeMonth(new DateOnly(expense.Year, expense.Month, 1)) < startDate)
            .Sum(expense => expense.Amount);

        return Math.Max(0m, startingRemainingDebt - amortizedBeforeStart);
    }

    private void RefreshLoanDerivedValues(Guid loanId)
    {
        var loan = Data.HousingLoans.FirstOrDefault(item => item.Id == loanId);
        if (loan is null)
        {
            return;
        }

        var todayMonth = NormalizeMonth(DateOnly.FromDateTime(DateTime.Today));
        loan.CurrentAmortization = GetActiveLoanAmortizationPlans(loan.Id, todayMonth)
            .Sum(plan => plan.MonthlyAmortizationAmount);

        var totalCalculatedAmortization = Data.ExpenseRecords
            .Where(expense => expense.LoanId == loan.Id && expense.LoanAmortizationPlanId.HasValue)
            .Sum(expense => expense.Amount);

        loan.RemainingDebt = Math.Max(0m, loan.StartingRemainingDebt - totalCalculatedAmortization);
    }

    private void RefreshTransportLoanDerivedValues(Guid loanId)
    {
        var loan = Data.TransportLoans.FirstOrDefault(item => item.Id == loanId);
        if (loan is null)
        {
            return;
        }

        var todayMonth = NormalizeMonth(DateOnly.FromDateTime(DateTime.Today));
        loan.CurrentAmortization = GetActiveLoanAmortizationPlans(loan.Id, todayMonth)
            .Sum(plan => plan.MonthlyAmortizationAmount);

        var totalCalculatedAmortization = Data.ExpenseRecords
            .Where(expense =>
                expense.LoanId == loan.Id &&
                expense.Category == ExpenseCategory.Transport &&
                expense.LoanAmortizationPlanId.HasValue)
            .Sum(expense => expense.Amount);

        loan.RemainingDebt = Math.Max(0m, loan.StartingRemainingDebt - totalCalculatedAmortization);
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

    private decimal GetCreditDebtBeforeResetForMonth(Credit credit, DateOnly reportMonth)
    {
        var normalizedReportMonth = NormalizeMonth(reportMonth);
        if (NormalizeMonth(credit.StartDate) > normalizedReportMonth)
        {
            return 0m;
        }

        var purchases = Data.ExpenseRecords
            .Where(expense =>
                expense.CreditId == credit.Id &&
                expense.CreditCostSource == CreditCostSource.ManualPurchase &&
                NormalizeMonth(new DateOnly(expense.Year, expense.Month, 1)) <= normalizedReportMonth)
            .Sum(expense => expense.Amount);

        var manualAmortizations = Data.ExpenseRecords
            .Where(expense =>
                expense.CreditId == credit.Id &&
                expense.CreditCostSource == CreditCostSource.ManualAmortization &&
                NormalizeMonth(new DateOnly(expense.Year, expense.Month, 1)) <= normalizedReportMonth)
            .Sum(expense => expense.Amount);

        var generatedResetBeforeMonth = Data.ExpenseRecords
            .Where(expense =>
                expense.CreditId == credit.Id &&
                expense.CreditCostSource == CreditCostSource.GeneratedResetAmortization &&
                NormalizeMonth(new DateOnly(expense.Year, expense.Month, 1)) < normalizedReportMonth)
            .Sum(expense => expense.Amount);

        var generatedInterestBeforeMonth = Data.ExpenseRecords
            .Where(expense =>
                expense.CreditId == credit.Id &&
                expense.CreditCostSource == CreditCostSource.GeneratedInterest &&
                NormalizeMonth(new DateOnly(expense.Year, expense.Month, 1)) < normalizedReportMonth)
            .Sum(expense => expense.Amount);

        return Math.Max(0m, credit.StartingRemainingDebt + purchases + generatedInterestBeforeMonth - manualAmortizations - generatedResetBeforeMonth);
    }

    private decimal GetCreditDebtForMonth(Credit credit, DateOnly reportMonth)
    {
        var normalizedReportMonth = NormalizeMonth(reportMonth);
        var debtBeforeReset = GetCreditDebtBeforeResetForMonth(credit, normalizedReportMonth);
        var generatedInterestForMonth = Data.ExpenseRecords
            .Where(expense =>
                expense.CreditId == credit.Id &&
                expense.CreditCostSource == CreditCostSource.GeneratedInterest &&
                NormalizeMonth(new DateOnly(expense.Year, expense.Month, 1)) == normalizedReportMonth)
            .Sum(expense => expense.Amount);

        var generatedResetForMonth = Data.ExpenseRecords
            .Where(expense =>
                expense.CreditId == credit.Id &&
                expense.CreditCostSource == CreditCostSource.GeneratedResetAmortization &&
                NormalizeMonth(new DateOnly(expense.Year, expense.Month, 1)) == normalizedReportMonth)
            .Sum(expense => expense.Amount);

        return Math.Max(0m, debtBeforeReset + generatedInterestForMonth - generatedResetForMonth);
    }

    private void RefreshCreditDerivedValues(Guid creditId)
    {
        var credit = Data.Credits.FirstOrDefault(item => item.Id == creditId);
        if (credit is null)
        {
            return;
        }

        var purchases = Data.ExpenseRecords
            .Where(expense => expense.CreditId == credit.Id && expense.CreditCostSource == CreditCostSource.ManualPurchase)
            .Sum(expense => expense.Amount);

        var generatedInterest = Data.ExpenseRecords
            .Where(expense => expense.CreditId == credit.Id && expense.CreditCostSource == CreditCostSource.GeneratedInterest)
            .Sum(expense => expense.Amount);

        var amortizations = Data.ExpenseRecords
            .Where(expense =>
                expense.CreditId == credit.Id &&
                expense.CreditCostSource is CreditCostSource.ManualAmortization or CreditCostSource.GeneratedResetAmortization)
            .Sum(expense => expense.Amount);

        credit.RemainingDebt = Math.Max(0m, credit.StartingRemainingDebt + purchases + generatedInterest - amortizations);
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

    private decimal GetMonthlyIncome(int year, int month) =>
        Data.IncomeRecords
            .Where(entry => entry.Year == year && entry.Month == month)
            .Sum(entry => entry.Amount);

    private decimal GetMonthlyExpenses(int year, int month) =>
        Data.ExpenseRecords
            .Where(entry => entry.Year == year && entry.Month == month)
            .Sum(entry => entry.Amount);

    private decimal GetHousingLoanDebtForMonth(DateOnly reportMonth) =>
        Data.HousingLoans.Sum(loan => GetLoanDebtForMonth(loan.LoanStartDate, loan.StartingRemainingDebt, loan.Id, ExpenseCategory.Housing, reportMonth));

    private decimal GetCreditsDebtForMonth(DateOnly reportMonth)
    {
        var transportLoanDebt = Data.TransportLoans
            .Sum(loan => GetLoanDebtForMonth(loan.LoanStartDate, loan.StartingRemainingDebt, loan.Id, ExpenseCategory.Transport, reportMonth));

        var creditDebt = Data.Credits
            .Sum(credit => GetCreditDebtForMonth(credit, reportMonth));

        return transportLoanDebt + creditDebt;
    }

    private decimal GetSavingsForMonth(DateOnly reportMonth)
    {
        return Data.SavingsAccounts.Sum(account => GetSavingsBalanceForMonth(account.Id, reportMonth));
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

        foreach (var account in Data.SavingsAccounts)
        {
            RefreshSavingsAccountBalance(account.Id);
        }

        await PersistAndNotifyAsync();
    }

    private bool TryGetSavingsCalculationRange(out DateOnly startDate, out DateOnly endDate)
    {
        var startCandidates = Data.SavingsAccounts
            .Select(account => account.CreatedDate)
            .Concat(Data.SavingsReturnPeriods.Select(period => period.StartDate))
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
    {
        var account = GetSavingsAccount(accountId);
        var normalizedMonth = NormalizeMonth(month);
        if (NormalizeMonth(account.CreatedDate) > normalizedMonth)
        {
            return 0m;
        }

        if (NormalizeMonth(account.CreatedDate) == normalizedMonth)
        {
            return account.OpeningBalance;
        }

        var manualChanges = GetSavingsManualNetChangeUntil(accountId, normalizedMonth.AddMonths(-1));
        var generatedReturns = Data.SavingsGeneratedReturns
            .Where(item => item.SavingsAccountId == accountId && NormalizeMonth(new DateOnly(item.Year, item.Month, 1)) < normalizedMonth)
            .Sum(item => item.Amount);

        return Math.Max(0m, account.OpeningBalance + manualChanges + generatedReturns);
    }

    private decimal GetSavingsBalanceBeforeOrAtMonth(Guid accountId, DateOnly month)
    {
        var account = GetSavingsAccount(accountId);
        var normalizedMonth = NormalizeMonth(month);
        if (NormalizeMonth(account.CreatedDate) > normalizedMonth)
        {
            return 0m;
        }

        var manualChanges = GetSavingsManualNetChangeUntil(accountId, normalizedMonth);
        var generatedReturns = Data.SavingsGeneratedReturns
            .Where(item => item.SavingsAccountId == accountId && NormalizeMonth(new DateOnly(item.Year, item.Month, 1)) <= normalizedMonth)
            .Sum(item => item.Amount);

        return Math.Max(0m, account.OpeningBalance + manualChanges + generatedReturns);
    }

    private decimal GetSavingsBalanceForMonth(Guid accountId, DateOnly month) =>
        GetSavingsBalanceBeforeOrAtMonth(accountId, month);

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

        return deposits - withdrawals;
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

        return deposits - withdrawals;
    }

    private void RefreshSavingsAccountBalance(Guid accountId)
    {
        var account = Data.SavingsAccounts.FirstOrDefault(item => item.Id == accountId);
        if (account is null)
        {
            return;
        }

        account.CurrentBalance = GetSavingsBalanceBeforeOrAtMonth(account.Id, NormalizeMonth(DateOnly.FromDateTime(DateTime.Today)));
    }

    private decimal GetLoanDebtForMonth(DateOnly loanStartDate, decimal startingRemainingDebt, Guid loanId, ExpenseCategory category, DateOnly reportMonth)
    {
        var normalizedReportMonth = NormalizeMonth(reportMonth);
        if (NormalizeMonth(loanStartDate) > normalizedReportMonth)
        {
            return 0m;
        }

        var amortizedAmount = Data.ExpenseRecords
            .Where(expense =>
                expense.LoanId == loanId &&
                expense.Category == category &&
                expense.LoanAmortizationPlanId.HasValue &&
                NormalizeMonth(new DateOnly(expense.Year, expense.Month, 1)) <= normalizedReportMonth)
            .Sum(expense => expense.Amount);

        return Math.Max(0m, startingRemainingDebt - amortizedAmount);
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
