# Budget Advisor

Budget Advisor is a standalone Blazor WebAssembly application for household budgeting and financial planning. Data and localization resources are stored in the browser as JSON via LocalStorage.

The application is built to support both quick budgeting and more detailed planning over time. You can start from where you are today without any historical data at all, or gradually build up 12 months of history using imported bank transactions, recurring costs, loans, savings, and manually entered adjustments.

## How To Work With The App

There is no single correct way to get started. A few common approaches are:

### 1. Start From Today

If you want to get going quickly, you can begin with the current month and only enter the data you know right now:

- household members
- income
- fixed housing costs
- transport costs
- debts and savings

This is often enough to get an initial budget and a first view of balance, savings, and loan levels.

### 2. Build Up History From Bank Data

If you want more accurate budget averages, import transactions exported from your bank. The app has support for working your way back through older months so you can build up around 12 months of data.

This makes the budget view more useful because several parts of the application rely on completed months to calculate averages and monthly patterns.

### 3. Build History Manually

If you do not want to use bank data, you can still create a useful budget by entering reasonable estimates:

- add incomes manually
- add expenses manually
- create recurring expenses
- add loans and interest periods

This is a good option if you want a fast planning tool rather than exact bookkeeping.

### 4. Use Recurring Data To Fill Gaps

Recurring expenses and loan functions can be used both forward and backward in time. That means you can use them to:

- create future planning data
- backfill known monthly costs
- estimate historical interest and amortization patterns

This is especially useful for housing costs, transport costs, subscriptions, and loans with known payment periods.

## Main Workflow

A practical way to build up your budget is:

1. Add household members.
2. Import transactions or enter a few months manually.
3. Categorize income and expenses.
4. Add recurring expenses where appropriate.
5. Add housing loans, transport loans, leasing, credits, and savings.
6. Review Dashboard and Analysis to spot patterns, transfers, Swish activity, and outliers.
7. Adjust categories and member assignments until the budget feels correct.

## Sample Data: Alice & Bob

Two backup files are available for testing Budget Advisor with the fictional household Alice & Bob. All related files are located in the `sample-data` folder in the repository.

Option 1: Test the import flow

- Restore the backup file `budget-advisor-backup-sample_before_import.zip`.
- Then import all `*.csv` files from the `sample-data` folder.
- This will create three months of transaction history for both Alice and Bob.

Option 2: Skip import and go directly to analysis/testing

- Restore the backup file `budget-advisor-backup-not-corrected.zip`.
- The data in this backup intentionally contains a few things that need to be adjusted.
- This is by design, so you can try the Analysis features in the application.
- In particular, you can test removing transfer posts.
- You can also inspect the Swish posts and assign names to them if desired.
- There are three mobile numbers in the sample data: one for Alice, one for Bob, and one for their child.

## Main Tabs

### Dashboard

Dashboard gives you a compact overview of your current financial situation.

It includes:

- budget summary
- key metrics
- debt and savings indicators
- widget settings for controlling what is shown

This is the best place to check whether the overall model looks reasonable.

### Analys

Analysis helps you find and clean data.

Current analysis views include:

- Transfers
  Matches income and expense records that likely represent transfers by looking for the same date and exact amount.
- Swish
  Identifies transactions that contain `Swish` and a mobile number, groups them by number, and lets you name and clean them up.
- Query
  Lets you build a dynamic search across incomes and expenses using text filters, amount ranges, and date ranges.

### Inkomster

This area is used for household members and income registration.

You can:

- manage household members
- assign initials to members
- connect incomes to a specific member
- filter and review income history

This is useful if you want to understand who in the household contributes what.

### Boende

Housing contains costs and loans related to your home.

You can:

- register housing expenses
- create recurring housing expenses
- add housing loans
- define interest and amortization periods
- backfill loan-related costs over time

This makes it possible to model both current and historical housing costs.

### Transport

Transport works similarly to Housing, but for vehicles and transport-related costs.

You can:

- register transport expenses
- create recurring transport expenses
- manage transport loans
- manage leasing
- manage vehicles

### Utgifter

Expenses is the general expense area for costs that do not belong specifically to housing or transport.

You can:

- register expenses manually
- create recurring expenses
- review upcoming expenses

### Krediter

Credits is used for credit-related debt and monthly debt tracking. It contributes to the overall view of liabilities and affects relevant calculations in Dashboard.

### Sparande

Savings is used to track savings balances and development over time. These values are used in budget and key metric calculations.

### Import

Import is used to bring transaction data into the application from bank exports.

You can:

- preview imported rows
- assign a household member to the import
- import both income and expense rows
- update member assignment afterwards from the import log

Importing is often the fastest way to build a good data foundation.

### Inställningar

Settings contains application-level configuration such as language and other utility functions.

## Recurring Costs And Loans

Recurring costs are important in the app because they let you build realistic data without entering every row manually.

Typical use cases:

- rent
- broadband
- insurance
- electricity
- subscriptions

Loan support is intended to help you model both current status and month-by-month development:

- remaining debt
- current amortization
- interest periods
- historical and future loan expense generation

## Budget Philosophy

Budget Advisor is meant to support planning rather than strict accounting.

That means:

- exact imported data works well
- estimated values also work well
- you can mix real data and assumptions
- you can refine the model over time instead of doing everything at once

The goal is that you should be able to get value from the app early, and then improve the quality of the budget as more data becomes available.

## Build

Local `.NET 10` is required for a host build:

```bash
dotnet restore BudgetAdvisor.slnx
dotnet build BudgetAdvisor.slnx
```

If `.NET 10` is not installed locally, use Docker.

## Run

```bash
dotnet run --project src/BudgetAdvisor.App/BudgetAdvisor.App.csproj
```

## Docker

Build the image:

```bash
docker build -t budget-advisor:local .
```

Run the container:

```bash
docker run --rm -p 8080:8080 budget-advisor:local
```

Then open `http://localhost:8080`.
