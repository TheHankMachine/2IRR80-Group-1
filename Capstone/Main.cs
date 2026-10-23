using Capstone;
using Capstone.Final;
using static Capstone.Final.SharedAccountFiltering;


//=====================================
// WHATEVER GABI IS DOING
//=====================================

// maybe use a specific date for the final data collection?
DateTimeOffset analysisEnd = new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero);
DateTimeOffset analysisStart = analysisEnd.AddYears(-1);

okto health = new("godot", "godotengine");

//var contributors = await health.GetInvolvement(analysisStart, analysisEnd, includeReviews: true);
//var thresholds = health.ClassifyContributors(contributors, analysisEnd);
//ContributorHealth.PrintSummary(contributors, thresholds);
//ContributorHealth.PrintCsv(contributors);

var contributors = await health.GetInvolvement(analysisStart, analysisEnd, includeReviews: true);

// Identify bots
BotFiltering.PrintBotAnalysis(contributors);

// Remove verified bots
var humanContributors = BotFiltering.FilterBots(contributors);

// Account sharing
IdentityFiltering identityFiltering = new("godot", "godotengine");

var sharedAccountCandidates = await identityFiltering.FindSharedAccountCandidates(humanContributors, analysisStart, analysisEnd);

identityFiltering.PrintCandidates(sharedAccountCandidates);

//=======================
// WHATEVER LORE IS DOING
//=======================

//FetchOptions fetchOptions = new FetchOptions
//{
//    From = new DateTimeOffset(2025, 10, 18, 0, 0, 0, TimeSpan.Zero),
//    To = new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero),
//    SampleSize = 1000,
//    FetchComments = false,
//    FetchReviewComments = false
//};
//RepositoryDataFetcher dataFetcher = new (fetchOptions);
//var data = await dataFetcher.FetchAsync();

//CsvExportOptions csvOptions = new CsvExportOptions();
//CsvExporter exporter = new CsvExporter(csvOptions);
//exporter.Export(data);