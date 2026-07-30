# Setup guide

How to get SmartPlagiarism running from a clean machine, on **Windows**, **macOS**
or **Linux**. Follow the section for your OS in Part 1, then Parts 2–5 are the
same everywhere.

The same clone works on all three. You can develop on one machine and continue on
another — see [Working across two machines](#7-working-across-two-machines).

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

## 5. OCR (optional — safe to skip)

OCR only affects **scanned PDF pages** (a page with no text layer). PDFs, DOCX and
PPTX files with real text are extracted without it. Skipping OCR costs you nothing
except that scanned pages come back empty with a warning; nothing crashes and no
analysis fails.

### Support by platform

The `Tesseract` NuGet package ships native binaries for **Windows only**
(`x64/tesseract50.dll`). That is a fact about the package, not a limitation of
this project.

| OS | Status |
| --- | --- |
| **Windows** | Works. Download the language data (below) and you are done. |
| **Linux** | Needs system Tesseract libraries installed alongside the language data. Untested by this project. |
| **macOS** | Not supported by the package — no `.dylib` is shipped. The app reports OCR as unavailable and carries on. |

### Enable it (Windows)

```powershell
mkdir src\SmartPlagiarism.Web\App_Data\tessdata
curl -L -o src\SmartPlagiarism.Web\App_Data\tessdata\eng.traineddata `
  https://github.com/tesseract-ocr/tessdata_fast/raw/main/eng.traineddata
```

### Enable it (Linux, best effort)

```bash
sudo apt install -y tesseract-ocr libtesseract-dev libleptonica-dev
mkdir -p src/SmartPlagiarism.Web/App_Data/tessdata
curl -L -o src/SmartPlagiarism.Web/App_Data/tessdata/eng.traineddata \
  https://github.com/tesseract-ocr/tessdata_fast/raw/main/eng.traineddata
```

The .NET wrapper looks for specific library filenames, so this may still need
symlinks. If it does not initialise, the app logs the reason and continues.

### Turn it off explicitly

```jsonc
// appsettings.Development.json
"Ocr": { "Enabled": false }
```

Whatever the outcome, check the startup log — the exact reason OCR is unavailable
is written there and recorded against every affected page.

---

## 6. Everyday commands

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

## 7. Working across two machines

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

## 8. Troubleshooting

| Symptom | Cause and fix |
| --- | --- |
| `Skipped Development seeding: the database could not be reached` | SQL Server is not running. `docker start smartplagiarism-sql`, or check your LocalDB secret. |
| `Login failed for user 'sa'` | The container was created with a different password. Remove and recreate it: `docker rm -f smartplagiarism-sql` then rerun the command in Part 3. |
| `dotnet ef` is not recognised | `dotnet tool restore` |
| Port 1433 already in use | Another SQL Server is running. Stop it, or map a different port and set a matching user secret. |
| `Failed to determine the https port for redirect` | Harmless — you started the HTTP-only profile. Use `--launch-profile https` if you want HTTPS. |
| OCR warnings in the log | Expected unless you completed Part 5. Extraction of normal documents is unaffected. |
| Every file shows as modified right after cloning | An old clone made before `.gitattributes` existed. Re-clone, or run `git add --renormalize .`. |
