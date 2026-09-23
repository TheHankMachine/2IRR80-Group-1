//
// using System.Data;
// using Capstone;
// using Octokit;
//
// var client = new GitHubClient(new  ProductHeaderValue("godotengine"));
// Env.AddAccessToken(client);
//
// // commit.Commit.Committer.Date
//
//
// var commits = await Cache.GetIfCached(
//      () => client.Repository.Commit.GetAll("vitejs", "vite"),
//      "vitejs_commits", 
//      false
// );
//
//
// Func<DateTimeOffset, int> getQuarter = (d) => (d.Month - 1) / 3 + 1;
// Func<DateTimeOffset, string> getKey = (d) => "Y" + d.Year + " Q" + getQuarter(d);
//
// var quarters = commits.GroupBy(commit => getKey(commit.Commit.Committer.Date));
// var random = new Random(42);
//
// foreach (var quarter in quarters)
// {
//      var c = quarter
//           .OrderBy(_ => random.NextDouble())
//           .Take(5)
//           .Select(commit => commit.Commit.Message.Split(":")[0])
//           .GroupBy(a => a);
//
//      Console.WriteLine($"{quarter.Key}");
//      foreach (var a in c)
//      {
//           Console.WriteLine($"{a.Key} : {a.Count()} ");
//      }
// }
//
//
//
//
//
//
//
//
//
//
//
//
// // int n = 30;
// // var random = new Random();
// // var b = commits.OrderBy(_ => random.NextDouble()).Take(n).Select(commit => commit.Commit.Message.Split(":")[0]).GroupBy(a => a);
// //
// // foreach (var grouping in b)
// // {
// //      Console.WriteLine(grouping.Key + "\t\t\t" + grouping.Count() + "\t\t\t" + ((float) grouping.Count() / n) + "%");
// // }
//
//
//
//
// //
// // Console.WriteLine($"{r.Count} issues found");
// //
// // long endTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
// //
// // Console.WriteLine($"got all issues in {endTime - startTime}ms");

// var r = new Random();
//

using Capstone;

// int n = 1000;
// int barLength = 50;
//
// var b = new ProgressBar(50);
//
// for (int i = 0; i < n; i++)
// {
//     b.Update((double) i / n);
//     Thread.Sleep(100);
// }

// Console.Write("— -");