# SmartPlagiarism

A plagiarism detection system for academic reports (PDF/DOCX) and presentations
(PPTX/PDF), with Student / Teacher / Admin roles, a standalone similarity engine,
and a versioned REST API for university-portal integration.

**New here?** [SETUP.md](SETUP.md) has step-by-step instructions for Windows,
macOS and Linux, from installing prerequisites through to signing in.

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

## OCR (optional)

Text extraction reads the text layer of PDFs, DOCX and PPTX files directly. A PDF
page carrying fewer than ~20 characters — a scan, in other words — is handed to
Tesseract instead.

**OCR is entirely optional.** Without it the app still extracts text from every
document that has a text layer; scanned pages simply come back empty with a
warning naming the reason. Nothing crashes and no analysis fails.

To enable it, put the English language data where `Ocr:TessDataPath` points
(`src/SmartPlagiarism.Web/App_Data/tessdata` by default):

```bash
mkdir -p src/SmartPlagiarism.Web/App_Data/tessdata
curl -L -o src/SmartPlagiarism.Web/App_Data/tessdata/eng.traineddata \
  https://github.com/tesseract-ocr/tessdata_fast/raw/main/eng.traineddata
```

`tessdata_fast` is the smaller, quicker model and is the right default here. Swap
`tessdata_fast` for `tessdata_best` if you would rather have accuracy than speed,
or `tessdata` for the legacy models.

Configuration lives under `Ocr` in `appsettings.json`:

| Setting | Meaning |
| --- | --- |
| `Enabled` | Set `false` to skip OCR even when the language data is present |
| `TessDataPath` | Directory holding the `.traineddata` files, relative to the content root |
| `Language` | Language code; must match a `{Language}.traineddata` file |

**Native library note.** The `Tesseract` NuGet package ships native binaries for
Windows and Linux only — there is no macOS build. On a Mac the engine reports OCR
as unavailable and carries on; the reason is logged at startup and recorded
against each affected page. Extraction of PDFs, DOCX and PPTX with real text
layers is unaffected.

## Entity Framework

```bash
dotnet ef migrations add <Name> -p src/SmartPlagiarism.Infrastructure -s src/SmartPlagiarism.Web
dotnet ef database update -p src/SmartPlagiarism.Infrastructure -s src/SmartPlagiarism.Web
```
