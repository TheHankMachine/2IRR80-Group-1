namespace Capstone;
using Octokit;
using System.Text.Json;

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
        var pullRequests = await Client.PullRequest
            .GetAllForRepository(
                RepoOwner,
                RepoName,
                request);
        var count = pullRequests.Where(x => x.Merged).Select(x => x.User).Distinct().Count();
        Console.WriteLine(count);
    }
    
    public static async Task Main(string[] args)
    {
        var cont = new Contributors();
        await cont.ContributorCount();
    }
}

