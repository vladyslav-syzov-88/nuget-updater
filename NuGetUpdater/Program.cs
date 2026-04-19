using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using NuGetUpdater;

var config = new Config();

// ==============================================================================
//  PRE-FLIGHT CHECKS
// ==============================================================================

Log.Header("Pre-flight checks");
Shell.AssertPrerequisite("git",    "Install from https://git-scm.com");
Shell.AssertPrerequisite("dotnet", "Install the .NET 9 SDK from https://dot.net");
Shell.Run("Enabling long path support", "git", ["config", "--global", "core.longpaths", "true"]);

var (credUser, credPass) = Bitbucket.ResolveCredentials(config.BitbucketBaseUrl);
if (string.IsNullOrEmpty(credUser) || string.IsNullOrEmpty(credPass))
{
	Log.Fail($"No credentials found for {config.BitbucketBaseUrl}. Make sure you have cloned a repo from that host so Git has stored your credentials.");
	Environment.Exit(1);
}

Bitbucket.Configure(config.BitbucketBaseUrl, credUser, credPass);

if (!Bitbucket.CheckAuth())
{
	Log.Fail("Bitbucket authentication failed. Try re-cloning a repo from that host to refresh stored credentials.");
	Environment.Exit(1);
}
Log.Ok("All prerequisites satisfied.");

// ==============================================================================
//  WORKING FOLDER
// ==============================================================================

string timestamp   = DateTime.Now.ToString("yyyy-MM-dd");
string solutionRoot = FindSolutionRoot();
string workingRoot  = Path.Combine(solutionRoot, "checkouts", $"{config.JiraTicketId}_{timestamp}");
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

var createdPrs   = new List<(string Repository, string Branch, string PrUrl)>();
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
	bool updateFailed    = false;

	foreach (NuGetPackage package in config.Packages)
	{
		Log.Step($"Searching for projects referencing '{package.Name}'...");

		string packagePattern         = $"PackageReference Include=\"{package.Name}\"";
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
	bool branchExists   = !string.IsNullOrWhiteSpace(branchList);

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

	string[] urlParts  = repoUrl.Split('/');
	string projectKey  = urlParts[^2].ToUpper();
	string repoSlug    = Path.GetFileNameWithoutExtension(urlParts[^1]);

	var (prOk, prUrl) = Bitbucket.CreatePullRequest(
		projectKey:  projectKey,
		repoSlug:    repoSlug,
		title:       config.PullRequestTitle,
		description: config.PrBody,
		fromBranch:  config.FeatureBranchName,
		toBranch:    config.BaseBranch,
		reviewers:   config.Reviewers);

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

internal static class Log
{
	internal static void Step(string msg)   => Write(ConsoleColor.Cyan,    $"  --> {msg}");
	internal static void Ok(string msg)     => Write(ConsoleColor.Green,   $"  [OK] {msg}");
	internal static void Fail(string msg)   => Write(ConsoleColor.Red,     $"  [FAIL] {msg}");
	internal static void Warn(string msg)   => Write(ConsoleColor.Yellow,  $"  [WARN] {msg}");
	internal static void Header(string msg) => Write(ConsoleColor.Magenta, $"\n==============================\n  {msg}\n==============================");

	private static void Write(ConsoleColor color, string msg)
	{
		Console.ForegroundColor = color;
		Console.WriteLine(msg);
		Console.ResetColor();
	}
}

internal static class Shell
{
	internal static void AssertPrerequisite(string command, string installHint)
	{
		var psi = new ProcessStartInfo
		{
			FileName               = OperatingSystem.IsWindows() ? "where" : "which",
			Arguments              = command,
			UseShellExecute        = false,
			RedirectStandardOutput = true,
			RedirectStandardError  = true,
		};
		using var p = Process.Start(psi)!;
		p.WaitForExit();
		if (p.ExitCode != 0)
		{
			Log.Fail($"'{command}' not found. {installHint}");
			Environment.Exit(1);
		}
	}

	internal static bool Run(string description, string executable, string[] args, string? workingDir = null)
	{
		Log.Step(description);
		int exitCode = Execute(executable, args, workingDir);
		if (exitCode != 0)
		{
			Log.Fail($"{description} failed (exit {exitCode}).");
			return false;
		}
		Log.Ok(description);
		return true;
	}

	internal static (int ExitCode, string Output) Capture(string executable, string[] args, string? workingDir = null)
	{
		var output    = new StringBuilder();
		using var process = new Process { StartInfo = BuildPsi(executable, args, workingDir) };

		process.OutputDataReceived += (_, e) =>
		{
			if (e.Data is null)
				return;
			Console.WriteLine(e.Data);
			output.AppendLine(e.Data);
		};
		process.ErrorDataReceived += (_, e) =>
		{
			if (e.Data is null)
				return;
			Console.WriteLine(e.Data);
			output.AppendLine(e.Data);
		};

		process.Start();
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();
		process.WaitForExit();
		return (process.ExitCode, output.ToString().Trim());
	}

	private static int Execute(string executable, string[] args, string? workingDir)
	{
		using var process = new Process { StartInfo = BuildPsi(executable, args, workingDir) };
		process.OutputDataReceived += (_, e) => { if (e.Data is not null) Console.WriteLine(e.Data); };
		process.ErrorDataReceived  += (_, e) => { if (e.Data is not null) Console.WriteLine(e.Data); };
		process.Start();
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();
		process.WaitForExit();
		return process.ExitCode;
	}

	private static ProcessStartInfo BuildPsi(string executable, string[] args, string? workingDir)
	{
		var psi = new ProcessStartInfo(executable)
		{
			UseShellExecute        = false,
			RedirectStandardOutput = true,
			RedirectStandardError  = true,
			WorkingDirectory       = workingDir ?? Directory.GetCurrentDirectory(),
		};
		foreach (string arg in args)
		{
			psi.ArgumentList.Add(arg);
		}
		return psi;
	}
}

internal static class Bitbucket
{
	private static readonly HttpClient Client = new();
	private static string _baseUrl = "";

	internal static (string Username, string Password) ResolveCredentials(string baseUrl)
	{
		var uri   = new Uri(baseUrl);
		string input = $"protocol={uri.Scheme}\nhost={uri.Host}:{uri.Port}\n\n";

		var psi = new ProcessStartInfo("git", "credential fill")
		{
			UseShellExecute        = false,
			RedirectStandardInput  = true,
			RedirectStandardOutput = true,
			RedirectStandardError  = true,
		};
		using var p = Process.Start(psi)!;
		p.StandardInput.Write(input);
		p.StandardInput.Close();
		string output = p.StandardOutput.ReadToEnd();
		p.WaitForExit();

		string username = "", password = "";
		foreach (string line in output.Split('\n'))
		{
			if (line.StartsWith("username=")) username = line["username=".Length..].Trim();
			if (line.StartsWith("password=")) password = line["password=".Length..].Trim();
		}
		return (username, password);
	}

	internal static void Configure(string baseUrl, string username, string password)
	{
		_baseUrl = baseUrl.TrimEnd('/');
		string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
		Client.DefaultRequestHeaders.Authorization =
			new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", encoded);
	}

	internal static bool CheckAuth()
	{
		try
		{
			var response = Client.GetAsync($"{_baseUrl}/rest/api/1.0/projects?limit=1").GetAwaiter().GetResult();
			return response.IsSuccessStatusCode;
		}
		catch
		{
			return false;
		}
	}

	internal static (bool Success, string PrUrl) CreatePullRequest(
		string projectKey,
		string repoSlug,
		string title,
		string description,
		string fromBranch,
		string toBranch,
		IEnumerable<string> reviewers)
	{
		var body = new
		{
			title,
			description,
			fromRef = new
			{
				id         = fromBranch,
				repository = new
				{
					slug    = repoSlug,
					project = new { key = projectKey },
				},
			},
			toRef = new
			{
				id         = toBranch,
				repository = new
				{
					slug    = repoSlug,
					project = new { key = projectKey },
				},
			},
			reviewers = reviewers.Select(r => new { user = new { name = r } }).ToArray(),
		};

		string url      = $"{_baseUrl}/rest/api/1.0/projects/{projectKey}/repos/{repoSlug}/pull-requests";
		var    response = Client.PostAsJsonAsync(url, body).GetAwaiter().GetResult();
		string content  = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

		if (!response.IsSuccessStatusCode)
		{
			// 409 means a PR already exists for this branch — extract its URL and treat as success
			if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
			{
				try
				{
					using var errDoc = JsonDocument.Parse(content);
					string existingUrl = errDoc.RootElement
						.GetProperty("errors")[0]
						.GetProperty("existingPullRequest")
						.GetProperty("links")
						.GetProperty("self")[0]
						.GetProperty("href")
						.GetString() ?? "";
					if (!string.IsNullOrEmpty(existingUrl))
						return (true, $"{existingUrl} (already existed)");
				}
				catch { /* fall through to generic error */ }
			}

			return (false, $"HTTP {(int)response.StatusCode} {response.StatusCode}: {content}");
		}

		using var doc = JsonDocument.Parse(content);
		string prUrl = doc.RootElement
			.GetProperty("links")
			.GetProperty("self")[0]
			.GetProperty("href")
			.GetString() ?? url;

		return (true, prUrl);
	}
}