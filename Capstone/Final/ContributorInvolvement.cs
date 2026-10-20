using Octokit;
using System;
using System.Collections.Generic;
using System.Text;

namespace Capstone
{
    /// <summary>
    /// stores the involvement metrics for one github account
    /// this class only stores data and ContributorHealth does the analysis!!!
    /// </summary>
    public class ContributorInvolvement
    {
        public required string Login { get; init; }
        // Activity during the analysis period
        public int PullRequestsAuthored { get; set; }
        public int MergedPullRequestsAuthored { get; set; }
        public int IssuesAuthored { get; set; }
        // Number of distinct pull requests reviewed
        public int PullRequestsReviewed { get; set; }
        // Total number of individual review events submitted
        public int ReviewEventsSubmitted { get; set; }
        // Used to measure how consistently somebody participates
        public HashSet<string> ActiveMonths { get; } = [];
        public DateTimeOffset? FirstObservedActivity { get; private set; }
        public DateTimeOffset? LastObservedActivity { get; private set; }
        public DateTimeOffset? FirstAuthoredActivity { get; private set; }
        public bool HadAuthoredActivityInLookback { get; set; }
        // Contributor classifications
        public bool IsNewcomer { get; set; }
        public bool IsDormant { get; set; }
        public bool IsFrequent { get; set; }
        public bool IsKey { get; set; }

        /// <summary>
        /// Simple activity metric used for contributor involvement
        ///
        /// One authored PR = 1
        /// One authored issue = 1
        /// One distinct reviewed PR = 1
        /// </summary>
        public int TotalActivity => PullRequestsAuthored + IssuesAuthored + PullRequestsReviewed;

        /// <summary>
        /// Number of different types of activity performed
        ///
        /// Possible types:
        /// 1. PR authoring
        /// 2. issue authoring
        /// 3. PR reviewing
        /// </summary>
        public int ActivityTypes => (PullRequestsAuthored > 0 ? 1 : 0) + (IssuesAuthored > 0 ? 1 : 0) + (PullRequestsReviewed > 0 ? 1 : 0);

        /// <summary>
        /// Record an authored PR or issue
        /// </summary>
        public void AddAuthoredActivity(DateTimeOffset date)
        {
            AddActivity(date);

            if (FirstAuthoredActivity == null || date < FirstAuthoredActivity) FirstAuthoredActivity = date;
        }

        /// <summary>
        /// Record any contributor activity
        /// </summary>
        public void AddActivity(DateTimeOffset date)
        {
            ActiveMonths.Add($"{date:yyyy-MM}");

            if (FirstObservedActivity == null || date < FirstObservedActivity) FirstObservedActivity = date;
            if (LastObservedActivity == null || date > LastObservedActivity) LastObservedActivity = date;
        }
    }
}
