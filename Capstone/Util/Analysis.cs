using Octokit;

namespace Capstone;

using MonthData = (DateTimeOffset month, int count);

public class Analysis
{
    protected readonly string RepoName;
    protected readonly string RepoOwner;
    protected GitHubClient Client { get; private set; }

    

    public Analysis(string repoName, string repoOwner)
    {
        RepoName = repoName;
        RepoOwner = repoOwner;
        Client = new GitHubClient(new ProductHeaderValue("godotengine"));
        Env.AddAccessToken(Client);
    }

    public async Task<int> GetCount(string request)
    {
        return await Cache.GetIfCached(async () =>
        {
            var fullRequest = $"repo:{RepoOwner}/{RepoName} {request}";
            SearchIssuesRequest req = new(fullRequest);
            req.Page = 1;
            req.PerPage = 1;
            return (await Client.Search.SearchIssues(req)).TotalCount;
        }, $"{RepoName} {request}");
    }
    
    public Task<List<MonthData>> GetCountPerMonth(string request, DateTime startMonth, DateTime endMonth)
    {
        return GetCountPerMonth(
            async month => await GetCount($"{request} created:{month:yyyy-MM}"),
            startMonth,
            endMonth
        );
    }
    
    public async Task<List<MonthData>> GetCountPerMonth(Func<DateTime, Task<int>> callback, DateTime startMonth, DateTime endMonth)
    {
        List<MonthData> result = [];
        var month = new DateTime(startMonth.Year, startMonth.Month, 1);
        endMonth = new DateTime(endMonth.Year, endMonth.Month, 1);

        var i = 0;

        while (month <= endMonth && i <= 500)
        {
            result.Add((month, await callback(month)));
            month = month.AddMonths(1);

            i++;
        }
        return result;
    }
    
    // I would NOT recommend not using this
    protected async Task<List<Issue>> GetAllWithJankyHack(string request, DateTimeOffset lowerBound)
    {
        List<Issue> result = [];

        var fullRequest = $"repo:{RepoOwner}/{RepoName} {request} sort:created-asc";

        var upperBound = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ssZ");

        // hack to get around paging limitations
        while (true)
        {
            SearchIssuesRequest req = new($"{fullRequest} created:{lowerBound:yyyy-MM-ddTHH:mm:ssZ}..{upperBound}")
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
                    Console.WriteLine($"Encountered Rate Limit. Sleeping for {waitTime.TotalMilliseconds / 1000.0f:F3}s");
                    await Task.Delay(waitTime);
                }
            }
        }

        return result;
    }
    
    public void PrintCSVTable(List<string> logins, List<(string name, Func<string, Task<int>> eval)> metrics)
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
        Console.WriteLine();
        Console.WriteLine(result);
    }

    protected async Task<T> WithRateLimitRetry<T>(Func<Task<T>> action)
    {
        while (true)
        {
            try
            {
                return await action();
            }
            catch (RateLimitExceededException ex)
            {
                var waitTime = ex.Reset - DateTimeOffset.UtcNow;

                if (waitTime > TimeSpan.Zero)
                {
                    Console.WriteLine(
                        $"Rate limit reached. Waiting {waitTime.TotalSeconds:F0}s."
                    );

                    await Task.Delay(waitTime);
                }
            }
        }
    }
}