# Budget Advisor

Budget Advisor is a standalone Blazor WebAssembly application for simple household budgeting. Data and localization resources are stored in the browser as JSON via LocalStorage.

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
