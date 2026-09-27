using Capstone;


Activities activities = new("godot", "godotengine");

activities.PrintMonthlyCSV(await activities.GetReviewCountPerMonth(1000));
activities.PrintMonthlyCSV(await activities.GetIssueCountMonth());
activities.PrintMonthlyCSV(await activities.GetPRCountPerMonth(true));


var analysis = new Contributors("godot", "godotengine");
var topTen = await analysis.GetTopN(10);
analysis.PrintCSVTable(topTen, [
    (
        "number of authored pull request",
        (login) => analysis.GetCount($"is:pr is:merged author:{login}")
    ),
    (
        "number of reviewed pull requests",
        (login) => analysis.GetCount($"is:pr reviewed-by:{login}")
    ),
    (
        "number of authored (and completed) issues",
        (login) => analysis.GetCount($"is:issue is:closed reason:completed author:{login}")
    )
]);