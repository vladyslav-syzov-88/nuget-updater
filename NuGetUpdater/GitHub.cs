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
			if (line.StartsWith("username="))
			{
				username = line["username=".Length..].Trim();
			}

			if (line.StartsWith("password="))
			{
				password = line["password=".Length..].Trim();
			}
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

	internal static async Task<bool> CheckAuthAsync()
	{
		try
		{
			var response = await Client.GetAsync($"{ApiBase}/user");
			return response.IsSuccessStatusCode;
		}
		catch
		{
			return false;
		}
	}

	internal static async Task<(bool Success, string PrUrl)> CreatePullRequestAsync(
		string repo,
		string title,
		string body,
		string fromBranch,
		string toBranch,
		IReadOnlySet<string> reviewers)
	{
		var prBody = new
		{
			title,
			body,
			head = fromBranch,
			@base = toBranch,
		};

		string url = $"{ApiBase}/repos/{_org}/{repo}/pulls";
		var response = await Client.PostAsJsonAsync(url, prBody);
		string content = await response.Content.ReadAsStringAsync();

		if (!response.IsSuccessStatusCode)
		{
			// 422 means a PR already exists for this head branch — find it and treat as success
			if (response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
			{
				string? existingUrl = await FindExistingPrAsync(repo, fromBranch);
				if (existingUrl is not null)
				{
					return (true, $"{existingUrl} (already existed)");
				}
			}

			return (false, $"HTTP {(int)response.StatusCode} {response.StatusCode}: {content}");
		}

		using var doc = JsonDocument.Parse(content);
		int prNumber = doc.RootElement.GetProperty("number").GetInt32();
		string prUrl = doc.RootElement.GetProperty("html_url").GetString() ?? url;

		if (reviewers is { Count: > 0 })
		{
			var reviewersBody = new { reviewers };
			string reviewersUrl = $"{ApiBase}/repos/{_org}/{repo}/pulls/{prNumber}/requested_reviewers";
			await Client.PostAsJsonAsync(reviewersUrl, reviewersBody);
		}

		return (true, prUrl);
	}

	private static async Task<string?> FindExistingPrAsync(string repo, string branch)
	{
		try
		{
			string url = $"{ApiBase}/repos/{_org}/{repo}/pulls?head={_org}:{branch}&state=open";
			var response = await Client.GetAsync(url);
			if (!response.IsSuccessStatusCode)
			{
				return null;
			}

			string content = await response.Content.ReadAsStringAsync();
			using var doc = JsonDocument.Parse(content);
			if (doc.RootElement.GetArrayLength() > 0)
			{
				return doc.RootElement[0].GetProperty("html_url").GetString();
			}
		}
		catch
		{
		}

		return null;
	}
}