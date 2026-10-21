using System.Net;
using Octokit;
using Octokit.Internal;

namespace Capstone;

/// <summary>
/// Settings for a fetch run.
/// </summary>
public record FetchOptions
{
    public string Owner { get; init; } = "godotengine";
    public string Repo { get; init; } = "godot";

    /// <summary>Only items CREATED at or after this instant are considered. null = no lower bound.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Only items CREATED before this instant are considered. null = no upper bound.</summary>
    public DateTimeOffset? To { get; init; }

    /// <summary>
    /// Total number of items (issues and PRs combined) to fetch from the period. null = all of them.
    /// Items are drawn uniformly at random from the period, so the issue/PR mix follows the repo's natural ratio.
    /// </summary>
    public int? SampleSize { get; init; }

    /// <summary>Seed for the random sampling so runs are reproducible. null = non-deterministic.</summary>
    public int? Seed { get; init; } = 42;

    /// <summary>Ignore existing cache files and re-download everything.</summary>
    public bool ForceCacheReload { get; init; } = false;

    /// <summary>Include issues. If false, issues are skipped and don't count toward SampleSize. [Sill consumes an API call tho]</summary>
    public bool IncludeIssues { get; init; } = true;

    /// <summary>Include pull requests. If false, PRs are skipped and don't count toward SampleSize. [Sill consumes an API call tho]</summary>
    public bool IncludePullRequests { get; init; } = true;

    /// <summary>PR reviews (approvals, change requests). [1+ calls per PR] </summary>
    public bool FetchReviews { get; init; } = true;

    /// <summary>Inline comments on a PR's diff. [1+ calls per PR]</summary>
    public bool FetchReviewComments { get; init; } = true;

    /// <summary>Conversation comments on issues and PRs. [1+ calls per item]</summary>
    public bool FetchComments { get; init; } = true;
}

/// <summary>A PR together with everything attached to it. Raw data, nothing precomputed.</summary>
public record PullRequestData(
    PullRequest PullRequest,
    List<PullRequestReview> Reviews,
    List<PullRequestReviewComment> ReviewComments, // inline comments on the diff
    List<IssueComment> Comments);                  // conversation comments on the PR

/// <summary>An issue together with its comments. Raw data, nothing precomputed.</summary>
public record IssueData(
    Issue Issue,
    List<IssueComment> Comments);

public record RepositoryDataset(
    FetchOptions Options,
    List<PullRequestData> PullRequests,
    List<IssueData> Issues);

/// <summary>
/// Fetches issues, PRs, reviews and comments for a repository.
///
/// Sampling works on item NUMBERS: issues and PRs share one number sequence per repo, and numbers
/// grow with creation time. We binary-search the number range that matches the time period, pick
/// random numbers from that range, and fetch only those items.
///
/// Caching: the cache stores the RAW JSON GitHub returned (one file per object) and turns it into
/// Octokit objects with Octokit's own deserializer, the same path a normal API call takes.
///
/// We never serialize Octokit models ourselves, because Octokit's serializer is a big chud and
/// fails to serialize good when nested stuff is introduced
/// </summary>
public class RepositoryDataFetcher
{
    // How many consecutive numbers we try when a number is missing (deleted/transferred) during the binary search.
    private const int MaxProbeGap = 25;
    private const int PageSize = 100;

    private static readonly SimpleJsonSerializer Json = new();

    private readonly GitHubClient _client;
    private readonly FetchOptions _options;
    
    public RepositoryDataFetcher(FetchOptions options)
    {
        _client = new GitHubClient(new ProductHeaderValue("capstone-project-health"));
        Env.AddAccessToken(_client);
        _options = options;
    }

    // ------------------------------------------------------------------
    // Main entry point
    // ------------------------------------------------------------------

    public async Task<RepositoryDataset> FetchAsync()
    {
        Console.WriteLine("Determining item number range for the period...");
        int max = await GetHighestNumberAsync();
        int first = _options.From == null ? 1 : await FindFirstNumberCreatedAtOrAfterAsync(_options.From.Value, max);
        int endExclusive = _options.To == null ? max + 1 : await FindFirstNumberCreatedAtOrAfterAsync(_options.To.Value, max);

        var candidates = Enumerable.Range(first, Math.Max(0, endExclusive - first)).ToArray();
        int target = Math.Min(_options.SampleSize ?? int.MaxValue, candidates.Length);
        Console.WriteLine($"Numbers {first}..{endExclusive - 1} fall in the period ({candidates.Length} candidates), fetching {target}.");

        var prs = new List<PullRequestData>();
        var issues = new List<IssueData>();
        if (target == 0) return new RepositoryDataset(_options, prs, issues);

        var rng = _options.Seed.HasValue ? new Random(_options.Seed.Value) : new Random();
        bool shuffle = _options.SampleSize != null;
        var bar = new ProgressBar(40, target);
        bar.Update(0);

        int accepted = 0;
        for (int i = 0; i < candidates.Length && accepted < target; i++)
        {
            // Lazy Fisher-Yates: only shuffles as far as we actually draw, so no "load all then pick".
            if (shuffle)
            {
                int j = rng.Next(i, candidates.Length);
                (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
            }

            int number = candidates[i];

            Issue issue;
            try
            {
                issue = await GetIssueAsync(number);
            }
            catch (ApiException e) when (IsMissing(e))
            {
                continue; // deleted / transferred number, draw another one
            }

            // The binary-searched bounds are approximate (numbers aren't perfectly chronological), so re-check.
            if (_options.From != null && issue.CreatedAt < _options.From) continue;
            if (_options.To != null && issue.CreatedAt >= _options.To) continue;

            if (issue.PullRequest != null)
            {
                if (!_options.IncludePullRequests) continue;

                var pr = await GetPullRequestAsync(number);
                var reviews = _options.FetchReviews ? await GetReviewsAsync(number) : [];
                var reviewComments = _options.FetchReviewComments ? await GetReviewCommentsAsync(number) : [];
                var comments = _options.FetchComments ? await GetCommentsAsync(number) : [];
                prs.Add(new PullRequestData(pr, reviews, reviewComments, comments));
            }
            else
            {
                if (!_options.IncludeIssues) continue;

                var comments = _options.FetchComments ? await GetCommentsAsync(number) : [];
                issues.Add(new IssueData(issue, comments));
            }

            accepted++;
            bar.Update(accepted);
        }

        Console.WriteLine();
        prs.Sort((a, b) => a.PullRequest.Number.CompareTo(b.PullRequest.Number));
        issues.Sort((a, b) => a.Issue.Number.CompareTo(b.Issue.Number));
        return new RepositoryDataset(_options, prs, issues);
    }

    // ------------------------------------------------------------------
    // Cached single-object getters (public so other classes can reuse the cache)
    // ------------------------------------------------------------------

    /// <summary>Works for both issues and PRs (a PR has Issue.PullRequest != null).</summary>
    public Task<Issue> GetIssueAsync(int number) =>
        GetObjectAsync<Issue>($"{Prefix}_issue{number}", $"{RepoUrl}/issues/{number}");

    public Task<PullRequest> GetPullRequestAsync(int number) =>
        GetObjectAsync<PullRequest>($"{Prefix}_pr{number}", $"{RepoUrl}/pulls/{number}");

    public Task<List<PullRequestReview>> GetReviewsAsync(int prNumber) =>
        GetListAsync<PullRequestReview>($"{Prefix}_pr{prNumber}_reviews", $"{RepoUrl}/pulls/{prNumber}/reviews");

    public Task<List<PullRequestReviewComment>> GetReviewCommentsAsync(int prNumber) =>
        GetListAsync<PullRequestReviewComment>($"{Prefix}_pr{prNumber}_reviewcomments", $"{RepoUrl}/pulls/{prNumber}/comments");

    /// <summary>Conversation comments; the same endpoint serves issues and PRs.</summary>
    public Task<List<IssueComment>> GetCommentsAsync(int number) =>
        GetListAsync<IssueComment>($"{Prefix}_issue{number}_comments", $"{RepoUrl}/issues/{number}/comments");

    // ------------------------------------------------------------------
    // Raw JSON fetching + caching
    // ------------------------------------------------------------------

    private string Prefix => $"{_options.Owner}_{_options.Repo}";
    private string RepoUrl => $"repos/{_options.Owner}/{_options.Repo}";

    private async Task<T> GetObjectAsync<T>(string cacheKey, string url)
    {
        string raw = await Cache.GetRawIfCached(() => FetchRawObjectAsync<T>(url), cacheKey, _options.ForceCacheReload);
        return Json.Deserialize<T>(raw);
    }

    private async Task<List<T>> GetListAsync<T>(string cacheKey, string url)
    {
        string raw = await Cache.GetRawIfCached(() => FetchRawListAsync<T>(url), cacheKey, _options.ForceCacheReload);
        return Json.Deserialize<List<T>>(raw);
    }

    /// <summary>GETs one object and returns the raw JSON body. T is only used so Octokit can parse the response.</summary>
    private async Task<string> FetchRawObjectAsync<T>(string url)
    {
        var response = await WithRateLimit(() =>
            _client.Connection.Get<T>(new Uri(url, UriKind.Relative), new Dictionary<string, string>()));
        return response.HttpResponse.Body as string ?? "{}";
    }

    /// <summary>GETs every page of a list endpoint and returns ONE raw JSON array containing all items.</summary>
    private async Task<string> FetchRawListAsync<T>(string url)
    {
        var parts = new List<string>();
        Uri? next = new Uri(url, UriKind.Relative);
        var parameters = new Dictionary<string, string> { ["per_page"] = PageSize.ToString() };

        while (next != null)
        {
            var uri = next;
            var query = parameters;
            var response = await WithRateLimit(() => _client.Connection.Get<List<T>>(uri, query));

            // strip the surrounding [ ] of each page so the pages can be joined into one array
            string page = (response.HttpResponse.Body as string ?? "[]").Trim();
            if (page.Length > 2)
            {
                string inner = page[1..^1].Trim();
                if (inner.Length > 0) parts.Add(inner);
            }

            // later pages: the "next" link already contains its query string
            parameters = new Dictionary<string, string>();
            next = response.HttpResponse.ApiInfo.Links.TryGetValue("next", out var nextUri) ? nextUri : null;
        }

        return "[" + string.Join(",", parts) + "]";
    }

    // ------------------------------------------------------------------
    // Number range discovery
    // ------------------------------------------------------------------

    /// <summary>Highest issue/PR number in the repo (one call, not cached since it changes).</summary>
    private async Task<int> GetHighestNumberAsync()
    {
        var latest = await WithRateLimit(() => _client.Issue.GetAllForRepository(
            _options.Owner, _options.Repo,
            new RepositoryIssueRequest
            {
                State = ItemStateFilter.All,
                SortProperty = IssueSort.Created,
                SortDirection = SortDirection.Descending
            },
            new ApiOptions { PageSize = 1, PageCount = 1, StartPage = 1 }));

        if (latest.Count == 0) throw new InvalidOperationException("Repository has no issues or PRs.");
        return latest[0].Number;
    }

    /// <summary>
    /// Binary search for the smallest number whose item was created at or after <paramref name="when"/>.
    /// Returns max + 1 if there is none. Each probe is cached, so repeated runs are free.
    /// </summary>
    private async Task<int> FindFirstNumberCreatedAtOrAfterAsync(DateTimeOffset when, int max)
    {
        int lo = 1, hi = max + 1;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            var probe = await ProbeAtOrAboveAsync(mid, max);
            if (probe == null || probe.CreatedAt >= when) hi = mid;
            else lo = mid + 1;
        }
        return lo;
    }

    /// <summary>Returns the first existing item with number >= the given one (skips deleted numbers).</summary>
    private async Task<Issue?> ProbeAtOrAboveAsync(int number, int max)
    {
        for (int n = number; n <= Math.Min(max, number + MaxProbeGap); n++)
        {
            try
            {
                return await GetIssueAsync(n);
            }
            catch (ApiException e) when (IsMissing(e))
            {
                // try next number
            }
        }
        return null;
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static bool IsMissing(ApiException e) =>
        e.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone;

    private const int MaxTransientRetries = 8;

    /// <summary>
    /// Runs an Octokit call and retries on problems that are not our fault:
    ///  - primary rate limit: sleeps until the reset time
    ///  - secondary rate limit ("abuse" detection): sleeps for the time GitHub asks for
    ///  - 5xx errors (e.g. 503 Service Unavailable), network errors and timeouts: exponential backoff
    /// Anything else (404, 401, ...) is thrown to the caller as before.
    /// </summary>
    private static async Task<T> WithRateLimit<T>(Func<Task<T>> call)
    {
        int transientFailures = 0;
        while (true)
        {
            try
            {
                return await call();
            }
            catch (RateLimitExceededException e)
            {
                var wait = e.GetRetryAfterTimeSpan() + TimeSpan.FromSeconds(5);
                if (wait < TimeSpan.FromSeconds(5)) wait = TimeSpan.FromSeconds(30);
                Console.WriteLine($"\nRate limit hit. Waiting {wait.TotalMinutes:0.0} min until reset...");
                await Task.Delay(wait);
            }
            catch (AbuseException e)
            {
                var wait = TimeSpan.FromSeconds((e.RetryAfterSeconds ?? 60) + 5);
                Console.WriteLine($"\nSecondary rate limit hit. Waiting {wait.TotalSeconds:0} s...");
                await Task.Delay(wait);
            }
            catch (Exception e) when (IsTransient(e) && ++transientFailures <= MaxTransientRetries)
            {
                // 2, 4, 8, 16, 32, 60, 60, 60 seconds
                var wait = TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, transientFailures)));
                Console.WriteLine($"\nTransient error ({e.GetType().Name}: {e.Message}). Retry {transientFailures}/{MaxTransientRetries} in {wait.TotalSeconds:0} s...");
                await Task.Delay(wait);
            }
        }
    }

    private static bool IsTransient(Exception e) => e switch
    {
        RateLimitExceededException or AbuseException => false, // handled separately above
        ApiException api => (int)api.StatusCode >= 500,
        HttpRequestException => true,
        TaskCanceledException => true, // HttpClient timeouts surface this way
        _ => false
    };
}