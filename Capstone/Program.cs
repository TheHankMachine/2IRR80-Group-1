using Capstone;


Activities activities = new("godot", "godotengine");

var c = await activities.GetCountPerMonth(
    "is:pr is:merged",
    new DateTime(2014, 2, 1),
    // new DateTime(2015, 3, 1)
    DateTime.Today
);

foreach (var group in c)
{
    Console.WriteLine($"{group.month.ToString("yyyy-MM")},{group.count}");
}



// DateTimeOffset d = ;


// var analysis = new Contributors("godot", "godotengine");
// var top = await analysis.GetTopN(10);
// var table = analysis.GetCSVTable(top, [
//     (
//         "number of authored pull request",
//         (login) => analysis.GetCount($"is:pr is:merged author:{login}")
//     ),
//     (
//         "number of reviewed pull requests",
//         (login) => analysis.GetCount($"is:pr reviewed-by:{login}")
//     ),
//     (
//         "number of authored (and completed) issues",
//         (login) => analysis.GetCount($"is:issue is:closed reason:completed author:{login}")
//     )
// ]);
//
// Console.WriteLine(table);