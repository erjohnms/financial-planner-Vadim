# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Financial Planner is a full-stack budgeting application built with .NET 10.0. It allows users to upload CSV transaction statements, view transactions, and manage their budget.

**Stack:**
- Backend: ASP.NET Core 10.0 (RESTful API)
- Frontend: Blazor Server-Side (C# web UI)
- Database: SQLite
- API Documentation: OpenAPI with Scalar UI

## Architecture

### Backend (SampleApp/BackEnd)

The backend is a minimal ASP.NET Core API with the following structure:

- **Program.cs**: Contains all API endpoints and business logic
  - `/users/login` (POST): User authentication/creation
  - `/transactions/upload` (POST): CSV file upload and parsing
  - `/transactions` (GET): Retrieve user transactions
  - `/transactions` (DELETE): Clear user transactions
  - `TransactionRepository`: SQLite data access layer, handles database initialization and CRUD operations
  - CSV parsing: Flexible delimiter detection (comma/tab), flexible column mapping (date/description/amount/category)
  - Database: `budgeting.db` (SQLite, auto-created on startup)

**Key Details:**
- All endpoints are parameter-validated (userName is required, CSV structure validated)
- CSV parser supports multiple date formats and flexible column naming
- Transactions tracked with source filename and upload timestamp
- User/transaction relationship maintained via foreign key

### Frontend (SampleApp/FrontEnd)

Blazor Server-Side application with the following structure:

- **Program.cs**: Configures Blazor, registers BudgetClient HTTP client (requires BACKEND_URL environment variable)
- **Pages/FetchData.razor**: Main UI with login, CSV upload, transaction list, search
- **Data/BudgetClient.cs**: HTTP client for backend API communication
- **Data/BudgetTransaction.cs**: Data model for transactions
- **Shared/MainLayout.razor**: Page layout and styling

**Key Details:**
- Single-page app (/) with conditional rendering based on login state
- File upload: supports .csv files up to 10MB
- Search: filters transactions by date, description, category, or amount
- Error/success messages displayed to user

## Development Setup

### Environment Variables

**Frontend** requires:
- `BACKEND_URL`: Base URL for backend API (e.g., `http://localhost:8080` for local development)

Set via `launchSettings.json` or environment.

### Build & Run

**In VS Code (Recommended):**
- Click **Run and Debug** → **Run All** (launches both backend and frontend)
- Backend opens at `http://localhost:8080/scalar` (API docs)
- Frontend opens at `http://localhost:8081` (app UI)

**From Terminal:**
```bash
# Build
dotnet build SampleApp/SampleApp.sln

# Run individually
dotnet run --project SampleApp/BackEnd/BackEnd.csproj
dotnet run --project SampleApp/FrontEnd/FrontEnd.csproj

# Watch mode (auto-rebuild on file changes)
dotnet watch run --project SampleApp/BackEnd/BackEnd.csproj
dotnet watch run --project SampleApp/FrontEnd/FrontEnd.csproj
```

**Debug Configuration (.vscode/launch.json):**
- `BackEnd` config: Builds, runs, and opens Scalar API docs
- `FrontEnd` config: Builds, runs, and opens app
- `Run All` compound: Launches both simultaneously

### Testing

Currently no automated tests. Manual testing approach:
1. Start both backend and frontend
2. Sign in with a test name
3. Upload a CSV file (samples available in `samples/`)
4. Verify transactions display correctly
5. Test search functionality
6. Test delete transactions

## Key Files

| File | Purpose |
|------|---------|
| `SampleApp/BackEnd/Program.cs` | All API endpoints, CSV parsing, TransactionRepository |
| `SampleApp/FrontEnd/Pages/FetchData.razor` | Main UI component (login, upload, display) |
| `SampleApp/FrontEnd/Data/BudgetClient.cs` | HTTP client for backend API |
| `SampleApp/BackEnd/budgeting.db` | SQLite database (auto-created) |
| `.vscode/launch.json` | Debug configuration |
| `.vscode/tasks.json` | Build tasks |

## Development Patterns

### CSV Parsing (Backend)

The `ParseTransactions()` function in Program.cs handles:
- **Delimiter detection**: Automatically chooses comma or tab based on header line
- **Column mapping**: Flexible header matching (case-insensitive):
  - Date: "date", "transactiondate", "posteddate"
  - Description: "description", "desc"
  - Amount: "amount", "amt", "value"
  - Category: "category" (required)
- **Date normalization**: Parses multiple formats (DateOnly, DateTime) and normalizes to "yyyy-MM-dd"
- **Error handling**: Validates CSV structure, throws `InvalidDataException` with descriptive messages

### Database Access

`TransactionRepository` uses raw ADO.NET (SQLiteConnection) with:
- Transactions for multi-row inserts
- Parameterized queries (SQLite doesn't support stored procedures)
- Auto-migration: Adds `transaction_date` column if missing (for schema evolution)

### Frontend State Management

`FetchData.razor` maintains component state:
- `currentUser`: Current logged-in user
- `transactions`: List of all transactions
- `FilteredTransactions`: Computed property for search filtering
- `isBusy`: Loading state for async operations
- Error messages cleared on new operations

## Common Tasks

### Add a New API Endpoint

1. Add endpoint mapping in `Program.cs` with validation
2. Add corresponding method in `TransactionRepository` if database access needed
3. Add method in `BudgetClient.cs` (frontend) to call it
4. Update UI in `FetchData.razor` if needed
5. Test via Scalar API docs and frontend

### Modify CSV Parsing

Edit `ParseTransactions()` and related helper functions in `Program.cs`:
- `FindHeaderIndex()`: Add new column aliases
- `NormalizeDate()`: Add date format support
- `DetectDelimiter()`: Adjust delimiter detection logic

### Update Database Schema

Database schema is in `InitializeDatabase()` in `TransactionRepository`. For backward compatibility:
- Add new columns with defaults
- Include auto-migration logic (like `transaction_date` column addition)
- Never drop columns without migration

### Frontend UI Changes

Edit `FetchData.razor`:
- Component tree at the top with Razor syntax
- `@code` block at the bottom with C# logic and state

## Deployment Notes

- **Dev Container**: Uses .NET 10.0 image with ASP.NET Core runtime
- **Post-Create Command**: `dotnet new install Aspire.ProjectTemplates && cd ./SampleApp && dotnet restore`
- **Ports Forwarded**: 8080 (backend), 8081 (frontend)
- **Resource Requirements**: 8GB RAM, 4 CPUs (Codespaces default)

## CI/CD

GitHub Actions workflow (`.github/workflows/build.yml`):
- Builds both projects on PR to main
- Uses .NET 10.0
- Restores dependencies and builds in Release configuration
- No automated tests currently
