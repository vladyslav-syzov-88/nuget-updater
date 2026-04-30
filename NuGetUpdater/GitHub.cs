using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace NuGetUpdater;

/// <summary>GitHub REST API v3 client — handles authentication and pull request creation for the configured organisation.</summary>
internal static class GitHub
{
	private const string ApiBase = "https://api.github.com";
	private static readonly HttpClient Client = new();
	private static string _org = "";

	internal static (string Username, string Password) ResolveCredentials()
	{
		string input = "protocol=https\nhost=github.com\n\n";

		var psi = new ProcessStartInfo("git", "credential fill")
		{
			UseShellExecute = false,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
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

	internal static void Configure(string org, string token)
	{
		_org = org;
		Client.DefaultRequestHeaders.Authorization =
			new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
		Client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
		Client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
		Client.DefaultRequestHeaders.UserAgent.ParseAdd("NuGetUpdater/1.0");
	}

	internal static bool CheckAuth()
	{
		try
		{
			var response = Client.GetAsync($"{ApiBase}/user").GetAwaiter().GetResult();
			return response.IsSuccessStatusCode;
		}
		catch
		{
			return false;
		}
	}

	internal static (bool Success, string PrUrl) CreatePullRequest(
		string repo,
		string title,
		string body,
		string fromBranch,
		string toBranch,
		IEnumerable<string> reviewers)
	{
		var prBody = new
		{
			title,
			body,
			head = fromBranch,
			@base = toBranch,
		};

		string url = $"{ApiBase}/repos/{_org}/{repo}/pulls";
		var response = Client.PostAsJsonAsync(url, prBody).GetAwaiter().GetResult();
		string content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

		if (!response.IsSuccessStatusCode)
		{
			// 422 means a PR already exists for this head branch — find it and treat as success
			if (response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
			{
				string? existingUrl = FindExistingPr(repo, fromBranch);
				if (existingUrl is not null)
					return (true, $"{existingUrl} (already existed)");
			}

			return (false, $"HTTP {(int)response.StatusCode} {response.StatusCode}: {content}");
		}

		using var doc = JsonDocument.Parse(content);
		int prNumber = doc.RootElement.GetProperty("number").GetInt32();
		string prUrl = doc.RootElement.GetProperty("html_url").GetString() ?? url;

		var reviewerList = reviewers.ToList();
		if (reviewerList.Count > 0)
		{
			var reviewersBody = new { reviewers = reviewerList.ToArray() };
			string reviewersUrl = $"{ApiBase}/repos/{_org}/{repo}/pulls/{prNumber}/requested_reviewers";
			Client.PostAsJsonAsync(reviewersUrl, reviewersBody).GetAwaiter().GetResult();
		}

		return (true, prUrl);
	}

	private static string? FindExistingPr(string repo, string branch)
	{
		try
		{
			string url = $"{ApiBase}/repos/{_org}/{repo}/pulls?head={_org}:{branch}&state=open";
			var response = Client.GetAsync(url).GetAwaiter().GetResult();
			if (!response.IsSuccessStatusCode) return null;
			string content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
			using var doc = JsonDocument.Parse(content);
			if (doc.RootElement.GetArrayLength() > 0)
				return doc.RootElement[0].GetProperty("html_url").GetString();
		}
		catch
		{
		}

		return null;
	}
}