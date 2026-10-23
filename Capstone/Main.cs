using Capstone;

// // maybe use a specific date for the final data collection?
// DateTimeOffset analysisEnd = new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero);
// DateTimeOffset analysisStart = analysisEnd.AddYears(-1);
//
// ContributorHealth health = new("godot", "godotengine");
//
// var contributors = await health.GetInvolvement(analysisStart, analysisEnd, includeReviews: true);
// var thresholds = health.ClassifyContributors(contributors, analysisEnd);
// ContributorHealth.PrintSummary(contributors, thresholds);
// ContributorHealth.PrintCsv(contributors);

FetchOptions fetchOptions = new FetchOptions
{
    From = new DateTimeOffset(2025, 10, 18, 0, 0, 0, TimeSpan.Zero),
    To = new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero),
    FetchComments = true,
    FetchReviewComments = true
};
RepositoryDataFetcher dataFetcher = new (fetchOptions);
var data = await dataFetcher.FetchAsync();

CsvExporter exporter = new CsvExporter("2025-oct to 2026-oct");
exporter.Export(data);
