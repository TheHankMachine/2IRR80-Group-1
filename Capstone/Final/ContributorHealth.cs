using Octokit;

namespace Capstone
{
    public class HealthSearchItem
    {
        public long Id { get; set; }
        public int Number { get; set; }
        public string Login { get; set; } = "";
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }

    public class HealthReviewItem
    {
        public string Login { get; set; } = "";
        public DateTimeOffset SubmittedAt { get; set; }
    }

    /// <summary>
    /// Stores the thresholds that were used to classify contributors
    /// </summary>
    public record ContributorThresholds(
        int FrequentActivityThreshold,
        int KeyActivityThreshold,
        int FrequentMinimumActiveMonths,
        int KeyMinimumActiveMonths,
        int KeyMinimumActivityTypes,
        int NewcomerWindowDays,
        int DormancyWindowDays,
        int HistoricalLookbackMonths
    );

    public class ContributorHealth(string repoName, string repoOwner) : Analysis(repoName, repoOwner)
    {
        // GENERAL HELPERS

        private ContributorInvolvement GetOrCreate(Dictionary<string, ContributorInvolvement> contributors, string login)
        {
            if (!contributors.TryGetValue(login, out var contributor))
            {
                contributor = new ContributorInvolvement { Login = login };
                contributors[login] = contributor;
            }
            return contributor;
        }

        // INVOLVEMENT ANALYSIS

        public async Task<List<ContributorInvolvement>> GetInvolvement(
            DateTimeOffset analysisStart,
            DateTimeOffset analysisEnd,
            bool includeReviews = true,
            int historicalLookbackMonths = 12)
        {
            if (analysisEnd <= analysisStart)
            {
                throw new ArgumentException("analysisEnd must be after analysisStart.");
            }

            DateTimeOffset historyStart = analysisStart.AddMonths(-historicalLookbackMonths);
            Dictionary<string, ContributorInvolvement> contributors = new(StringComparer.OrdinalIgnoreCase);

            //checks if a "newcomer" has actually authored something a while back
            HashSet<string> priorAuthors = new(StringComparer.OrdinalIgnoreCase);

            // PULL REQUEST AUTHORSHIP
            Console.WriteLine();
            Console.WriteLine($"Fetching pull requests from " + $"{historyStart:yyyy-MM-dd} to " + $"{analysisEnd:yyyy-MM-dd}...");

            var pullRequests = await GetPullRequestsCreatedBetween(historyStart, analysisEnd, mergedOnly: false);

            Console.WriteLine($"Finished fetching relevant pull requests: " + $"{pullRequests.Count}");

            // Search separately for merged PRs during the analysis period so that we can record merged PR authorship
            Console.WriteLine();
            Console.WriteLine("Fetching merged pull requests for the analysis period...");

            var mergedPullRequests = await GetPullRequestsCreatedBetween(analysisStart, analysisEnd, mergedOnly: true);
            HashSet<int> mergedPullRequestNumbers = mergedPullRequests.Select(pr => pr.Number).ToHashSet();
            Console.WriteLine($"Finished fetching merged pull requests: " + $"{mergedPullRequests.Count}");

            foreach (var pr in pullRequests)
            {
                string login = pr.Login;

                // History lookback
                if (pr.CreatedAt >= historyStart && pr.CreatedAt < analysisStart)
                {
                    priorAuthors.Add(login);
                    continue;
                }

                // Current analysis period
                if (pr.CreatedAt < analysisStart || pr.CreatedAt >= analysisEnd) continue;

                var contributor = GetOrCreate(contributors, login);
                contributor.PullRequestsAuthored++;

                if (mergedPullRequestNumbers.Contains(pr.Number)) contributor.MergedPullRequestsAuthored++;
                contributor.AddAuthoredActivity(pr.CreatedAt);
            }

            // ISSUE AUTHORSHIP
            Console.WriteLine();
            Console.WriteLine($"Fetching issues from " + $"{historyStart:yyyy-MM-dd} to " + $"{analysisEnd:yyyy-MM-dd}...");
            var issues = await GetIssuesBetween(historyStart, analysisEnd);
            Console.WriteLine($"Finished fetching issues: " + $"{issues.Count}");

            foreach (var issue in issues)
            {
                string login = issue.Login;

                // History lookback
                if (issue.CreatedAt >= historyStart && issue.CreatedAt < analysisStart)
                {
                    priorAuthors.Add(login);
                    continue;
                }

                // Current analysis period
                if (issue.CreatedAt < analysisStart || issue.CreatedAt >= analysisEnd) continue;

                var contributor = GetOrCreate(contributors, login);
                contributor.IssuesAuthored++;
                contributor.AddAuthoredActivity(issue.CreatedAt);
            }

            // PULL REQUEST REVIEWS
            if (includeReviews)
            {
                Console.WriteLine();
                Console.WriteLine(
                    "Finding pull requests that may contain reviews..."
                );

                var reviewCandidates = await GetPullRequestsUpdatedBetween(analysisStart, analysisEnd);

                Console.WriteLine($"Review candidate PRs: " + $"{reviewCandidates.Count}");

                ProgressBar bar = new(50, reviewCandidates.Count);
                int progress = 0;

                foreach (var pr in reviewCandidates)
                {
                    var reviews = await GetReviewsForPullRequest(pr.Number);
                    var relevantReviews = reviews.Where(review => review.SubmittedAt >= analysisStart && review.SubmittedAt < analysisEnd).ToList();


                    /*
                     * A person may submit several review events on one PR
                     * For our main involvement metric this counts as one reviewed PR, but ReviewEventsSubmitted = 2
                     */
                    foreach (var reviewerGroup in relevantReviews.GroupBy(review => review.Login, StringComparer.OrdinalIgnoreCase))
                    {
                        string login = reviewerGroup.Key;
                        var contributor = GetOrCreate(contributors, login);
                        contributor.PullRequestsReviewed++;
                        contributor.ReviewEventsSubmitted += reviewerGroup.Count();

                        foreach (var review in reviewerGroup)
                        {
                            contributor.AddActivity(review.SubmittedAt);
                        }
                    }

                    progress++;
                    bar.Update(progress);
                }

                Console.WriteLine();
                Console.WriteLine("Finished collecting reviews.");
            }

            // History lookback flag

            foreach (var contributor in contributors.Values)
            {
                contributor.HadAuthoredActivityInLookback = priorAuthors.Contains(contributor.Login);
            }

            return contributors.Values.OrderByDescending(c => c.TotalActivity).ThenBy(c => c.Login).ToList();
        }

        // CONTRIBUTOR CLASSIFICATION

        public ContributorThresholds ClassifyContributors(
            List<ContributorInvolvement> contributors,
            DateTimeOffset analysisEnd,
            double frequentPercentile = 0.75,
            double keyPercentile = 0.95,
            int frequentMinimumActiveMonths = 3,
            int keyMinimumActiveMonths = 6,
            int keyMinimumActivityTypes = 2,
            int newcomerWindowDays = 90,
            int dormancyWindowDays = 90,
            int historicalLookbackMonths = 12)
        {
            if (contributors.Count == 0) throw new ArgumentException("No contributors were supplied.");

            var activityValues = contributors.Select(c => c.TotalActivity).Where(activity => activity > 0)
                    .OrderBy(activity => activity).ToList();
            int frequentThreshold = Percentile(activityValues, frequentPercentile);
            int keyThreshold = Percentile(activityValues, keyPercentile);

            DateTimeOffset newcomerBoundary = analysisEnd.AddDays(-newcomerWindowDays);
            DateTimeOffset dormancyBoundary = analysisEnd.AddDays(-dormancyWindowDays);

            foreach (var contributor in contributors)
            {
                // KEY CONTRIBUTOR

                /* this means:
                 * - top 5% activity
                 * - active in the last 6 months
                 * - at least 2 differnet activity types
                 */

                contributor.IsKey = contributor.TotalActivity >= keyThreshold && contributor.ActiveMonths.Count >= keyMinimumActiveMonths
                    && contributor.ActivityTypes >= keyMinimumActivityTypes;


                // FREQUENT CONTRIBUTOR

                /* this means:
                 * - active
                 * - repeated participation
                 */

                contributor.IsFrequent = contributor.TotalActivity >= frequentThreshold
                    && contributor.ActiveMonths.Count >= frequentMinimumActiveMonths;


                // NEWCOMER

                /* this means:
                 * - first authored activity is in the last 90 days
                 * - no authored PR or issue was found during the history lookback (12 months prior to this year)
                 */

                contributor.IsNewcomer = contributor.FirstAuthoredActivity != null
                    && contributor.FirstAuthoredActivity >= newcomerBoundary && !contributor.HadAuthoredActivityInLookback;


                // DORMANT

                /* this means:
                 * - participated during the analysis year
                 * - has not participated in the lat 90 days
                 */

                contributor.IsDormant = contributor.LastObservedActivity != null && contributor.LastObservedActivity < dormancyBoundary;
            }

            return new ContributorThresholds(
                FrequentActivityThreshold: frequentThreshold,
                KeyActivityThreshold: keyThreshold,
                FrequentMinimumActiveMonths: frequentMinimumActiveMonths,
                KeyMinimumActiveMonths: keyMinimumActiveMonths,
                KeyMinimumActivityTypes: keyMinimumActivityTypes,
                NewcomerWindowDays: newcomerWindowDays,
                DormancyWindowDays: dormancyWindowDays,
                HistoricalLookbackMonths: historicalLookbackMonths
            );
        }

        // OUTPUT

        public static void PrintSummary(IReadOnlyCollection<ContributorInvolvement> contributors, ContributorThresholds thresholds)
        {
            Console.WriteLine();
            Console.WriteLine("===== CONTRIBUTOR HEALTH SUMMARY =====");
            Console.WriteLine($"Observed contributors: " + $"{contributors.Count}");
            Console.WriteLine($"Key contributors:      " + $"{contributors.Count(c => c.IsKey)}");
            Console.WriteLine($"Frequent contributors: " + $"{contributors.Count(c => c.IsFrequent)}");
            Console.WriteLine($"Newcomers:             " + $"{contributors.Count(c => c.IsNewcomer)}");
            Console.WriteLine($"Dormant contributors:  " + $"{contributors.Count(c => c.IsDormant)}");
            Console.WriteLine();
            Console.WriteLine($"Frequent activity threshold: " + $"{thresholds.FrequentActivityThreshold}");
            Console.WriteLine($"Key activity threshold:      " + $"{thresholds.KeyActivityThreshold}");
            Console.WriteLine($"Frequent min active months:  " + $"{thresholds.FrequentMinimumActiveMonths}");
            Console.WriteLine($"Key min active months:       " + $"{thresholds.KeyMinimumActiveMonths}");
            Console.WriteLine($"Key min activity types:      " + $"{thresholds.KeyMinimumActivityTypes}");
            Console.WriteLine($"Newcomer window:             " + $"{thresholds.NewcomerWindowDays} days");
            Console.WriteLine($"Dormancy window:             " + $"{thresholds.DormancyWindowDays} days");
            Console.WriteLine($"Historical lookback:         " + $"{thresholds.HistoricalLookbackMonths} months");
        }

        public static void PrintCsv(IEnumerable<ContributorInvolvement> contributors)
        {
            Console.WriteLine();
            Console.WriteLine(
                "login," +
                "prs_authored," +
                "merged_prs_authored," +
                "issues_authored," +
                "prs_reviewed," +
                "review_events," +
                "total_activity," +
                "activity_types," +
                "active_months," +
                "first_authored_activity," +
                "first_observed_activity," +
                "last_observed_activity," +
                "had_authored_activity_in_lookback," +
                "is_newcomer," +
                "is_dormant," +
                "is_frequent," +
                "is_key"
            );

            foreach (var contributor in contributors.OrderByDescending(c => c.TotalActivity).ThenBy(c => c.Login))
            {
                Console.WriteLine(
                    $"{contributor.Login}," +
                    $"{contributor.PullRequestsAuthored}," +
                    $"{contributor.MergedPullRequestsAuthored}," +
                    $"{contributor.IssuesAuthored}," +
                    $"{contributor.PullRequestsReviewed}," +
                    $"{contributor.ReviewEventsSubmitted}," +
                    $"{contributor.TotalActivity}," +
                    $"{contributor.ActivityTypes}," +
                    $"{contributor.ActiveMonths.Count}," +
                    $"{FormatDate(contributor.FirstAuthoredActivity)}," +
                    $"{FormatDate(contributor.FirstObservedActivity)}," +
                    $"{FormatDate(contributor.LastObservedActivity)}," +
                    $"{contributor.HadAuthoredActivityInLookback}," +
                    $"{contributor.IsNewcomer}," +
                    $"{contributor.IsDormant}," +
                    $"{contributor.IsFrequent}," +
                    $"{contributor.IsKey}"
                );
            }
        }

        #region PR-Issue-Review Collection
        // PULL REQUEST COLLECTION - DATE CREATED

        private async Task<List<HealthSearchItem>>
            GetPullRequestsCreatedBetween(DateTimeOffset start, DateTimeOffset end, bool mergedOnly)
        {
            List<HealthSearchItem> result = [];
            DateTimeOffset monthStart = FirstDayOfMonth(start);

            while (monthStart < end)
            {
                DateTimeOffset monthEnd = monthStart.AddMonths(1);
                DateTimeOffset queryStart = monthStart < start ? start : monthStart;
                DateTimeOffset queryEnd = monthEnd > end ? end : monthEnd;

                Console.WriteLine($"  PRs: " + $"{queryStart:yyyy-MM-dd} to " + $"{queryEnd:yyyy-MM-dd}" + (mergedOnly ? " [merged]" : ""));

                string cacheKey = $"{RepoName}-health-prs-v3-" + $"{(mergedOnly ? "merged" : "all")}-" + $"{queryStart:yyyyMMddHHmmss}-" + $"{queryEnd:yyyyMMddHHmmss}";

                var monthPullRequests = await Cache.GetIfCached(() =>
                            SearchPullRequestsCreatedRange(queryStart, queryEnd, mergedOnly), cacheKey);

                result.AddRange(monthPullRequests);
                monthStart = monthEnd;
            }

            /*
             * Monthly GitHub ranges are inclusive at both ends? so something exactly at a month boundary can appear twice 
             * Remove duplicates here.
             */
            return result.GroupBy(item => item.Id).Select(group => group.First()).ToList();
        }

        private async Task<List<HealthSearchItem>>
            SearchPullRequestsCreatedRange(DateTimeOffset start, DateTimeOffset end, bool mergedOnly)
        {
            string mergedQualifier = mergedOnly ? "is:merged " : "";
            string query =
                $"repo:{RepoOwner}/{RepoName} " +
                $"is:pr " +
                mergedQualifier +
                $"created:{FormatGitHubDate(start)}" +
                $"..{FormatGitHubDate(end)} " +
                $"sort:created-asc";

            return await SearchHealthItems(
                query,
                $"pull requests created between " +
                $"{start:yyyy-MM-dd} and " +
                $"{end:yyyy-MM-dd}"
            );
        }

        // PULL REQUEST COLLECTION - DATE UPDATED

        private async Task<List<HealthSearchItem>>
        GetPullRequestsUpdatedBetween(DateTimeOffset start, DateTimeOffset end)
        {
            List<HealthSearchItem> result = [];

            PullRequestRequest request = new()
            {
                State = ItemStateFilter.All,
                SortProperty = PullRequestSort.Updated,
                SortDirection = SortDirection.Descending
            };

            const int pageSize = 100;
            int page = 1;
            bool reachedBeforeStart = false;

            while (!reachedBeforeStart)
            {
                Console.WriteLine($"  Fetching review-candidate PR page {page}...");

                ApiOptions options = new() { PageSize = pageSize, PageCount = 1, StartPage = page };

                var pullRequests = await WithRateLimitRetry(() =>
                    Client.PullRequest.GetAllForRepository(RepoOwner, RepoName, request, options));

                if (pullRequests.Count == 0) break;

                foreach (var pr in pullRequests)
                {
                    //once we encounter a PR whose latest update is before analysisStart, stop
                    if (pr.UpdatedAt < start)
                    {
                        reachedBeforeStart = true;
                        break;
                    }

                    if (pr.User == null || String.IsNullOrWhiteSpace(pr.User.Login)) continue;

                    result.Add(new HealthSearchItem
                    {
                        Id = pr.Id,
                        Number = pr.Number,
                        Login = pr.User.Login,
                        CreatedAt = pr.CreatedAt,
                        UpdatedAt = pr.UpdatedAt
                    }
                    );
                }

                //Fewer than 100 results = this was the final available page
                if (pullRequests.Count < pageSize) break;

                page++;
            }

            result = result.GroupBy(pr => pr.Id).Select(group => group.First()).ToList();
            Console.WriteLine($"Found {result.Count} PRs that may contain " + $"reviews since {start:yyyy-MM-dd}.");

            return result;
        }

        // ISSUE COLLECTION
        private async Task<List<HealthSearchItem>>
            GetIssuesBetween(DateTimeOffset start, DateTimeOffset end)
        {
            List<HealthSearchItem> result = [];

            DateTimeOffset monthStart = FirstDayOfMonth(start);


            while (monthStart < end)
            {
                DateTimeOffset monthEnd = monthStart.AddMonths(1);
                DateTimeOffset queryStart = monthStart < start ? start : monthStart;
                DateTimeOffset queryEnd = monthEnd > end ? end : monthEnd;

                Console.WriteLine($"  Issues: " + $"{queryStart:yyyy-MM-dd} to " + $"{queryEnd:yyyy-MM-dd}");
                string cacheKey = $"{RepoName}-health-issues-v3-" + $"{queryStart:yyyyMMddHHmmss}-" + $"{queryEnd:yyyyMMddHHmmss}";

                var monthIssues = await Cache.GetIfCached(() =>
                            SearchIssuesRange(queryStart, queryEnd), cacheKey);

                result.AddRange(monthIssues);

                monthStart = monthEnd;
            }

            return result.GroupBy(item => item.Id).Select(group => group.First()).ToList();
        }

        private async Task<List<HealthSearchItem>>
            SearchIssuesRange(DateTimeOffset start, DateTimeOffset end)
        {
            string query =
                $"repo:{RepoOwner}/{RepoName} " +
                $"is:issue " +
                $"created:{FormatGitHubDate(start)}" +
                $"..{FormatGitHubDate(end)} " +
                $"sort:created-asc";

            return await SearchHealthItems(
                query,
                $"issues created between " +
                $"{start:yyyy-MM-dd} and " +
                $"{end:yyyy-MM-dd}"
            );
        }

        // GITHUB SEARCH

        /// <summary>
        /// Executes a GitHub issue/PR search and converts Octokit's objects 
        /// </summary>
        private async Task<List<HealthSearchItem>>
            SearchHealthItems(string query, string description)
        {
            List<HealthSearchItem> result = [];

            int retrievedItems = 0;

            for (int page = 1; page <= 10; page++)
            {
                SearchIssuesRequest request = new(query) { Page = page, PerPage = 100 };

                var response = await WithRateLimitRetry(() => Client.Search.SearchIssues(request));

                //fail if there are more than 1000 responses (GitHub limit)
                if (page == 1 && response.TotalCount > 1000)
                {
                    throw new InvalidOperationException(
                        $"{response.TotalCount} results for " + $"{description}. " + $"This exceeds GitHub's 1000-result search");
                }

                foreach (var item in response.Items)
                {
                    if (item.User == null || String.IsNullOrWhiteSpace(item.User.Login)) continue;

                    result.Add(new HealthSearchItem
                    {
                        Id = item.Id,
                        Number = item.Number,
                        Login = item.User.Login,
                        CreatedAt = item.CreatedAt,
                        UpdatedAt = item.UpdatedAt ?? item.CreatedAt
                    }
                    );
                }

                retrievedItems += response.Items.Count;

                if (response.Items.Count < 100 || retrievedItems >= response.TotalCount) break;
            }

            return result;
        }


        // REVIEW COLLECTION

        private async Task<List<HealthReviewItem>>
            GetReviewsForPullRequest(int pullRequestNumber)
        {
            string cacheKey = $"{RepoName}-health-reviews-v3-" + $"{pullRequestNumber}";

            return await Cache.GetIfCached(async () =>
            {
                var reviews = await WithRateLimitRetry(() =>
                            Client.PullRequest.Review.GetAll(RepoOwner, RepoName, pullRequestNumber));

                List<HealthReviewItem> result = [];

                foreach (var review in reviews)
                {
                    if (review.User == null || String.IsNullOrWhiteSpace(review.User.Login)) continue;

                    result.Add(new HealthReviewItem
                    {
                        Login = review.User.Login,
                        SubmittedAt = review.SubmittedAt
                    }
                    );
                }

                return result;
            }, cacheKey);
        }
        
        #endregion
        
        // RATE LIMIT HANDLING :(

        private async Task<T>
            WithRateLimitRetry<T>(Func<Task<T>> action)
        {
            while (true)
            {
                try
                {
                    return await action();
                }
                catch (RateLimitExceededException ex)
                {
                    TimeSpan waitTime = ex.Reset - DateTimeOffset.UtcNow;

                    if (waitTime > TimeSpan.Zero)
                    {
                        waitTime = waitTime.Add(TimeSpan.FromSeconds(2));
                        Console.WriteLine($"GitHub rate limit reached. " + $"Waiting " + $"{waitTime.TotalSeconds:F0} seconds...");
                        await Task.Delay(waitTime);
                    }
                    else await Task.Delay(TimeSpan.FromSeconds(2));
                }
            }
        }

        // STATISTIC HELPERS

        private static int Percentile(IReadOnlyList<int> sortedValues, double percentile)
        {
            if (sortedValues.Count == 0) return 0;
            if (percentile < 0 || percentile > 1) throw new ArgumentOutOfRangeException(nameof(percentile));

            int index = (int)Math.Ceiling(percentile * sortedValues.Count) - 1;
            index = Math.Clamp(index, 0, sortedValues.Count - 1);
            return sortedValues[index];
        }

        private static DateTimeOffset
            FirstDayOfMonth(DateTimeOffset date)
        {
            return new DateTimeOffset(date.Year, date.Month, 1, 0, 0, 0, TimeSpan.Zero);
        }

        private static string FormatGitHubDate(DateTimeOffset date)
        {
            return date.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
        }

        private static string FormatDate(DateTimeOffset? date)
        {
            return date?.ToString("yyyy-MM-dd") ?? "";
        }
    }
}