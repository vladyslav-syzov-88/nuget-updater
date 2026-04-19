namespace NuGetUpdater;

// ==============================================================================
//  CONFIGURATION — edit these values before running
// ==============================================================================

public record NuGetPackage(string Name, string Version = "");

public class Config
{
	public string JiraTicketUrl    { get; init; } = "https://telenor-ose.atlassian.net/browse/";
	public string JiraTicketId     { get; init; } = "T40TB-488";
	public string SemVerBump       { get; init; } = "major";   // patch | minor | major
	public string BaseBranch       { get; init; } = "development";
	public string BitbucketBaseUrl { get; init; } = "http://stash.iux.sonofon.dk:7990";

	public List<NuGetPackage> Packages { get; init; } =
	[
		new("Telenor.Api.Core.Web", "24.0.0.5"),
	];

	// Bitbucket usernames to add as reviewers on every PR
	public List<string> Reviewers { get; init; } = ["MYKO", "CATK", "VLOR", "TEAMNIK"];

	private string PackageSummary => Packages.Count == 1
		? Packages[0].Name
		: "NuGet packages";

	public string FeatureBranchName => Packages.Count == 1
		? $"feature/{JiraTicketId}-update-{Packages[0].Name.ToLower()}"
		: $"feature/{JiraTicketId}-update-nugets";

	public string CommitMessage     => $"{JiraTicketId} +semver: {SemVerBump} update {PackageSummary}";
	public string PullRequestTitle  => $"{JiraTicketId} Update {PackageSummary}";

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
		"http://stash.iux.sonofon.dk:7990/scm/osa/telenor.api.accountmanagement.service.git",
		// "http://stash.iux.sonofon.dk:7990/scm/osa/telenor.api.appointmentmanagement.service.git",
		// "http://stash.iux.sonofon.dk:7990/scm/osa/telenor.api.billmanagement.service.git",
		// "http://stash.iux.sonofon.dk:7990/scm/osa/telenor.api.communicationmanagement.service.git",
		// "http://stash.iux.sonofon.dk:7990/scm/osa/telenor.api.customermanagement.service.git",
		// "http://stash.iux.sonofon.dk:7990/scm/osa/telenor.api.partymanagement.service.git",
		// "http://stash.iux.sonofon.dk:7990/scm/osa/telenor.api.productcatalog.service.git",
		// "http://stash.iux.sonofon.dk:7990/scm/osa/telenor.api.productinventorymanagement.service.git",
		// "http://stash.iux.sonofon.dk:7990/scm/osa/telenor.api.productofferingqualification.service.git",
		// "http://stash.iux.sonofon.dk:7990/scm/osa/telenor.api.productorderingmanagement.service.git",
		// "http://stash.iux.sonofon.dk:7990/scm/osa/telenor.api.productorders.service.git",
		// "http://stash.iux.sonofon.dk:7990/scm/osa/telenor.api.producttemplates.service.git",
		// "http://stash.iux.sonofon.dk:7990/scm/osa/telenor.api.resourcemanagement.service.git",
		// "http://stash.iux.sonofon.dk:7990/scm/osa/telenor.api.stockmanagement.service.git",
		// "http://stash.iux.sonofon.dk:7990/scm/osa/telenor.api.troubleticketmanagement.service.git",
		// "http://stash.iux.sonofon.dk:7990/scm/osa/telenor.api.usermanagement.service.git",
	];
}