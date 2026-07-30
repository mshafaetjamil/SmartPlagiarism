# Smart Plagiarism Detection System — Claude Code Build Prompts

This document contains everything you need to build the project with Claude Code:

1. A `CLAUDE.md` file (put this in your repo root — Claude Code reads it automatically every session)
2. A kickoff prompt (Phase 0)
3. Phase-by-phase prompts (Phases 1–10)

**Architecture improvements over the original plan** (already baked into these prompts):

- **4 projects instead of 7.** Web, Core, Infrastructure, PlagiarismEngine (+ Tests). Seven projects is ceremony without benefit at this scale; four still demonstrates Clean Architecture and is far easier to navigate and defend.
- **Background processing for analysis.** Plagiarism analysis on large PDFs with OCR can take minutes. Running it inside an HTTP request will time out. These prompts use a queued `BackgroundService` with a status field (`Queued → Processing → Completed/Failed`) that the UI polls.
- **Extract text once, at upload time** — not at analysis time. Extracted text is stored per document, so comparing a new submission against 500 old ones doesn't re-parse 500 files.
- **SHA-256 content hashing** on upload for instant exact-duplicate detection and deduplicated file storage.
- **N-gram fingerprint pre-filtering** (winnowing-style) so the engine shortlists candidate documents before doing expensive pairwise comparison. This is what makes the system scale past a toy demo — and it's a great talking point in your defense.
- **REST API as a versioned area of the Web project** (`/api/v1/...`) instead of a separate project. Same integration story for the university portal, less plumbing.

---

## PART 1 — CLAUDE.md (save this file as `CLAUDE.md` in your repository root)

```markdown
# Smart Plagiarism Detection System

Final-year CSE project: a plagiarism detection system for academic reports (PDF/DOCX)
and presentations (PPTX/PDF), with Student / Teacher / Admin roles, an independent
plagiarism engine, and REST APIs for future university-portal integration.

## Tech stack

- ASP.NET Core MVC (.NET 8 LTS)
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
- Add migration: `dotnet ef migrations add <Name> -p src/SmartPlagiarism.Infrastructure -s src/SmartPlagiarism.Web`
- Update DB: `dotnet ef database update -p src/SmartPlagiarism.Infrastructure -s src/SmartPlagiarism.Web`

## Seed data (created on first run in Development)

- Roles: Admin, Teacher, Student
- Users: admin@uni.edu / Admin#123, teacher@uni.edu / Teacher#123,
  student@uni.edu / Student#123
- One department, one course, one semester for demo purposes
```

---

## PART 2 — Phase 0: Kickoff prompt

> If you are starting from your existing `WebApplication1` codebase, use Option A.
> If you'd rather start clean (recommended given how little is in the current
> project), use Option B. Either way, paste the chosen prompt as your first
> message in Claude Code, in the repo directory containing `CLAUDE.md`.

### Option A — evolve the existing project

```
Read CLAUDE.md first. This repository contains my existing ASP.NET Core MVC
project (WebApplication1). Before writing any code:

1. Read the entire existing codebase and produce a short written assessment:
   what exists, what is reusable, what conflicts with the target architecture
   in CLAUDE.md.
2. Propose a migration plan that transforms this into the 4-project solution
   described in CLAUDE.md, preserving anything genuinely reusable (layout,
   views, static assets, working config).
3. Delete .vs/, bin/, obj/ and add a proper .gitignore for .NET.

Then execute the migration: create the solution and 4 projects + tests project,
move reusable code into SmartPlagiarism.Web, wire project references in the
correct dependency direction, and confirm `dotnet build` passes.

Do NOT implement any features yet. Stop after the solution builds and give me
the assessment + a summary of the new structure.
```

### Option B — clean start (recommended)

```
Read CLAUDE.md first. Create the solution skeleton exactly as specified there:

- SmartPlagiarism.sln with the 4 src projects and the tests project
- Correct project references (Web → Core ← Infrastructure; Engine standalone)
- .gitignore for .NET, .editorconfig with standard Microsoft C# conventions
- In Web: MVC wired up, Bootstrap 5 layout with a clean navbar (brand:
  "SmartPlagiarism"), a landing page, and an empty /api/v1 area
- In Infrastructure: AppDbContext (empty for now), Identity registered with
  the three roles, SQL Server connection string in appsettings.Development.json
  using LocalDB
- A DbSeeder that creates roles + the three demo users from CLAUDE.md on
  startup in Development
- One placeholder xUnit test that passes

Do NOT implement any domain entities or features yet. Verify `dotnet build`
and `dotnet test` pass, then stop and summarize.
```

---

## PART 3 — Phase prompts

Paste these one at a time. Let each phase finish, review the summary, run the
app yourself, then move on. If something is wrong, fix it inside that phase
before continuing.

### Phase 1 — Domain model & database

```
Phase 1: Domain model and persistence. Follow CLAUDE.md.

In SmartPlagiarism.Core, create the entities with proper relationships:

- Department, Course (belongs to Department), Semester
- StudentProfile / TeacherProfile (linked 1:1 to Identity user, with
  StudentId number, department, etc.)
- Submission: Id, Student, Course, Semester, Title, Type (Report|Presentation),
  SubmittedAtUtc, AnalysisStatus (Queued|Processing|Completed|Failed),
  Status (PendingReview|Approved|Rejected), TeacherComment
- UploadedFile: Submission FK, OriginalFileName, ContentType, SizeBytes,
  Sha256Hash, StoragePath
- ExtractedText: UploadedFile FK, FullText, WordCount, ExtractionMethod
  (Native|Ocr|Mixed), ExtractedAtUtc
- DocumentFingerprint: UploadedFile FK, and a compact representation of
  winnowing fingerprints (store as varbinary or a child table — your choice,
  justify it)
- PlagiarismResult: Submission FK, OverallSimilarityPercent, AnalyzedAtUtc,
  EngineVersion
- MatchedDocument: PlagiarismResult FK, matched UploadedFile FK (nullable for
  future internet sources), SimilarityPercent, MatchedFragmentsJson
- AuditLog: UserId, Action, EntityType, EntityId, TimestampUtc, Details

In Infrastructure: fluent configurations (keys, indexes — include an index on
Sha256Hash and on Submission.AnalysisStatus), the DbContext, a generic
repository ONLY if it adds value over DbContext injection into services
(state your choice and reason), the initial migration, and extend the seeder
with 1 department, 2 courses, 1 semester.

Add unit-testable value: put AnalysisStatus/SubmissionStatus transitions in a
small domain method (e.g. Submission.MarkProcessing()) and test them.

Verify build + tests + `dotnet ef database update` works. Stop and summarize.
```

### Phase 2 — Authentication, roles & dashboards

```
Phase 2: Authentication and role-based shell. Follow CLAUDE.md.

- Login/logout/access-denied pages styled with the Bootstrap layout.
  Registration is Admin-only (admins create users) — no public signup.
- Role-based authorization policies: "AdminOnly", "TeacherOnly", "StudentOnly".
- Post-login redirect to a role-specific dashboard:
  - Student dashboard: my submissions count, latest results (placeholder data
    sources are NOT allowed — query real tables, they'll just be empty)
  - Teacher dashboard: pending reviews count, recent submissions, average
    similarity across analyzed submissions
  - Admin dashboard: user counts by role, total submissions, system stats
- Shared _Layout shows role-appropriate nav links only.
- Log login/logout to AuditLog via an IAuditService in Core.

Verify build, log in as each seeded user, confirm each dashboard renders.
Stop and summarize.
```

### Phase 3 — Secure file upload & student module

```
Phase 3: Student module with secure uploads. Follow CLAUDE.md.

Student can:
- Create a submission (title, course, semester, type) and upload 1..N files
  (PDF, DOCX, PPTX only)
- View submission history with status badges (AnalysisStatus + review Status)
- View a submission's detail page (results section shows "Analysis pending"
  until Phase 6 fills it in)

File handling (in an IFileStorageService in Core, implemented in
Infrastructure):
- Validate: extension whitelist, magic-byte check, 25 MB limit, reject
  zero-byte files
- Compute SHA-256; store at {storageRoot}/{first2charsOfHash}/{hash}{ext},
  outside wwwroot; if hash already exists, reuse the stored file
  (deduplication) but still create a new UploadedFile row
- Never trust or use the client filename for storage; keep it only as
  OriginalFileName in the DB
- Set AnalysisStatus = Queued on creation

Add unit tests for the validation logic (fake magic bytes, oversize, wrong
extension). Verify build + tests, upload a real PDF and DOCX manually.
Stop and summarize.
```

### Phase 4 — Text extraction pipeline (Engine, part 1)

```
Phase 4: Text extraction in SmartPlagiarism.Engine. Follow CLAUDE.md —
remember the Engine references no other project and no ASP.NET/EF packages.

In the Engine:
- ITextExtractor with implementations: PdfTextExtractor (PdfPig),
  DocxTextExtractor (Open XML SDK), PptxTextExtractor (Open XML SDK), and an
  ExtractorFactory selecting by extension
- OCR fallback: if a PDF page yields fewer than ~20 characters of native text,
  rasterize that page and run Tesseract on it; record ExtractionMethod
  accordingly (Native/Ocr/Mixed). Wrap Tesseract behind IOcrService so the
  engine still works (minus OCR) if tessdata is missing — degrade gracefully
  with a warning, never crash.
- Text normalization pipeline: lowercase, unicode normalization, collapse
  whitespace, strip punctuation for analysis (keep original text separately
  for display/highlighting)
- Return a rich result: full text, per-page/per-slide segments, word count,
  method used, warnings

In Core: an IExtractionService that takes an UploadedFile, calls the engine,
and persists ExtractedText. Not wired to any trigger yet — that's Phase 6.

Include tessdata setup instructions in README (eng traineddata download path).
Unit tests: extraction against 3 tiny fixture files you generate in the test
project (a small PDF, DOCX, PPTX created programmatically). Verify build +
tests. Stop and summarize.
```

### Phase 5 — Similarity engine (Engine, part 2)

```
Phase 5: Similarity algorithms in SmartPlagiarism.Engine. Follow CLAUDE.md.

Implement, each behind ISimilarityAlgorithm (name, Compute(docA, docB)):
- TF-IDF + cosine similarity (corpus-aware IDF passed in)
- Jaccard similarity over word sets
- N-gram overlap (word 3-grams)
- A combined weighted score (document the default weights)

Candidate pre-filtering (this is the scalability feature — implement it well):
- Winnowing fingerprints: word 5-grams → 64-bit hashes → winnowing window
  of 4 → fingerprint set per document
- IFingerprintIndex: given a new document's fingerprints, return candidate
  documents sharing ≥ N fingerprints. Engine defines the interface; the
  Infrastructure implementation queries DocumentFingerprint rows.
- The full pipeline: exact-hash check (SHA-256 equal → 100%) → fingerprint
  shortlist → detailed pairwise algorithms on shortlist only

Match localization for highlighting:
- For each candidate pair, find matching sentence/fragment spans (use the
  n-gram matches to locate character offsets in BOTH documents)
- Output MatchedFragment { SourceStart, SourceEnd, TargetStart, TargetEnd,
  Similarity } serializable to MatchedFragmentsJson

Unit tests: identical docs → ~100%; disjoint docs → ~0%; a doc with one
copied paragraph → intermediate score with correct fragment offsets.
Verify build + tests. Stop and summarize.
```

### Phase 6 — Background analysis pipeline

```
Phase 6: Background analysis orchestration. Follow CLAUDE.md.

- IAnalysisQueue (Channel<Guid> of submission IDs) + AnalysisWorker
  (BackgroundService) in Web/Infrastructure as appropriate
- Worker flow per submission: set Processing → extract text for each file
  (Phase 4) → compute + store fingerprints → get candidates from the index →
  run similarity pipeline (Phase 5) against candidates' stored ExtractedText →
  persist PlagiarismResult + MatchedDocuments → set Completed. Any exception:
  set Failed, log details, never crash the worker loop.
- Enqueue on submission creation (Phase 3 hook) and expose a Teacher action
  "Re-run analysis"
- Each unit of work uses its own DI scope (scoped DbContext)
- Student/Teacher submission pages: status auto-refresh (simple JS polling of
  a lightweight status endpoint every 3s while Queued/Processing)

Verify end-to-end manually: upload two documents where the second copies a
paragraph from the first; confirm the second gets a nonzero similarity and a
MatchedDocument row pointing at the first. Stop and summarize.
```

### Phase 7 — Teacher module & match viewer

```
Phase 7: Teacher module and the highlighted match viewer. Follow CLAUDE.md.

Teacher can:
- List all submissions with filters (course, semester, status, similarity
  range) and sorting
- Open a submission: metadata, files, overall similarity, matched documents
  with per-document similarity
- Side-by-side match viewer: submitted text on the left, matched source on
  the right, matched fragments highlighted (use MatchedFragmentsJson offsets;
  clicking a match in one pane scrolls the other pane to it)
- Approve / Reject with a required comment on reject; both audited
- Trigger re-analysis

Color-code overall similarity: <20% green, 20–50% yellow, >50% red.
Keep highlighting logic in a small JS module + a server-side service that
converts fragments to render-safe segments (HTML-encode everything — no raw
text injection). Verify manually with the Phase 6 test documents.
Stop and summarize.
```

### Phase 8 — PDF reports & admin module

```
Phase 8: Reporting and admin. Follow CLAUDE.md.

Reports (IReportGenerator in Core, QuestPDF implementation in Infrastructure):
- Plagiarism report PDF: university-style header, student info, submission
  info, overall similarity with color band, matched documents table, top
  matched fragments (source vs submitted excerpts), generation timestamp,
  teacher decision if present
- Downloadable by the student (own submissions) and teacher (any)

Admin module:
- CRUD: users (create with role, deactivate), departments, courses, semesters
- System statistics page: submissions over time, average similarity,
  extraction method breakdown, failed analyses list with retry button
- Audit log viewer with filters and paging

Authorization: verify a student can NEVER access another student's submission
or report (add an authorization handler + a test for the service-level check).
Verify build + tests. Stop and summarize.
```

### Phase 9 — REST API for portal integration

```
Phase 9: Versioned REST API. Follow CLAUDE.md.

Under /api/v1 in the Web project (controllers or minimal APIs — pick one and
be consistent):
- POST /api/v1/submissions        multipart upload → creates submission,
                                  queues analysis, returns 202 + id
- GET  /api/v1/submissions/{id}   status + result summary
- GET  /api/v1/submissions/{id}/report   the PDF
- GET  /api/v1/health

Auth: API-key scheme (keys managed by Admin in the UI, stored hashed).
This keeps portal integration simple without full OAuth. Rate-limit the
upload endpoint. Add Swagger/OpenAPI in Development only.

Reuse the SAME Core services as the MVC UI — zero duplicated business logic.
Add integration tests with WebApplicationFactory for the happy path and for
a rejected invalid file. Verify build + tests. Stop and summarize.
```

### Phase 10 — Hardening & polish

```
Phase 10: Final hardening pass. Follow CLAUDE.md.

- Global exception handling middleware + friendly error pages
- Serilog structured logging (console + rolling file)
- Review every query for missing AsNoTracking/Include and N+1 issues
- Pagination on every list page
- CSRF verified on all POSTs, security headers middleware, HTTPS redirect
- Input validation review (FluentValidation or data annotations —
  consistently, everywhere)
- README.md: project overview, architecture diagram (mermaid), setup steps,
  demo credentials, screenshots section placeholder
- A short ARCHITECTURE.md explaining layer responsibilities and the analysis
  pipeline — written so I can use it in my defense presentation

Run the full flow as all three roles. Fix anything broken. Produce a final
summary of the entire system: what exists, known limitations, and 5 ideas
for "future work" I can mention in my defense.
```

---

## PART 4 — Tips for running this with Claude Code

- **One phase per session works best.** Start each session with: `Continue with Phase N from claude-code-prompts-plagiarism-system.md` (keep this file in the repo too).
- **Review before proceeding.** After each phase, actually run the app and click through. Claude Code's summary tells you what to verify.
- **Use git.** Commit after every green phase: `git commit -m "Phase 3: student module + secure uploads"`. If a phase goes sideways, you can reset instead of untangling.
- **If context gets long**, use `/compact` between phases, or just start a fresh session — `CLAUDE.md` carries the conventions forward.
- **Scope control.** If Claude proposes something beyond the phase (e.g., adding Docker in Phase 2), reply: "Out of scope for this phase — note it for later and continue."
