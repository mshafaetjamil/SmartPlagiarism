# SmartPlagiarism

A plagiarism detection system for academic reports (PDF/DOCX) and presentations
(PPTX/PDF), with Student / Teacher / Admin roles, a standalone similarity engine,
and a versioned REST API for university-portal integration.

See [CLAUDE.md](CLAUDE.md) for the architecture rules this project is built to.

## Solution layout

| Project | Purpose |
| --- | --- |
| `src/SmartPlagiarism.Web` | ASP.NET Core MVC UI and the `/api/v1` REST surface |
| `src/SmartPlagiarism.Core` | Entities, enums, DTOs, service interfaces, business logic |
| `src/SmartPlagiarism.Infrastructure` | EF Core `AppDbContext`, migrations, Identity setup, seeding |
| `src/SmartPlagiarism.Engine` | Standalone plagiarism engine (no ASP.NET, no EF Core, no project references) |
| `tests/SmartPlagiarism.Tests` | xUnit tests for the engine and services |

## Prerequisites

- .NET 10 SDK
- SQL Server (see below)

## Database

`appsettings.Development.json` points at SQL Server on `localhost,1433`. On
macOS and Linux, run it in Docker:

```bash
docker run -d --name smartplagiarism-sql \
  -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD='SmartPlag#2026' \
  -p 1433:1433 mcr.microsoft.com/mssql/server:2022-latest
```

On Windows you can use LocalDB instead by swapping the connection string for:

```
Server=(localdb)\MSSQLLocalDB;Database=SmartPlagiarism;Trusted_Connection=True;MultipleActiveResultSets=true
```

The app starts even when the database is unreachable — seeding is skipped with a
warning so the UI can be worked on without SQL Server running.

## Running

```bash
dotnet build
dotnet test
dotnet run --project src/SmartPlagiarism.Web
```

In Development the app applies migrations and seeds three demo accounts on
startup:

| Role | Email | Password |
| --- | --- | --- |
| Admin | admin@uni.edu | `Admin#123` |
| Teacher | teacher@uni.edu | `Teacher#123` |
| Student | student@uni.edu | `Student#123` |

## Entity Framework

```bash
dotnet ef migrations add <Name> -p src/SmartPlagiarism.Infrastructure -s src/SmartPlagiarism.Web
dotnet ef database update -p src/SmartPlagiarism.Infrastructure -s src/SmartPlagiarism.Web
```
