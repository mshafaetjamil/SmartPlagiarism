# Smart Plagiarism Detection System

Final-year CSE project: a plagiarism detection system for academic reports (PDF/DOCX)
and presentations (PPTX/PDF), with Student / Teacher / Admin roles, an independent
plagiarism engine, and REST APIs for future university-portal integration.

## Tech stack

- ASP.NET Core MVC (.NET 10 LTS)
- SQL Server + Entity Framework Core (code-first migrations)
- ASP.NET Core Identity (role-based: Admin, Teacher, Student)
- Razor Views + Bootstrap 5 (no SPA framework)
- PdfPig (PDF text extraction), Open XML SDK (DOCX/PPTX), Tesseract (OCR fallback)
- QuestPDF (report generation)
- xUnit + FluentAssertions for tests

## Solution structure

    SmartPlagiarism.sln
    src/
      SmartPlagiarism.Web/              MVC UI + REST API endpoints (/api/v1)
      SmartPlagiarism.Core/             Entities, enums, DTOs, service interfaces,
                                        application services (business logic)
      SmartPlagiarism.Infrastructure/   DbContext, migrations, repositories,
                                        Identity setup, file storage, email
      SmartPlagiarism.Engine/           Plagiarism engine class library.
                                        MUST have zero references to ASP.NET,
                                        EF Core, or any other project.
    tests/
      SmartPlagiarism.Tests/            Unit tests (engine + services)

Dependency direction: Web → Core ← Infrastructure; Engine referenced by Core
through interfaces only. Engine references nothing but the BCL and its own
NuGet packages (Tesseract, PdfPig, Open XML SDK).

## Architecture rules

- Controllers are thin: bind → call service → return view/result. No business
  logic, no DbContext, no file IO in controllers.
- All business logic lives in Core services behind interfaces, registered via DI.
- All IO is async/await. No .Result, no .Wait().
- Use ViewModels for views and DTOs across layers. Never pass EF entities to views.
- Plagiarism analysis runs in a background queue (BackgroundService), never
  inside an HTTP request. Submissions have an AnalysisStatus the UI polls.
- Uploaded files are stored outside wwwroot, named by SHA-256 hash, with the
  original filename kept in the database. Validate extension + magic bytes +
  size limit (25 MB) on every upload.
- EF Core: no lazy loading; use explicit Include; AsNoTracking for read queries.
- Every schema change gets a migration in the same commit.

## Workflow rules for Claude Code

- Work on ONE phase / one logical task at a time, then stop and summarize:
  what changed, why, files touched, how to verify.
- After every change: `dotnet build` must pass. Run `dotnet test` when tests exist.
- Never delete or rewrite working code without stating the reason first.
- Prefer small, reviewable diffs over large rewrites.
- No placeholder logic, no TODO stubs left behind in committed code, no
  copy-pasted duplicate code.
- When a design decision has trade-offs, state the options briefly and pick one;
  don't silently choose.

## Commands

- Build: `dotnet build`
- Run: `dotnet run --project src/SmartPlagiarism.Web`
- Test: `dotnet test`
- Start the dev database (macOS/Linux — LocalDB is Windows-only):
  `docker run -d --name smartplagiarism-sql -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD='SmartPlag#2026' -p 1433:1433 mcr.microsoft.com/mssql/server:2022-latest`
- Add migration: `dotnet ef migrations add <Name> -p src/SmartPlagiarism.Infrastructure -s src/SmartPlagiarism.Web`
- Update DB: `dotnet ef database update -p src/SmartPlagiarism.Infrastructure -s src/SmartPlagiarism.Web`

## Seed data (created on first run in Development)

- Roles: Admin, Teacher, Student
- Users: admin@uni.edu / Admin#123, teacher@uni.edu / Teacher#123,
  student@uni.edu / Student#123
- One department, one course, one semester for demo purposes