namespace NuGetUpdater;

// ==============================================================================
//  CONFIGURATION — edit these values before running
// ==============================================================================

/// <summary>A NuGet package name and optional pinned version to update.</summary>
public record NuGetPackage(string Name, string Version = "");

/// <summary>All user-editable settings for a single run. Edit this file before executing the tool.</summary>
public class Config
{
	public string JiraTicketUrl { get; init; } = "https://telenor-ose.atlassian.net/browse/";
	public string JiraTicketId { get; init; } = "BMRT-9470";
	public string SemVerBump { get; init; } = "major";   // patch | minor | major
	public string BaseBranch { get; init; } = "develop";
	public string GitHubOrg { get; init; } = "TelenorInternal";

	public List<NuGetPackage> Packages { get; init; } =
	[
		new("Telenor.Api.Models", "37.7.0.10"),
	];

	// GitHub usernames to add as reviewers on every PR
	public List<string> Reviewers { get; init; } =
	[
		// keep this empty because they are added automatically as code owners.
		// "Nick-Nilga",
		// "kate-krivko",
		// "VLOR-telenor-dk",
		// "Mary-Nikiforova"
	];

	private string PackageSummary => Packages.Count == 1
		? Packages[0].Name
		: "NuGet packages";

	public string FeatureBranchName => Packages.Count == 1
		? $"feature/{JiraTicketId}-update-{Packages[0].Name.ToLower()}-add-product-order-intent"
		: $"feature/{JiraTicketId}-update-nugets";

	// public string CommitMessage     => $"{JiraTicketId} +semver: {SemVerBump} update {PackageSummary}";
	public string CommitMessage => $"{JiraTicketId} update {PackageSummary} (add Product Order Intent)";
	public string PullRequestTitle => $"{JiraTicketId} Update {PackageSummary} (add Product Order Intent)";

	public string PrBody =>
		$"""
		## Summary

		{string.Join("\n\t\t", Packages.Select(p =>
			$"- Updated NuGet package `{p.Name}`" +
			(string.IsNullOrEmpty(p.Version)
				? " to the latest available version"
				: $" to version `{p.Version}`")))}

		## Jira

		[{JiraTicketId}]({JiraTicketUrl}{JiraTicketId})

		## Test plan

		- [x] Solution builds successfully
		- [x] Unit tests pass

		Automated via NuGetUpdater
		""";

	public List<string> Repositories { get; init; } =
	[
		// "https://github.com/TelenorInternal/dk-s11020-telenor-api-product-offering-qualification-service.git",
		// "https://github.com/TelenorInternal/dk-s11020-telenor-api-product-ordering-management-service.git",
		// "https://github.com/TelenorInternal/dk-s11020-telenor-api-product-orders-service.git",
		"https://github.com/TelenorInternal/dk-s11020-telenor-api-user-management-service.git",
	];
}