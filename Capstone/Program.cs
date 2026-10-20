using Capstone;


//Activities activities = new("godot", "godotengine");

//activities.PrintMonthlyCSV(await activities.GetReviewCountPerMonth(1000));
//activities.PrintMonthlyCSV(await activities.GetIssueCountMonth());
//activities.PrintMonthlyCSV(await activities.GetPRCountPerMonth(true));


//var analysis = new Contributors("godot", "godotengine");
//var topTen = await analysis.GetTopN(10);
//analysis.PrintCSVTable(topTen, [
//    (
//        "number of authored pull request",
//        (login) => analysis.GetCount($"is:pr is:merged author:{login}")
//    ),
//    (
//        "number of reviewed pull requests",
//        (login) => analysis.GetCount($"is:pr reviewed-by:{login}")
//    ),
//    (
//        "number of authored (and completed) issues",
//        (login) => analysis.GetCount($"is:issue is:closed reason:completed author:{login}")
//    )
//]);


// maybe use a specific date for the final data collection?
DateTimeOffset analysisEnd = new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero);
DateTimeOffset analysisStart = analysisEnd.AddYears(-1);

ContributorHealth health = new("godot", "godotengine");

var contributors = await health.GetInvolvement(analysisStart, analysisEnd, includeReviews: true);
var thresholds = health.ClassifyContributors(contributors, analysisEnd);
ContributorHealth.PrintSummary(contributors, thresholds);
ContributorHealth.PrintCsv(contributors);