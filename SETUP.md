# Setup guide

How to get SmartPlagiarism running from a clean machine, on **Windows**, **macOS**
or **Linux**. Follow the section for your OS in Part 1, then Parts 2–5 are the
same everywhere.

The same clone works on all three. You can develop on one machine and continue on
another — see [Working across two machines](#9-working-across-two-machines).

---

## 0. Read this first

Two things are **per-machine and never synced**, by design:

| | Why |
| --- | --- |
| **The database** | Each machine runs its own SQL Server. Migrations are in Git, so the schema is identical; the *data* is not. |
| **`App_Data/uploads`** | Uploaded coursework is gitignored — it must never reach GitHub. |

So a submission created on your Mac will not exist on your Windows machine, and
its file will not be there either. Each machine seeds its own demo accounts and
demo course data on first run. This is normal and nothing to work around.

---

## 1. Prerequisites — install these *before* cloning

### All platforms

**.NET 10 SDK** — check with `dotnet --version` (expect `10.x`).

You also need **Git**, and **Docker** unless you are on Windows and prefer LocalDB.

### Windows

1. **.NET 10 SDK** — <https://dotnet.microsoft.com/download/dotnet/10.0>, or:
   ```powershell
   winget install Microsoft.DotNet.SDK.10
   ```
2. **Git** — `winget install Git.Git`
3. **A database**, pick one:
   - **SQL Server LocalDB** (lightest; no container). Comes with
     [SQL Server Express](https://www.microsoft.com/sql-server/sql-server-downloads) —
     choose the *Express* installer and tick LocalDB. Verify:
     ```powershell
     sqllocaldb info
     ```
   - **or Docker Desktop** — `winget install Docker.DockerDesktop`
4. *(Optional)* Visual Studio 2022/2026 or JetBrains Rider. `SmartPlagiarism.sln`
   opens directly. VS Code + the C# Dev Kit also works.

### macOS

1. **.NET 10 SDK** — <https://dotnet.microsoft.com/download/dotnet/10.0>, or:
   ```bash
   brew install --cask dotnet-sdk
   ```
2. **Git** — preinstalled, or `brew install git`
3. **Docker Desktop** — `brew install --cask docker`, then launch it once.
   LocalDB does **not** exist on macOS, so Docker is the only option.

### Linux

1. **.NET 10 SDK** — follow
   <https://learn.microsoft.com/dotnet/core/install/linux> for your distro. On
   Ubuntu 24.04+:
   ```bash
   sudo apt update && sudo apt install -y dotnet-sdk-10.0
   ```
2. **Git** — `sudo apt install -y git`
3. **Docker Engine** — <https://docs.docker.com/engine/install/>, then add
   yourself to the `docker` group so you do not need `sudo`:
   ```bash
   sudo usermod -aG docker $USER   # log out and back in
   ```

> **ARM Linux (aarch64):** the official `mssql/server` image is x86-64 only. Use
> `mcr.microsoft.com/azure-sql-edge:latest` instead — it speaks the same protocol
> and the connection string is unchanged. (On Apple Silicon the normal image runs
> fine under Docker Desktop's emulation.)

---

## 2. Clone

```bash
git clone https://github.com/mshafaetjamil/SmartPlagiarism.git
cd SmartPlagiarism
```

The repository has a `.gitattributes` that stores text as LF and checks it out as
LF everywhere, so no line-ending configuration is needed on Windows.

---

## 3. Start a database

### Docker — works on Windows, macOS and Linux

```bash
docker run -d --name smartplagiarism-sql \
  -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD='SmartPlag#2026' \
  -p 1433:1433 mcr.microsoft.com/mssql/server:2022-latest
```

PowerShell uses backticks for line continuation, so on Windows either put it on
one line or use `` ` `` instead of `\`.

This matches the connection string already in `appsettings.Development.json`, so
**nothing else to configure**. Check it is up:

```bash
docker ps
```

To stop and start it later: `docker stop smartplagiarism-sql` /
`docker start smartplagiarism-sql`.

### LocalDB — Windows only, optional

If you would rather not run a container, use LocalDB and point the app at it with
**user secrets**. User secrets live outside the repository and override
`appsettings.Development.json`, so you never edit a tracked file and never create
a merge conflict between machines:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=(localdb)\MSSQLLocalDB;Database=SmartPlagiarism;Trusted_Connection=True;MultipleActiveResultSets=true" --project src/SmartPlagiarism.Web
```

To see or undo it:

```powershell
dotnet user-secrets list --project src/SmartPlagiarism.Web
dotnet user-secrets remove "ConnectionStrings:DefaultConnection" --project src/SmartPlagiarism.Web
```

---

## 4. Build, test, run

```bash
dotnet build
dotnet test
dotnet run --project src/SmartPlagiarism.Web
```

On first run in Development the app applies all migrations and seeds demo data
automatically — you do **not** need to run `dotnet ef database update` by hand.

Open the URL printed in the console (usually <http://localhost:5028>) and sign in:

| Role | Email | Password |
| --- | --- | --- |
| Admin | admin@uni.edu | `Admin#123` |
| Teacher | teacher@uni.edu | `Teacher#123` |
| Student | student@uni.edu | `Student#123` |

**If the database is not running**, the app still starts and serves pages — it logs
a warning and skips seeding. Sign-in will not work until a database is available.

For HTTPS (optional, one-time per machine):

```bash
dotnet dev-certs https --trust
dotnet run --project src/SmartPlagiarism.Web --launch-profile https
```

---

## 5. OCR (needed for Bangla PDFs and scanned pages)

OCR reads what a PDF page *shows* rather than the text stored inside it. The
system uses it for two kinds of page:

- **Scanned pages** — there is no stored text at all.
- **Bangla PDF pages** — the stored text is usually damaged: PDF fonts have no
  Unicode mapping for conjuncts (ক্ত, ন্ত, র্), and vowel signs like ি and ে come
  out in the wrong order. Without OCR, a Bangla PDF copied word for word can
  score **0%**. With OCR the same file matched at 80% in testing.

DOCX and PPTX files — Bangla included — store text exactly as typed and never
need OCR. Legacy **Bijoy** text (SutonnyMJ and the other "MJ" fonts) is
converted to Unicode automatically in DOCX, PPTX and PDF, with no setup. Without OCR nothing crashes: affected pages use the best text
available, and a warning on the submission says exactly what was lost.

### How it runs on each platform

The app tries the in-process Tesseract library first and falls back to the
`tesseract` command-line program. The library ships for **Windows only**; the
program installs everywhere in one command. Pages are drawn for OCR by PDFium
(the `PDFtoImage` package), which ships for Windows, Linux and macOS.

### 1. Install Tesseract (macOS and Linux)

```bash
brew install tesseract                     # macOS
sudo apt install -y tesseract-ocr          # Ubuntu/Debian
```

Windows needs no install — the NuGet package carries the library.

### 2. Download the Bangla and English language data (all platforms)

The app reads `ben+eng` (Bangla and English on the same page) from
`src/SmartPlagiarism.Web/App_Data/tessdata`. Use the `tessdata_best` models —
the smaller `tessdata_fast` ones are noticeably worse at Bangla.

```bash
mkdir -p src/SmartPlagiarism.Web/App_Data/tessdata
for lang in ben eng; do
  curl -L -o src/SmartPlagiarism.Web/App_Data/tessdata/$lang.traineddata \
    https://github.com/tesseract-ocr/tessdata_best/raw/main/$lang.traineddata
done
```

```powershell
# Windows (PowerShell)
mkdir src\SmartPlagiarism.Web\App_Data\tessdata
foreach ($lang in "ben", "eng") {
  curl.exe -L -o "src\SmartPlagiarism.Web\App_Data\tessdata\$lang.traineddata" `
    "https://github.com/tesseract-ocr/tessdata_best/raw/main/$lang.traineddata"
}
```

`App_Data/` is git-ignored, so every machine downloads these once.

### 3. Check it works

```bash
dotnet test --filter WithRealOcr --logger "console;verbosity=detailed"
```

The test prints the OCR'd Bangla text and its similarity score. If it prints
`Skipped: OCR unavailable here (...)`, the reason in brackets says which step is
missing.

### Settings

```jsonc
// appsettings.json - the defaults
"Ocr": {
  "Enabled": true,
  "TessDataPath": "App_Data/tessdata",
  "Language": "ben+eng",
  "TesseractExecutable": ""   // empty = search PATH, Homebrew and Program Files
}
```

Set `"Enabled": false` to switch OCR off. Whenever OCR cannot run, the reason is
recorded as a warning against every page it would have read.

---

## 6. REST API (portal integration)

The university portal can submit work and fetch results over `/api/v1`.

1. Sign in as an admin, open **API keys**, and create a key for the portal.
   Copy it straight away: it is shown once and only a hash is stored.
2. Send it in the `X-Api-Key` header of every request.

| Method | Path | What it does |
| --- | --- | --- |
| `POST` | `/api/v1/submissions` | Multipart upload (`StudentNumber`, `Title`, `Type`, `CourseId`, `SemesterId`, `Files`). Returns **202** and a `Location` to poll. |
| `GET` | `/api/v1/submissions/{id}` | Analysis status, and the similarity result once `Completed`. |
| `GET` | `/api/v1/submissions/{id}/report` | The originality report PDF (the student's copy). **409** until analysis finishes. |
| `GET` | `/api/v1/health` | Liveness; no key needed. |

```bash
curl -H "X-Api-Key: spk_..." \
     -F StudentNumber=2026-CSE-001 -F Title="My report" -F CourseId=1 -F SemesterId=1 \
     -F Files=@report.docx http://localhost:5028/api/v1/submissions
```

A key sees only the submissions it created. Uploads are limited per key
(`Api:UploadsPerMinute` in appsettings.json, default 10) and answer **429**
beyond that. In Development, the full contract is at **/swagger**.

---

## 7. Archive of past years' work (2010 onwards)

New submissions are compared against every earlier submission **and** against
the archive: reports and theses from before this system existed. Fill it once
from the old records; after that, new work builds up on its own.

1. Sign in as an admin and open **Archive**.
2. Upload a ZIP (up to 1 GB, 20,000 files) of PDF, DOCX and PPTX files. It is
   imported in the background; the page shows progress and refreshes itself.
3. Each file needs a year, taken from the first of:
   - a `manifest.csv` row for it (see below);
   - a year folder in its path, e.g. `2015/cse/thesis.pdf`;
   - the **Default year** chosen on the upload form.

   A file with none of these is skipped, and the import's notes say so.

An optional `manifest.csv` at the top of the ZIP adds the details a file
cannot supply itself. Only `file` is required, columns may be in any order,
and a file can be named by its path in the ZIP or just by its name:

```csv
file,title,author,year,department,course
2015/cse/thesis.pdf,Smart Irrigation,Karim Ahmed,2015,CSE,CSE400
```

- Files already in the archive (same bytes) are skipped, so re-uploading a ZIP
  is harmless, and an import interrupted by a restart resumes on its own.
- Scanned PDFs go through OCR (section 5), and Bangla, including Bijoy
  (SutonnyMJ), is read as it is for submissions.
- Teachers see a match as *Archive 2015: Smart Irrigation - Karim Ahmed*;
  students see only *University archive (2015)*.
- A document can be deleted until a submission has matched it; after that it
  is kept as evidence.

---

## 8. Everyday commands

```bash
dotnet build                                    # compile everything
dotnet test                                     # run the unit tests
dotnet run --project src/SmartPlagiarism.Web    # start the site

# Entity Framework (a migration is only needed when entities change)
dotnet ef migrations add <Name> -p src/SmartPlagiarism.Infrastructure -s src/SmartPlagiarism.Web
dotnet ef database update      -p src/SmartPlagiarism.Infrastructure -s src/SmartPlagiarism.Web
```

`dotnet ef` is pinned in `dotnet-tools.json`. If the command is not found, run
`dotnet tool restore` once.

---

## 9. Working across two machines

1. **Finish on machine A:** `git add -A && git commit && git push`
2. **Continue on machine B:** `git pull`, then `dotnet run`.

On machine B the schema catches up automatically because migrations run at
startup. Remember the two per-machine things from Part 0: machine B has its own
empty database (with fresh demo accounts) and none of machine A's uploaded files.

**Never commit:**

- `App_Data/` — uploaded coursework; already gitignored
- Machine-specific connection strings — use `dotnet user-secrets` instead of
  editing `appsettings.Development.json`
- Large binaries — GitHub rejects anything over 100 MB

---

## 10. Troubleshooting

| Symptom | Cause and fix |
| --- | --- |
| `Skipped Development seeding: the database could not be reached` | SQL Server is not running. `docker start smartplagiarism-sql`, or check your LocalDB secret. |
| `Login failed for user 'sa'` | The container was created with a different password. Remove and recreate it: `docker rm -f smartplagiarism-sql` then rerun the command in Part 3. |
| `dotnet ef` is not recognised | `dotnet tool restore` |
| Port 1433 already in use | Another SQL Server is running. Stop it, or map a different port and set a matching user secret. |
| `Failed to determine the https port for redirect` | Harmless — you started the HTTP-only profile. Use `--launch-profile https` if you want HTTPS. |
| OCR warnings in the log | Expected unless you completed Part 5. Extraction of normal documents is unaffected. |
| An error page shows a **Reference** | It is the request's trace id. Search the log files for it: `grep <reference> src/SmartPlagiarism.Web/App_Data/logs/*.json`. |
| Where are the logs? | The console, and `src/SmartPlagiarism.Web/App_Data/logs/` (one JSON file per day, kept 30 days). Levels are under `Serilog` in `appsettings.json`. |
| Every file shows as modified right after cloning | An old clone made before `.gitattributes` existed. Re-clone, or run `git add --renormalize .`. |
