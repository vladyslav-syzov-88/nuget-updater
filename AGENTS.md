> **MUST READ:** You MUST read this file in full before editing any existing file or writing any new code in this solution.

# Update NuGet Across Services

## Mandatory: Keep README.md in sync

**Whenever you change behaviour, configuration, prerequisites, or the workflow in this solution you MUST also update `README.md` to reflect those changes before considering the task done.** README.md is the user-facing documentation — it must never lag behind the code.

## Overview

This solution automates updating a single NuGet package across multiple Telenor Denmark microservice repositories (Bitbucket Server / Stash). For each repository it clones, updates the package, builds, runs tests, commits, pushes a feature branch, and opens a pull request via the Bitbucket Server REST API.

Two equivalent implementations exist side-by-side — use whichever fits the runtime:

| File | Runtime | When to use |
|---|---|---|
| `Update-NuGetAcrossRepos.ps1` | PowerShell 5.1+ | Windows / quick ad-hoc run |
| `NuGetUpdater/` (.NET 9 console app) | `dotnet run` | Cross-platform or CI pipeline |

## Solution Structure

```
Update NuGet Across Services/
├── AGENTS.md                          # This file — read before every change
├── README.md                          # User-facing documentation — keep in sync with code
├── .gitignore                         # Excludes checkouts/
├── NuGetUpdater.slnx                  # Solution file (single project)
├── Update-NuGetAcrossRepos.ps1        # PowerShell implementation
├── checkouts/                         # Created at runtime — gitignored
│   └── {JiraTicket}_{timestamp}/      # One folder per run
└── NuGetUpdater/
    ├── NuGetUpdater.csproj            # net9.0 console app, no extra NuGet deps
    ├── Config.cs                      # All user-editable settings (single source of truth)
    └── Program.cs                     # Top-level statements + static helper classes
```

## Configuration

**All settings live in one place.** For the .NET app edit `NuGetUpdater/Config.cs`; for PowerShell edit the variables at the top of `Update-NuGetAcrossRepos.ps1`. The two files mirror each other exactly.

| Setting | Default | Description |
|---|---|---|
| `JiraTicketId` | `BMRT-XXXX` | Used in the checkout folder name, branch name, commit message, and PR title |
| `SemVerBump` | `major` | Semver increment keyword embedded in the commit message (`patch` \| `minor` \| `major`) |
| `Packages` | `[{ Name = "Telenor.Api.Core.Web", Version = "24.0.0.5" }]` | One or more packages to update. Set `Version = ""` to update to latest |
| `BaseBranch` | `development` | Branch to clone from and target for the PR |
| `BitbucketBaseUrl` | `http://stash.iux.sonofon.dk:7990` | Bitbucket Server root URL |
| `Reviewers` | `["MYKO", "CATK", "VLOR", "TEAMNIK"]` | Bitbucket usernames added as PR reviewers |
| `Repositories` | 16 Stash URLs | HTTPS clone URLs of repos to process |

Derived values (computed, not stored):

- `FeatureBranchName` → `feature/{JiraTicketId}-update-{packagename}` (single package) or `feature/{JiraTicketId}-update-nugets` (multiple)
- `CommitMessage` → `{JiraTicketId} +semver: {SemVerBump} update {PackageSummary}`
- `PullRequestTitle` → `{JiraTicketId} Update {PackageSummary}`

## Per-Repo Workflow

1. **Clone + checkout + pull** — clone into `checkouts/{JiraTicket}_{date}/{repoName}`, checkout `{BaseBranch}`, pull. **Skipped entirely if the repo directory already exists** — see Resume behaviour below.
2. **Selective update** — scan `*.csproj` files for `PackageReference Include="{NuGetPackageName}"`. Skip the whole repo if none found. Run `dotnet add <proj> package {NuGetPackageName} [--version {ver}]` for each matching project. `dotnet add package` is idempotent — safe to re-run.
3. **Build** — `dotnet build *.sln --configuration Release --no-incremental`. Skip repo if no `.sln` found.
4. **Test** — `dotnet test *.sln --configuration Release --no-build --logger console;verbosity=minimal`.
5. **Feature branch** — checkout existing or create `{FeatureBranchName}`.
6. **Commit** — stage all changes (`git add --all`), then commit. Skip repo if nothing staged.
7. **Push** — `git push --set-upstream --force-with-lease origin {FeatureBranchName}`.
8. **PR** — POST to Bitbucket Server REST API. If a PR already exists (HTTP 409), extract and report its URL instead of failing.

On any failure the repo is added to a **Skipped** list and the loop continues.

### Resume behaviour

The checkout folder uses a **date-only timestamp** (`yyyy-MM-dd`), so the same folder is reused for all runs on the same day. When a repo directory already exists inside that folder, steps 1 (clone + checkout + pull) are skipped and processing resumes from step 2.

**Primary use case:** if a build fails, fix the issue manually in the existing checkout, then rerun — the tool picks up from the NuGet update step without discarding your fix.

**Staging note:** all changes in the working tree are staged automatically (`git add --all`). Manual fixes of any file type will be included in the commit on rerun.

## Authentication

**No PAT or extra configuration required.** Credentials are resolved via `git credential fill` against `BitbucketBaseUrl` — the same credentials Git uses for cloning. Ensure you have cloned at least one repo from that host so Windows Credential Manager has them stored. HTTP Basic Auth is used for all Bitbucket REST API calls.

## Prerequisites

- `git` — https://git-scm.com
- `dotnet` — .NET 9 SDK — https://dot.net
- Credentials for `stash.iux.sonofon.dk` stored in Windows Credential Manager (clone any repo from that host once to populate them)

The pre-flight checks also automatically run `git config --global core.longpaths true` to prevent Windows MAX_PATH failures on deeply nested repo files.

## Running

```powershell
# PowerShell
.\Update-NuGetAcrossRepos.ps1

# .NET app (from solution root)
dotnet run --project NuGetUpdater
```

Checkouts are created under `checkouts\{JiraTicket}_{yyyy-MM-dd_HH-mm-ss}\` next to the solution file. Each run gets its own timestamped folder. The `checkouts\` directory is gitignored.

## Code Structure (Program.cs)

The file has two sections separated by the `HELPERS` banner:

1. **Top-level statements** — the linear workflow (pre-flight → working folder → main loop → summary). Read top-to-bottom as prose.
2. **Static helper classes** defined after the statements:
   - `Log` — coloured console output (`Step`, `Ok`, `Fail`, `Warn`, `Header`), all delegating to a private `Write` method.
   - `Shell` — process execution (`AssertPrerequisite`, `Run`, `Capture`) plus private `Execute` and `BuildPsi` helpers.
   - `Bitbucket` — REST API client (`ResolveCredentials`, `Configure`, `CheckAuth`, `CreatePullRequest`).

## EditorConfig compliance

All code changes must follow the rules in `.editorconfig` at the repo root. Key rules:

- **Indentation** — tabs, width 4
- **Line endings** — CRLF
- **No trailing whitespace**, no final newline at end of file
- **Namespaces** — file-scoped (`namespace Foo;`)
- **`using` directives** — `System.*` first, no blank line between groups
- **Static fields** — PascalCase (e.g. `Client`, not `_client`)
- **Instance private fields** — `_camelCase`
- **Naming** — types and members PascalCase; parameters and locals camelCase; async methods end with `Async`

After every change verify with:

```powershell
dotnet format NuGetUpdater/NuGetUpdater.csproj --verify-no-changes
```

If it reports changes, run without `--verify-no-changes` to auto-fix, then re-run to confirm clean.

## Build verification

After every code change, run:

```powershell
dotnet build NuGetUpdater/NuGetUpdater.csproj --configuration Release
```

The build must succeed with **0 warnings and 0 errors**. If warnings appear, resolve them before considering the task done — do not leave new warnings behind. Warnings treated as noise accumulate and mask real problems.

## Conventions

- **No extra NuGet dependencies** — `NuGetUpdater.csproj` has no `<PackageReference>` entries; keep it that way. Use BCL APIs only (`System.Net.Http`, `System.Text.Json`, etc. are all in-box).
- **Config is the only edit point** — business logic lives in `Program.cs`. Do not split it into multiple files unless the file grows unmaintainable.
- **Mirror changes across both implementations** — if you change behaviour in the .NET app, apply the equivalent change to the PowerShell script, and vice versa.
- **Idempotent by design** — re-running against an already-processed repo is safe. Clone and branch steps are guarded by existence checks; push uses `--force-with-lease`; duplicate PRs are detected via HTTP 409 and reported rather than treated as errors.
- **Lock files** — `packages.lock.json` files are always staged alongside `.csproj` changes because repos use `RestorePackagesWithLockFile`; omitting them breaks CI locked-restore.
- **Emoji in PR descriptions are forbidden** — the Bitbucket Server database uses MySQL `utf8` (not `utf8mb4`) and rejects 4-byte Unicode characters with a HTTP 500 DataStoreException.
- **No comments explaining the obvious** — inline comments are reserved for non-obvious constraints (e.g. the lock-file and emoji notes above).
