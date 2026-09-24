// using System.Collections.Immutable;
//
// namespace Capstone;
// using Octokit;
//
// public class Contributors
// {
//     public GitHubClient Client;
//     public string RepoName;
//     public string RepoOwner;
//     
//     public Contributors(bool test = false)
//     {
//         Client = new GitHubClient(new  ProductHeaderValue("godotengine"));
//         RepoName = test? "Font-Awesome" : "godot";
//         RepoOwner = test? "FortAwesome" : "godotengine";
//         Env.AddAccessToken(Client);
//     }
//
//     async public Task ContributorCount()
//     {
//         var request = new PullRequestRequest();
//         request.State = ItemStateFilter.Closed;
//         var pullRequests = await Cache.GetIfCached(() => Client.PullRequest.GetAllForRepository(RepoOwner, RepoName, request), $"{RepoName}_PRs");
//         var count = pullRequests.Where(x => x.Merged).Select(x => x.User.Id).Distinct().Count();
//         Console.WriteLine(count);
//     }
//
//     async public Task Top10Contributors()
//     {
//         var request = new PullRequestRequest();
//         request.State = ItemStateFilter.Closed;
//         
//         var pullRequests = await Cache.GetIfCached(() => Client.PullRequest.GetAllForRepository(RepoOwner, RepoName, request), $"{RepoName}_PRs");
//         // var issues = await Cache.GetIfCached(() => Client.Issue.GetAllForRepository(RepoOwner, RepoName), $"{RepoName}_issues");
//         var reviews = new List<PullRequestReview>();
//
//         var b = new ProgressBar(60, pullRequests.Count());
//         int i = 0;
//         foreach (var pr in pullRequests)
//         {
//             b.Update(i);
//             i++;
//             // int b = (barLength * i) / n;
//             // Console.Write($"\r[{new string('#', b)}{new string('-', barLength - b)}] ({(100.0f * i / n).ToString("00.00")}%)");
//             
//             // var reviews = await Client.PullRequest.Review.GetAll(RepoOwner, RepoName, pr.Number);
//             var prReviews = await Cache.GetIfCached(() => Client.PullRequest.Review.GetAll(RepoOwner, RepoName, pr.Number), $"${RepoName}-${pr.Id}-{pr.Number}");
//             reviews.AddRange(prReviews);
//         }
//                 
//
//         // var issues = await Cache.GetIfCached(() => Client.Issue.GetAllForRepository(RepoOwner, RepoName), $"{RepoName}_Issues");
//         
//         var PRsPerUser = pullRequests
//             .Where(pr => pr.Merged)
//             .GroupBy(pr => pr.User.Id);
//
//         // lol janky hack
//         var top10 = PRsPerUser.OrderByDescending(grp => grp.Count()).Select(pr => pr.First().User).Take(10);
//         
//         var metrics = new List<(string name, Func<User, int> cb)>
//         {
//             // ("N PRs\t", user => pullRequests.Count(pr => pr.Merged &&  pr.User.Id == user.Id)),
//             // ("N Issues", user => issues.Count(issue => issue.User.Id == user.Id)),
//             ("N Reviews", user => reviews.Count(review => review.User != null && review.User.Id == user.Id)),
//         };
//         
//         foreach (var user in top10)
//         {
//             Console.Write($"{user.Login}");
//             metrics.ToList().ForEach(metric => Console.WriteLine($"\t{metric.cb(user)}\t"));
//             // Console.WriteLine();
//         }
//         
//     }
//
//
//     async public Task getReviews(User user)
//     {
//         var request = new SearchIssuesRequest($"repo:{owner}/{repo} reviewed-by:{username}")
//         {
//             Type = IssueTypeQualifier.PR
//         };
//
//         Client.Search.SearchIssues();
//     }
//
//     public static async Task Main(string[] args)
//     {
//         var cont = new Contributors(false);
//
//         // .RateLimit.GetRateLimits();
//         
//         // await cont.ContributorCount();
//         await cont.Top10Contributors();
//     }
// }
//
