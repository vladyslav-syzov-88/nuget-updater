using NuGetUpdater;

var config = new Config();

// ==============================================================================
//  PRE-FLIGHT CHECKS
// ==============================================================================

Log.Header("Pre-flight checks");
Shell.AssertPrerequisite("git", "Install from https://git-scm.com");
Shell.AssertPrerequisite("dotnet", "Install the .NET 9 SDK from https://dot.net");
Shell.Run("Enabling long path support", "git", ["config", "--global", "core.longpaths", "true"]);

var (credUser, credPass) = GitHub.ResolveCredentials();
if (string.IsNullOrEmpty(credUser) || string.IsNullOrEmpty(credPass))
{
	Log.Fail("No credentials found for github.com. Run 'gh auth login' or clone a GitHub repo to store credentials.");
	Environment.Exit(1);
}

GitHub.Configure(config.GitHubOrg, credPass);

if (!GitHub.CheckAuth())
{
	Log.Fail("GitHub authentication failed. Run 'gh auth login' or re-clone a GitHub repo to refresh stored credentials.");
	Environment.Exit(1);
}
Log.Ok("All prerequisites satisfied.");

// ==============================================================================
//  WORKING FOLDER
// ==============================================================================

string timestamp = DateTime.Now.ToString("yyyy-MM-dd");
string solutionRoot = FindSolutionRoot();
string workingRoot = Path.Combine(solutionRoot, "checkouts", $"{config.JiraTicketId}_{timestamp}");
Log.Header($"Working folder: {workingRoot}");

if (!Directory.Exists(workingRoot))
{
	Directory.CreateDirectory(workingRoot);
	Log.Ok($"Created folder '{config.JiraTicketId}'.");
}
else
{
	Log.Warn($"Folder '{config.JiraTicketId}' already exists — skipping creation.");
}

// ==============================================================================
//  MAIN LOOP
// ==============================================================================

var createdPrs = new List<(string Repository, string Branch, string PrUrl)>();
var skippedRepos = new List<string>();

foreach (string repoUrl in config.Repositories)
{
	string repoName = Path.GetFileNameWithoutExtension(repoUrl.Split('/').Last());
	string repoPath = Path.Combine(workingRoot, repoName);

	Log.Header($"Repository: {repoName}");

	// ── Clone + checkout + pull (skipped when repo was cloned in an earlier run today) ──
	if (Directory.Exists(repoPath))
	{
		Log.Warn($"Folder '{repoPath}' already exists — resuming from existing checkout.");
	}
	else
	{
		if (!Shell.Run($"Cloning {repoUrl}", "git", ["clone", repoUrl, repoPath]))
		{
			skippedRepos.Add(repoName);
			continue;
		}

		if (!Shell.Run($"Checking out '{config.BaseBranch}'", "git", ["checkout", config.BaseBranch], repoPath))
		{
			skippedRepos.Add(repoName);
			continue;
		}

		if (!Shell.Run($"Pulling latest '{config.BaseBranch}'", "git", ["pull", "--ff-only"], repoPath))
		{
			skippedRepos.Add(repoName);
			continue;
		}
	}

	// ── Update NuGet packages — only projects that already reference each package ─
	bool anyPackageFound = false;
	bool updateFailed = false;

	foreach (NuGetPackage package in config.Packages)
	{
		Log.Step($"Searching for projects referencing '{package.Name}'...");

		string packagePattern = $"PackageReference Include=\"{package.Name}\"";
		List<string> projectsToUpdate = Directory
			.EnumerateFiles(repoPath, "*.csproj", SearchOption.AllDirectories)
			.Where(f => File.ReadAllText(f).Contains(packagePattern, StringComparison.OrdinalIgnoreCase))
			.ToList();

		if (projectsToUpdate.Count == 0)
		{
			Log.Warn($"Package '{package.Name}' is not referenced in any project — skipping this package.");
			continue;
		}

		anyPackageFound = true;

		foreach (string proj in projectsToUpdate)
		{
			var updateArgs = new List<string> { "add", proj, "package", package.Name };
			if (!string.IsNullOrEmpty(package.Version))
			{
				updateArgs.AddRange(["--version", package.Version]);
			}

			if (!Shell.Run($"Updating '{package.Name}' in {Path.GetFileName(proj)}", "dotnet", [.. updateArgs], repoPath))
			{
				updateFailed = true;
				break;
			}
		}

		if (updateFailed)
			break;
	}

	if (!anyPackageFound)
	{
		Log.Warn("None of the configured packages are referenced in any project. Skipping repo.");
		skippedRepos.Add(repoName);
		continue;
	}

	if (updateFailed)
	{
		skippedRepos.Add(repoName);
		continue;
	}

	// ── Build ─────────────────────────────────────────────────────────────────
	string? slnFile = Directory.EnumerateFiles(repoPath, "*.sln").FirstOrDefault();
	if (slnFile is null)
	{
		Log.Warn($"No .sln file found in '{repoPath}'. Skipping repo.");
		skippedRepos.Add(repoName);
		continue;
	}

	if (!Shell.Run($"Building solution '{Path.GetFileName(slnFile)}'", "dotnet",
			["build", slnFile, "--configuration", "Release", "--no-incremental"], repoPath))
	{
		skippedRepos.Add(repoName);
		continue;
	}

	// ── Tests ─────────────────────────────────────────────────────────────────
	if (!Shell.Run("Running unit tests", "dotnet",
			["test", slnFile, "--configuration", "Release", "--no-build",
			 "--logger", "console;verbosity=minimal"], repoPath))
	{
		skippedRepos.Add(repoName);
		continue;
	}

	// ── Feature branch ────────────────────────────────────────────────────────
	var (_, branchList) = Shell.Capture("git", ["branch", "--list", config.FeatureBranchName], repoPath);
	bool branchExists = !string.IsNullOrWhiteSpace(branchList);

	bool branchOk = branchExists
		? Shell.Run("Checking out existing branch", "git", ["checkout", config.FeatureBranchName], repoPath)
		: Shell.Run($"Creating branch '{config.FeatureBranchName}'", "git", ["checkout", "-b", config.FeatureBranchName], repoPath);

	if (!branchOk)
	{
		skippedRepos.Add(repoName);
		continue;
	}

	// ── Commit ────────────────────────────────────────────────────────────────
	if (!Shell.Run("Staging all changes", "git", ["add", "--all"], repoPath))
	{
		skippedRepos.Add(repoName);
		continue;
	}

	var (_, stagedFiles) = Shell.Capture("git", ["diff", "--cached", "--name-only"], repoPath);
	if (string.IsNullOrWhiteSpace(stagedFiles))
	{
		Log.Warn("No changes to commit — package version may already be up to date. Skipping repo.");
		skippedRepos.Add(repoName);
		continue;
	}

	if (!Shell.Run("Committing changes", "git", ["commit", "-m", config.CommitMessage], repoPath))
	{
		skippedRepos.Add(repoName);
		continue;
	}

	// ── Push & pull request ───────────────────────────────────────────────────
	if (!Shell.Run($"Pushing branch '{config.FeatureBranchName}'", "git",
			["push", "--set-upstream", "--force-with-lease", "origin", config.FeatureBranchName], repoPath))
	{
		skippedRepos.Add(repoName);
		continue;
	}

	Log.Step("Creating pull request...");

	var (prOk, prUrl) = GitHub.CreatePullRequest(
		repo: repoName,
		title: config.PullRequestTitle,
		body: config.PrBody,
		fromBranch: config.FeatureBranchName,
		toBranch: config.BaseBranch,
		reviewers: config.Reviewers);

	if (!prOk)
	{
		Log.Fail($"Failed to create pull request: {prUrl}");
		skippedRepos.Add(repoName);
		continue;
	}

	Log.Ok($"Pull request created: {prUrl}");
	createdPrs.Add((repoName, config.FeatureBranchName, prUrl));
}

// ==============================================================================
//  SUMMARY
// ==============================================================================

Log.Header("Summary");

if (createdPrs.Count > 0)
{
	Console.ForegroundColor = ConsoleColor.Green;
	Console.WriteLine($"\nPull requests created ({createdPrs.Count}):");
	Console.ResetColor();
	foreach (var (repo, _, prUrl) in createdPrs)
	{
		Console.WriteLine($"  {repo.PadRight(40)} {prUrl}");
	}
}
else
{
	Log.Warn("No pull requests were created.");
}

if (skippedRepos.Count > 0)
{
	Console.ForegroundColor = ConsoleColor.Yellow;
	Console.WriteLine($"\nSkipped repositories ({skippedRepos.Count}):");
	foreach (string repo in skippedRepos)
	{
		Console.WriteLine($"  - {repo}");
	}
	Console.ResetColor();
}

Console.WriteLine();

// ==============================================================================
//  HELPERS
// ==============================================================================

static string FindSolutionRoot()
{
	var dir = new DirectoryInfo(AppContext.BaseDirectory);
	while (dir is not null)
	{
		if (dir.EnumerateFiles("*.slnx").Any())
			return dir.FullName;
		dir = dir.Parent;
	}
	return AppContext.BaseDirectory;
}