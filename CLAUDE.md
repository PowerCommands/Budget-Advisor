# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run Commands

```bash
# Restore dependencies
dotnet restore BudgetAdvisor.slnx

# Build solution
dotnet build BudgetAdvisor.slnx

# Run locally (dev server with hot reload)
dotnet run --project src/BudgetAdvisor.App/BudgetAdvisor.App.csproj

# Docker build and run
docker build -t budget-advisor:local .
docker run --rm -p 8080:8080 budget-advisor:local
# Access: http://localhost:8080
```

There are no automated tests in this project — testing is done manually using the sample data in `sample-data/`.

## Architecture Overview

**Budget Advisor** is a Blazor WebAssembly app (.NET 10) that runs entirely in the browser. All data is stored as JSON in browser LocalStorage — there is no backend or database.

### Project Layout

```
src/
  BudgetAdvisor.Domain/    — Data models (47 classes) and enums (14 types)
  BudgetAdvisor.Services/  — Business logic, state management, sync
  BudgetAdvisor.App/       — Blazor WASM UI, pages, components, importers
```

### Central State: ApplicationState.cs

[src/BudgetAdvisor.Services/ApplicationState.cs](src/BudgetAdvisor.Services/ApplicationState.cs) (~4,951 lines) is the heart of the application. It is a singleton service that:
- Holds the entire in-memory application state
- Performs all budget calculations (totals, loan schedules, savings returns)
- Mutates data and notifies the UI via Blazor state change events
- Persists all changes to LocalStorage via `LocalStorageService`

All UI pages inject and depend on `ApplicationState`. State mutations happen through async methods on this service, not directly on the data models.

### Data Schema: ApplicationData.cs

[src/BudgetAdvisor.Domain/Models/ApplicationData.cs](src/BudgetAdvisor.Domain/Models/ApplicationData.cs) is the root JSON document stored under the LocalStorage key `"budget-advisor.application-data"`. It contains flat lists of all domain entities (members, income/expense entries, loans, savings, credits, subscriptions, etc.). All entities use GUIDs for identity.

### UI Layer

Pages are in [src/BudgetAdvisor.App/Pages/](src/BudgetAdvisor.App/Pages/) and map to named routes. Reusable dialogs and widgets are in [src/BudgetAdvisor.App/Shared/](src/BudgetAdvisor.App/Shared/). MudBlazor 8.x is used for all UI components (dialogs, tables, forms, icons).

### Bank Transaction Import

[src/BudgetAdvisor.App/Imports/](src/BudgetAdvisor.App/Imports/) contains importers for CSV/XLSX formats from Swedish banks: Swedbank, Nordea, Skandiabanken, SEB. Each implements `ITransactionImporter`. The `TransactionImportDetector` auto-detects the bank format from the file.

### Localization

All UI strings are externalized in [src/BudgetAdvisor.App/wwwroot/localization/en.json](src/BudgetAdvisor.App/wwwroot/localization/en.json) and `sv.json`. The `LocalizationService` is injected app-wide. Use existing localization keys rather than hardcoding strings; add new keys to both language files when adding UI text.

### Cloud Sync (Optional)

`DropboxSyncProvider` + `ManualSyncService` handle optional Dropbox backup. Configured via `wwwroot/appsettings.json` (Dropbox AppKey is already set). The OAuth callback is handled by a static page at `wwwroot/auth/dropbox/callback/`.

### Undo/Redo

`UndoService` maintains a stack of serialized `ApplicationData` snapshots. Before any destructive operation, `ApplicationState` pushes the current state onto this stack.

## Key Conventions

- **Currency**: Default SEK, configurable to USD/EUR/GBP. Always use `ApplicationState` for formatting — do not format currency directly.
- **Dates**: The app uses `DateOnly` throughout (not `DateTime`). See `DateOnlyExtensions` for helpers.
- **Recurring definitions vs. entries**: Subscriptions, housing costs, and transport costs are stored as *definitions* (rules) and expanded into concrete entries at calculation time by `ApplicationState` — do not store generated entries in `ApplicationData`.
- **Expense categories**: Defined in `ExpenseCategory` enum (`Food`, `Clothing`, `Housing`, `Transport`, `Credits`, `Savings`, `Assets`, `Other`).
