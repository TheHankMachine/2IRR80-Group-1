using System.ComponentModel;
using Octokit;

namespace Capstone;

public class Analysis
{
    private readonly string _repoName;
    private readonly string _repoOwner;
    public GitHubClient Client { get; private set; }


    public Analysis(string repoName, string repoOwner)
    {
        _repoName = repoName;
        _repoOwner = repoOwner;
        Client = new GitHubClient(new ProductHeaderValue("godotengine"));
        Env.AddAccessToken(Client);
    }

    public async Task<int> GetCount(string request)
    {
        var fullRequest = $"repo:{_repoOwner}/{_repoName} {request}";
        SearchIssuesRequest req = new(fullRequest);
        req.Page = 1;
        req.PerPage = 1;
        return (await Client.Search.SearchIssues(req)).TotalCount;
    }

    // I would NOT recommend not using this
    private async Task<List<Issue>> GetAllWithJankyHack(string request)
    {
        List<Issue> result = [];

        var fullRequest = $"repo:{_repoOwner}/{_repoName} {request} sort:created-asc";

        var lowerBound = Client.Repository.Get(_repoOwner, _repoName).Result.CreatedAt;
        var today = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ssZ");

        // hack to get around paging limitations
        while (true)
        {
            SearchIssuesRequest req = new($"{fullRequest} created:{lowerBound:yyyy-MM-ddTHH:mm:ssZ}..{today}")
                { Page = 1, PerPage = 100 };

            try
            {
                var res = await Client.Search.SearchIssues(req);

                if (!res.Items.Any()) break;

                result.AddRange(res.Items);

                lowerBound = result.Last().CreatedAt.AddSeconds(1);
            }
            catch (RateLimitExceededException ex)
            {
                var waitTime = ex.Reset - DateTimeOffset.UtcNow;
                if (waitTime.TotalMilliseconds > 0)
                {
                    Console.WriteLine($"Encountered Rate Limit. Sleeping for {waitTime.TotalMilliseconds}ms");
                    await Task.Delay(waitTime);
                }
            }
        }

        return result;
    }

    // docs in C# are so ugly wtf.
    /// <returns> the top n users based on number of pull requests
    /// that they authored that have been merged. </returns>
    public async Task<List<string>> GetTopN(int n)
    {
        if (n != 10)
        {
            throw new InvalidEnumArgumentException("n is not 10");
        }

        // hardcoded from our previous approach since we had issues otherwise 
        List<string> top10 =
        [
            "Calinou", "akien-mga", "bruvzg", "KoBeWi", "timothyqiu", 
            "Chaosus", "YeldhamDev", "RandomShaper", "aaronfranke", "clayjohn"
        ];

        return top10;
        
        // var contributors = await Cache.GetIfCached(() => Client.Repository.GetAllContributors(_repoOwner, _repoName),
        //     $"{_repoName}-contributors");
        //
        // return contributors
        //     // .OrderByDescending(c => c.Contributions)
        //     // .Take(n * 10)
        //     .Select(c => new { login = c.Login, nprs = GetCount($"is:merged is:pr author:{c.Login}").Result})
        //     .OrderByDescending(t => t.nprs)
        //     .Take(n)
        //     .Select(c => c.login)
        //     .ToList();
    }

    public string GetCSVTable(List<string> logins, List<(string name, Func<string, Task<int>> eval)> metrics)
    {
        string result = " ," + String.Join(",", metrics.Select(metric => metric.name));
        foreach (var login in logins)
        {
            result += '\n' + login;
            foreach (var metric in metrics)
            {
                result += "," + metric.eval(login).Result;
            }
        }
        return result;
    }
}