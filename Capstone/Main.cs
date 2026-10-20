using Capstone;

// maybe use a specific date for the final data collection?
DateTimeOffset analysisEnd = new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero);
DateTimeOffset analysisStart = analysisEnd.AddYears(-1);

ContributorHealth health = new("godot", "godotengine");

var contributors = await health.GetInvolvement(analysisStart, analysisEnd, includeReviews: true);
var thresholds = health.ClassifyContributors(contributors, analysisEnd);
ContributorHealth.PrintSummary(contributors, thresholds);
ContributorHealth.PrintCsv(contributors);