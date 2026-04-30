# Update NuGet Across Services

Automates updating a NuGet package across multiple Telenor Denmark microservice repositories hosted on GitHub. For each repository it clones, updates the package, builds, runs tests, commits, pushes a feature branch, and opens a pull request.

## Two implementations — same behaviour

| File | Runtime |
|---|---|
| `Update-NuGetAcrossRepos.ps1` | PowerShell 5.1+ |
| `NuGetUpdater/` | .NET 9 (`dotnet run`) |

Edit one → mirror the change to the other.

## Prerequisites

- [git](https://git-scm.com)
- [.NET 9 SDK](https://dot.net)
- GitHub credentials stored in Windows Credential Manager  
  *(run `gh auth login` or clone any GitHub repo — Git will prompt and store them)*

## Configuration

Edit `NuGetUpdater/Config.cs` (or the variables at the top of the `.ps1` file):

| Setting | Default | Description |
|---|---|---|
| `JiraTicketId` | `T40TB-488` | Jira ticket key — used in the branch name, commit message, and PR title |
| `SemVerBump` | `major` | Semver increment keyword embedded in the commit message (`patch` \| `minor` \| `major`) |
| `Packages` | `[{ Name = "Telenor.Api.Core.Web", Version = "25.3.0.9" }]` | One or more packages to update. Set `Version` to `""` for latest |
| `BaseBranch` | `develop` | Source and target branch for the PR |
| `GitHubOrg` | `TelenorInternal` | GitHub organisation that owns the repositories |
| `Reviewers` | GitHub usernames | GitHub usernames added as PR reviewers |
| `Repositories` | 4 GitHub HTTPS URLs | Clone URLs of repos to process — comment out any you want to skip |

## Running

```powershell
# PowerShell
.\Update-NuGetAcrossRepos.ps1

# .NET app (run from the solution root)
dotnet run --project NuGetUpdater
```

Cloned repositories are placed under `checkouts\{JiraTicket}_{yyyy-MM-dd}\` next to the solution. The same folder is reused for all runs on the same day. The `checkouts\` directory is gitignored.

## What it does per repository

1. Clones the repository, checks out `{BaseBranch}`, and pulls the latest.  
   **Skipped if the repo folder already exists** — see [Resuming after a failure](#resuming-after-a-failure).
2. Finds every `.csproj` that references `{NuGetPackageName}` and runs `dotnet add package`.
3. Builds the solution (`Release`, `--no-incremental`). Skips the repo on failure.
4. Runs all tests (`Release`, `--no-build`). Skips the repo on failure.
5. Creates or checks out branch `feature/{JiraTicket}-update-{package}`.
6. Stages all changes (`git add --all`) and commits. Skips if nothing changed.
7. Pushes with `--force-with-lease` (safe re-run if the branch already exists remotely).
8. Opens a pull request via the GitHub REST API v3 with `{Reviewers}` added as requested reviewers. If a PR already exists for the branch, reports its URL instead of failing.

Repos that fail any step are collected in a **Skipped** list printed in the final summary.

## Resuming after a failure

The checkout folder uses a **date-only timestamp** (`yyyy-MM-dd`), so the same folder is reused for all runs on the same day:

```
checkouts\T40TB-488_2026-04-30\dk-s11020-telenor-api-product-orders-service\
```

If a build or test failure occurs, fix the issue manually inside the existing checkout directory, then rerun — the tool detects the folder is already there, skips the clone/checkout/pull, and resumes from the NuGet update step with your fix intact.

> **Note:** all changes in the working tree are staged automatically (`git add --all`), so any manual fixes will be included in the commit on rerun.

## Authentication

No personal access token configuration required. The tool calls `git credential fill` for `github.com` to retrieve the credentials Git already has stored, then uses the password field (typically a PAT) as a Bearer token for all GitHub REST API calls.
