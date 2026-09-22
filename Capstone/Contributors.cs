namespace Capstone;
using Octokit;

public class Contributors
{
    public GitHubClient Client;
    public string RepoName;
    public string RepoOwner;
    
    public Contributors(bool test = false)
    {
        Client = new GitHubClient(new  ProductHeaderValue("godotengine"));
        RepoName = test? "Font-Awesome" : "godot";
        RepoOwner = test? "FortAwesome" : "godotengine";
        Env.AddAccessToken(Client);
    }

    async public Task ContributorCount()
    {
        var request = new PullRequestRequest();
        request.State = ItemStateFilter.Closed;
        var pullRequests = await Cache.GetIfCached(() => Client.PullRequest.GetAllForRepository(RepoOwner, RepoName, request), $"{RepoName}_PRs");
        var count = pullRequests.Where(x => x.Merged).Select(x => x.User).Distinct().Count();
        Console.WriteLine(count);
    }

    async public Task Top10Contributors()
    {
        var request = new PullRequestRequest();
        request.State = ItemStateFilter.Closed;
        var pullRequests = await Cache.GetIfCached(() => Client.PullRequest.GetAllForRepository(RepoOwner, RepoName), $"{RepoName}_PRs");
        var PRsPerUser = pullRequests.Where(x => x.Merged).GroupBy(x => x.User).Select(x => x.Count()).ToString();
        Console.WriteLine(PRsPerUser);
    }
    
    public static async Task Main(string[] args)
    {
        var cont = new Contributors(true);
        await cont.Top10Contributors();
    }
}

