using Capstone;

var analysis = new Analysis("godot", "godotengine");
var top = await analysis.GetTopN(10);
var table = analysis.GetCSVTable(top, [
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

Console.WriteLine(table);