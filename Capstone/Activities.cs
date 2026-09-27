using Octokit;

namespace Capstone;

using MonthData = (DateTimeOffset month, int count);

public class Activities(string repoName, string repoOwner) : Analysis(repoName, repoOwner)
{

    private Task<List<MonthData>> GetMonthDataForPastYear(string request)
    {
        return GetCountPerMonth(
            request,
            new DateTime(DateTime.Today.Year - 1, DateTime.Today.Month, 1),
            new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddDays(-1)
        );
    }
    
    private Task<List<MonthData>> GetMonthDataForPastYear(Func<DateTime, Task<int>> callback)
    {
        return GetCountPerMonth(
            callback,
            new DateTime(DateTime.Today.Year - 1, DateTime.Today.Month, 1),
            new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddDays(-1)
        );
    }
    
    public Task<List<MonthData>> GetPRCountPerMonth(bool mergedOnly = false)
    {
        return GetMonthDataForPastYear("is:pr" + (mergedOnly? " is:merged" : ""));
    }


    public Task<List<MonthData>> GetIssueCountMonth(bool completedOnly = false)
    {
        return GetMonthDataForPastYear("is:issue" + (completedOnly? " reason:completed" : ""));
    }
    
    
    public async Task<List<MonthData>> GetReviewCountPerMonth(int totalSamples = -1)
    {
        List<MonthData> result = await GetMonthDataForPastYear(_ => Task.FromResult(0));

        // this is dumb
        var add = (DateTimeOffset month) => {
            int i = result.FindIndex(e => month.Month == e.month.Month);
            if (i >= 0) result[i] = (month, result[i].count + 1);
        };
        
        // this might wrongfully remove PR which were reviewed a full year
        // after their creation. However, it should greatly reduce wasted
        // github requests.
        var pullRequestSampleEarliestInclusionDate = result[0].month.AddYears(-1);
        var reviewInclusionLowerBound = result.First().month;
        var reviewInclusionUpperBound = result.Last().month.AddMonths(1);
        
        
        Random random = new(0x53686974);
        var prRequest = new PullRequestRequest
        {
            State = ItemStateFilter.All
        };
        var pullRequests = await Cache.GetIfCached(
            () => Client.PullRequest.GetAllForRepository(RepoOwner, RepoName, prRequest), $"{RepoName}AllPRs"
        );


        var filteredPullRequests = pullRequests.Where(pr => pr.CreatedAt >= pullRequestSampleEarliestInclusionDate)
            .OrderBy(_ => random.NextDouble());
        int samples = totalSamples == -1 ? filteredPullRequests.Count() : Math.Min(totalSamples, filteredPullRequests.Count());
        var sampledPullRequests = filteredPullRequests.Take(samples).ToList();
        
        
        ProgressBar bar = new(50, sampledPullRequests.Count());
        int progress = 0;
        foreach (var pr in sampledPullRequests)
        {
            var reviews = await Cache.GetIfCached(() => Client.PullRequest.Review.GetAll(RepoOwner, RepoName, pr.Number), $"{RepoName}-PR-{pr.Number}");
            foreach (var review in reviews)
            {
                if(review.SubmittedAt > reviewInclusionLowerBound && review.SubmittedAt <= reviewInclusionUpperBound) add(review.SubmittedAt);
            }

            progress++;
            bar.Update(progress);
        }

        if (totalSamples != -1)
        {
            for (int i = 0; i < result.Count; i++)
            {
                result[i] = (result[i].month, result[i].count * filteredPullRequests.Count() / sampledPullRequests.Count());
            }
        }

        return result;
    }


    public void PrintMonthlyCSV(List<MonthData> data)
    {
        Console.WriteLine();
        foreach (var tuple in data)
        {
            // use MM instead of MMM because MMM gives Sept instead of Sep which makes it not compatible
            // with Google sheets which I am using for graphing.
            Console.WriteLine($"{tuple.month.ToString("yyyy-MM")},{tuple.count}");
        }
    }

}