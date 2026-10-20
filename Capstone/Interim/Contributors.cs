using System.ComponentModel;

namespace Capstone;
using Octokit;

public class Contributors(string repoName, string repoOwner) : Analysis(repoName, repoOwner) 
{
    
    public async Task<int> GetContributorCount()
    {
         var request = new PullRequestRequest();
         request.State = ItemStateFilter.Closed;
         var pullRequests = await Cache.GetIfCached(() => Client.PullRequest.GetAllForRepository(RepoOwner, RepoName, request), $"{RepoName}_PRs");
         return pullRequests.Where(x => x.Merged).Select(x => x.User.Id).Distinct().Count();
    }
    
    
    // docs in C# are so ugly wtf.
    /// <returns> the top n users based on number of pull requests
    /// that they authored that have been merged. </returns>
    public async Task<List<string>> GetTopN(int n)
    {
        if (n != 10)
        {
            throw new ArgumentException("n is not 10");
        }

        // hardcoded from our previous approach since we had issues otherwise 
        List<string> top10 =
        [
            "Calinou", "akien-mga", "bruvzg", "KoBeWi", "timothyqiu", 
            "Chaosus", "YeldhamDev", "RandomShaper", "aaronfranke", "clayjohn"
        ];

        return top10;
        
        // // this was the original approach which we haven't run in a while because of rate limit issues
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
}