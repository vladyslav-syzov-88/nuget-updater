#Requires -Version 5.1
<#
.SYNOPSIS
    Updates a NuGet package across multiple git repositories and opens pull requests.

.DESCRIPTION
    For each configured repository the script:
      1.  Creates a working folder named after the Jira ticket.
      2.  Clones the repository into a sub-folder.
      3.  Updates the specified NuGet package in every project that already references it.
      4.  Verifies the solution builds successfully.
      5.  Verifies all unit tests pass.
      6.  Creates a feature branch.
      7.  Commits the changes.
      8.  Pushes the branch and opens a pull request via the Bitbucket Server REST API.
    At the end it prints a summary of every pull request that was created.

.NOTES
    Prerequisites:
      - git       : https://git-scm.com
      - dotnet    : .NET 9 SDK
      - A Bitbucket Server Personal Access Token set in $BitbucketPat
#>

# ==============================================================================
#  CONFIGURATION — edit these values before running
# ==============================================================================

$JiraTicketId      = "BMRT-XXXX"
$JiraTicketUrl     = "https://telenordigital.atlassian.net/browse/"
$SemVerBump        = "major"          # patch | minor | major
$BaseBranch        = "development"
$BitbucketBaseUrl  = "http://stash.iux.sonofon.dk:7990"
$Reviewers         = @("MYKO")        # Bitbucket usernames

# Packages to update — each entry is a hashtable with Name and optional Version
$Packages = @(
    @{ Name = "Telenor.Api.Core.Web"; Version = "24.0.0.5" }
)

# Derived values
$PackageSummary    = if ($Packages.Count -eq 1) { $Packages[0].Name } else { "NuGet packages" }
$FeatureBranchName = if ($Packages.Count -eq 1) { "feature/$JiraTicketId-update-$($Packages[0].Name.ToLower())" } else { "feature/$JiraTicketId-update-nugets" }
$CommitMessage     = "$JiraTicketId +semver: $SemVerBump update $PackageSummary"
$PullRequestTitle  = "$JiraTicketId Update $PackageSummary"

# List of repositories to process (HTTPS clone URLs)
$Repositories = @(
    "http://stash.iux.sonofon.dk:7990/scm/osa/telenor.api.accountmanagement.service.git",
    "http://stash.iux.sonofon.dk:7990/scm/osa/telenor.api.appointmentmanagement.service.git",
    "http://stash.iux.sonofon.dk:7990/scm/osa/telenor.api.billmanagement.service.git",
    "http://stash.iux.sonofon.dk:7990/scm/osa/telenor.api.communicationmanagement.service.git",
    "http://stash.iux.sonofon.dk:7990/scm/osa/telenor.api.customermanagement.service.git",
    "http://stash.iux.sonofon.dk:7990/scm/osa/telenor.api.partymanagement.service.git",
    "http://stash.iux.sonofon.dk:7990/scm/osa/telenor.api.productcatalog.service.git",
    "http://stash.iux.sonofon.dk:7990/scm/osa/telenor.api.productinventorymanagement.service.git",
    "http://stash.iux.sonofon.dk:7990/scm/osa/telenor.api.productofferingqualification.service.git",
    "http://stash.iux.sonofon.dk:7990/scm/osa/telenor.api.productorderingmanagement.service.git",
    "http://stash.iux.sonofon.dk:7990/scm/osa/telenor.api.productorders.service.git",
    "http://stash.iux.sonofon.dk:7990/scm/osa/telenor.api.producttemplates.service.git",
    "http://stash.iux.sonofon.dk:7990/scm/osa/telenor.api.resourcemanagement.service.git",
    "http://stash.iux.sonofon.dk:7990/scm/osa/telenor.api.stockmanagement.service.git",
    "http://stash.iux.sonofon.dk:7990/scm/osa/telenor.api.troubleticketmanagement.service.git",
    "http://stash.iux.sonofon.dk:7990/scm/osa/telenor.api.usermanagement.service.git"
)

# ==============================================================================
#  HELPERS
# ==============================================================================

function Write-Step   { param([string]$Msg) Write-Host "  --> $Msg" -ForegroundColor Cyan   }
function Write-Ok     { param([string]$Msg) Write-Host "  [OK] $Msg" -ForegroundColor Green  }
function Write-Fail   { param([string]$Msg) Write-Host "  [FAIL] $Msg" -ForegroundColor Red  }
function Write-Warn   { param([string]$Msg) Write-Host "  [WARN] $Msg" -ForegroundColor Yellow }
function Write-Header { param([string]$Msg) Write-Host "`n==============================`n  $Msg`n==============================" -ForegroundColor Magenta }

function Invoke-Cmd {
    param(
        [string]   $Description,
        [string]   $Executable,
        [string[]] $Arguments
    )
    Write-Step $Description
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        Write-Fail "$Description failed (exit $LASTEXITCODE)."
        return $false
    }
    Write-Ok $Description
    return $true
}

function Assert-Prerequisite {
    param([string]$Command, [string]$InstallHint)
    if (-not (Get-Command $Command -ErrorAction SilentlyContinue)) {
        Write-Fail "'$Command' not found. $InstallHint"
        exit 1
    }
}

function Get-BitbucketCredentials {
    $uri    = [Uri]$BitbucketBaseUrl
    $input  = "protocol=$($uri.Scheme)`nhost=$($uri.Host):$($uri.Port)`n`n"
    $output = $input | git credential fill

    $username = ($output | Where-Object { $_ -match '^username=' }) -replace '^username=', ''
    $password = ($output | Where-Object { $_ -match '^password=' }) -replace '^password=', ''
    return @{ Username = $username; Password = $password }
}

function New-BitbucketPullRequest {
    param(
        [string]$ProjectKey,
        [string]$RepoSlug,
        [string]$Title,
        [string]$Description,
        [string]$FromBranch,
        [string]$ToBranch
    )

    $reviewerList = $Reviewers | ForEach-Object { @{ user = @{ name = $_ } } }

    $payload = @{
        title       = $Title
        description = $Description
        fromRef     = @{
            id         = $FromBranch
            repository = @{
                slug    = $RepoSlug
                project = @{ key = $ProjectKey }
            }
        }
        toRef       = @{
            id         = $ToBranch
            repository = @{
                slug    = $RepoSlug
                project = @{ key = $ProjectKey }
            }
        }
        reviewers   = @($reviewerList)
    } | ConvertTo-Json -Depth 10

    $uri = "$BitbucketBaseUrl/rest/api/1.0/projects/$ProjectKey/repos/$RepoSlug/pull-requests"

    try {
        $response = Invoke-RestMethod -Uri $uri -Method Post -Headers $script:BitbucketHeaders -Body $payload
        return $response.links.self[0].href
    } catch {
        $statusCode = $_.Exception.Response.StatusCode.value__
        if ($statusCode -eq 409) {
            $body = $_ | ConvertFrom-Json -ErrorAction SilentlyContinue
            $existingUrl = $body.errors[0].existingPullRequest.links.self[0].href
            if ($existingUrl) { return "$existingUrl (already existed)" }
        }
        throw
    }
}

# ==============================================================================
#  PRE-FLIGHT CHECKS
# ==============================================================================

Write-Header "Pre-flight checks"

Assert-Prerequisite "git"    "Install from https://git-scm.com"
Assert-Prerequisite "dotnet" "Install the .NET 9 SDK from https://dot.net"
Invoke-Cmd "Enabling long path support" "git" @("config", "--global", "core.longpaths", "true")

$creds = Get-BitbucketCredentials
if ([string]::IsNullOrWhiteSpace($creds.Username) -or [string]::IsNullOrWhiteSpace($creds.Password)) {
    Write-Fail "No credentials found for $BitbucketBaseUrl. Make sure you have cloned a repo from that host so Git has stored your credentials."
    exit 1
}

$encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("$($creds.Username):$($creds.Password)"))
$script:BitbucketHeaders = @{
    Authorization  = "Basic $encoded"
    "Content-Type" = "application/json"
}

try {
    Invoke-RestMethod `
        -Uri     "$BitbucketBaseUrl/rest/api/1.0/projects?limit=1" `
        -Headers $script:BitbucketHeaders | Out-Null
    Write-Ok "All prerequisites satisfied."
} catch {
    Write-Fail "Bitbucket authentication failed. Try re-cloning a repo from that host to refresh stored credentials."
    exit 1
}

# ==============================================================================
#  WORKING FOLDER
# ==============================================================================

$ScriptDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$Timestamp   = Get-Date -Format "yyyy-MM-dd"
$WorkingRoot = Join-Path $ScriptDir "checkouts" "$($JiraTicketId)_$Timestamp"

Write-Header "Working folder: $WorkingRoot"

if (-not (Test-Path $WorkingRoot)) {
    New-Item -ItemType Directory -Path $WorkingRoot | Out-Null
    Write-Ok "Created folder '$JiraTicketId'."
} else {
    Write-Warn "Folder '$JiraTicketId' already exists — skipping creation."
}

# ==============================================================================
#  MAIN LOOP
# ==============================================================================

$CreatedPullRequests = [System.Collections.Generic.List[PSCustomObject]]::new()
$SkippedRepos        = [System.Collections.Generic.List[string]]::new()

foreach ($RepoUrl in $Repositories) {

    $UrlParts   = $RepoUrl -split '/'
    $ProjectKey = $UrlParts[-2].ToUpper()
    $RepoSlug   = [IO.Path]::GetFileNameWithoutExtension($UrlParts[-1])
    $RepoName   = $RepoSlug
    $RepoPath   = Join-Path $WorkingRoot $RepoName

    Write-Header "Repository: $RepoName"

    # ------------------------------------------------------------------
    # Clone + checkout + pull (skipped when repo was cloned in an earlier run today)
    # ------------------------------------------------------------------
    $repoExisted = Test-Path $RepoPath

    if ($repoExisted) {
        Write-Warn "Folder '$RepoPath' already exists — resuming from existing checkout."
    } else {
        $ok = Invoke-Cmd "Cloning $RepoUrl" "git" @("clone", $RepoUrl, $RepoPath)
        if (-not $ok) { $SkippedRepos.Add($RepoName); continue }
    }

    Push-Location $RepoPath

    try {

        if (-not $repoExisted) {
            $ok = Invoke-Cmd "Checking out '$BaseBranch'" "git" @("checkout", $BaseBranch)
            if (-not $ok) { $SkippedRepos.Add($RepoName); continue }

            $ok = Invoke-Cmd "Pulling latest '$BaseBranch'" "git" @("pull", "--ff-only")
            if (-not $ok) { $SkippedRepos.Add($RepoName); continue }
        }

        # ------------------------------------------------------------------
        # Update NuGet packages — only in projects that already reference each
        # ------------------------------------------------------------------
        $anyPackageFound = $false
        $updateFailed    = $false

        foreach ($package in $Packages) {
            Write-Step "Searching for projects referencing '$($package.Name)'..."

            $projectsToUpdate = Get-ChildItem -Path . -Filter "*.csproj" -Recurse |
                Where-Object {
                    Select-String -Path $_.FullName `
                                  -Pattern ([regex]::Escape("PackageReference Include=`"$($package.Name)`"")) `
                                  -Quiet
                }

            if ($projectsToUpdate.Count -eq 0) {
                Write-Warn "Package '$($package.Name)' is not referenced in any project — skipping this package."
                continue
            }

            $anyPackageFound = $true

            foreach ($proj in $projectsToUpdate) {
                $updateArgs = @("add", $proj.FullName, "package", $package.Name)
                if ($package.Version) { $updateArgs += @("--version", $package.Version) }

                $ok = Invoke-Cmd "Updating '$($package.Name)' in $($proj.Name)" "dotnet" $updateArgs
                if (-not $ok) { $updateFailed = $true; break }
            }

            if ($updateFailed) { break }
        }

        if (-not $anyPackageFound) {
            Write-Warn "None of the configured packages are referenced in any project. Skipping repo."
            $SkippedRepos.Add($RepoName)
            continue
        }

        if ($updateFailed) { $SkippedRepos.Add($RepoName); continue }

        # ------------------------------------------------------------------
        # Build
        # ------------------------------------------------------------------
        $slnFile = Get-ChildItem -Path . -Filter "*.sln" | Select-Object -First 1
        if (-not $slnFile) {
            Write-Warn "No .sln file found in '$RepoPath'. Skipping repo."
            $SkippedRepos.Add($RepoName)
            continue
        }

        $ok = Invoke-Cmd "Building solution '$($slnFile.Name)'" "dotnet" @(
            "build", $slnFile.FullName, "--configuration", "Release", "--no-incremental"
        )
        if (-not $ok) { $SkippedRepos.Add($RepoName); continue }

        # ------------------------------------------------------------------
        # Tests
        # ------------------------------------------------------------------
        $ok = Invoke-Cmd "Running unit tests" "dotnet" @(
            "test", $slnFile.FullName, "--configuration", "Release", "--no-build",
            "--logger", "console;verbosity=minimal"
        )
        if (-not $ok) { $SkippedRepos.Add($RepoName); continue }

        # ------------------------------------------------------------------
        # Feature branch
        # ------------------------------------------------------------------
        $existingBranch = git branch --list $FeatureBranchName
        if ($existingBranch) {
            Write-Warn "Branch '$FeatureBranchName' already exists locally — checking it out."
            $ok = Invoke-Cmd "Checking out existing branch" "git" @("checkout", $FeatureBranchName)
        } else {
            $ok = Invoke-Cmd "Creating branch '$FeatureBranchName'" "git" @("checkout", "-b", $FeatureBranchName)
        }
        if (-not $ok) { $SkippedRepos.Add($RepoName); continue }

        # ------------------------------------------------------------------
        # Commit
        # ------------------------------------------------------------------
        $ok = Invoke-Cmd "Staging all changes" "git" @("add", "--all")
        if (-not $ok) { $SkippedRepos.Add($RepoName); continue }

        $stagedFiles = git diff --cached --name-only
        if (-not $stagedFiles) {
            Write-Warn "No changes to commit — package version may already be up to date. Skipping repo."
            $SkippedRepos.Add($RepoName)
            continue
        }

        $ok = Invoke-Cmd "Committing changes" "git" @("commit", "-m", $CommitMessage)
        if (-not $ok) { $SkippedRepos.Add($RepoName); continue }

        # ------------------------------------------------------------------
        # Push & pull request
        # ------------------------------------------------------------------
        $ok = Invoke-Cmd "Pushing branch '$FeatureBranchName'" "git" @(
            "push", "--set-upstream", "--force-with-lease", "origin", $FeatureBranchName
        )
        if (-not $ok) { $SkippedRepos.Add($RepoName); continue }

        Write-Step "Creating pull request..."

        $packageLines = ($Packages | ForEach-Object {
            $versionNote = if ($_.Version) { " to version ``$($_.Version)``" } else { " to the latest available version" }
            "- Updated NuGet package ``$($_.Name)``$versionNote"
        }) -join "`n"
        $description = @"
## Summary

$packageLines

## Jira

[$JiraTicketId]($JiraTicketUrl$JiraTicketId)

## Test plan

- [x] Solution builds successfully
- [x] Unit tests pass

Automated via Update-NuGetAcrossRepos.ps1
"@

        try {
            $prUrl = New-BitbucketPullRequest `
                -ProjectKey $ProjectKey `
                -RepoSlug   $RepoSlug `
                -Title      $PullRequestTitle `
                -Description $description `
                -FromBranch $FeatureBranchName `
                -ToBranch   $BaseBranch

            Write-Ok "Pull request created: $prUrl"
            $CreatedPullRequests.Add([PSCustomObject]@{
                Repository = $RepoName
                Branch     = $FeatureBranchName
                PrUrl      = $prUrl
            })
        } catch {
            Write-Fail "Failed to create pull request: $_"
            $SkippedRepos.Add($RepoName)
            continue
        }

    } finally {
        Pop-Location
    }
}

# ==============================================================================
#  SUMMARY
# ==============================================================================

Write-Header "Summary"

if ($CreatedPullRequests.Count -gt 0) {
    Write-Host "`nPull requests created ($($CreatedPullRequests.Count)):" -ForegroundColor Green
    $CreatedPullRequests | ForEach-Object {
        Write-Host "  $($_.Repository.PadRight(40)) $($_.PrUrl)" -ForegroundColor White
    }
} else {
    Write-Warn "No pull requests were created."
}

if ($SkippedRepos.Count -gt 0) {
    Write-Host "`nSkipped repositories ($($SkippedRepos.Count)):" -ForegroundColor Yellow
    $SkippedRepos | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow }
}

Write-Host ""
