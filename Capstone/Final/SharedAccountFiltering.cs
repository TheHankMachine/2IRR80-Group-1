using Octokit;
using System.Text.RegularExpressions;

namespace Capstone.Final
{
    internal class SharedAccountFiltering
    {
        public class IdentityCommitEvidence
        {
            public required string Login { get; init; }
            public required string Sha { get; init; }
            public required string AuthorName { get; init; }
            public required string AuthorEmail { get; init; }
            public DateTimeOffset AuthoredAt { get; init; }
        }


        // Normalized commit identity (used for comparison)
        public class NormalizedIdentityEvidence
        {
            public required string Login { get; init; }
            public required string Sha { get; init; }
            public required string OriginalAuthorName { get; init; }
            public required string OriginalAuthorEmail { get; init; }
            public required string CanonicalAuthorName { get; init; }
            public required string CanonicalAuthorEmail { get; init; }
            public required string EmailIdentityKey { get; init; }
            public bool WasChangedByMailmap { get; init; }
        }


        // Mailmap

        internal class MailmapIdentity
        {
            public required string CanonicalName { get; init; }
            public required string CanonicalEmail { get; init; }
        }


        // Result for one possible shared account

        public class SharedAccountCandidate
        {
            public required string Login { get; init; }
            public int CommitCount { get; init; }
            // Names after mailmap normalization
            public required List<string> AuthorNames { get; init; }
            public required List<string> AuthorEmails { get; init; }
            // Actual identities used for comparison.
            public required List<string> EmailIdentityKeys { get; init; }
            public required List<string> ExampleCommitShas { get; init; }
            public int MailmapNormalizedCommits { get; init; }
            public int DistinctAuthorNames => AuthorNames.Count;
            public int DistinctEmailIdentities => EmailIdentityKeys.Count;
            public bool IsCandidate => DistinctAuthorNames >= 2 && DistinctEmailIdentities >= 2;
        }


        // Analysis
        public class IdentityFiltering : Analysis
        {
            /*
             * GitHub's modern private noreply email format is:
             *
             * ID+USERNAME@users.noreply.github.com
             *
             * The numeric ID remains useful when the username changes.
             */
            private static readonly Regex GitHubNoreplyRegex =
                new(
                    @"^(?<id>[0-9]+)\+[^@]+@users\.noreply\.github\.com$",
                    RegexOptions.IgnoreCase
                    | RegexOptions.Compiled
                );


            /*
             * Extracts:
             *
             * Name <email>
             *
             * components from .mailmap lines.
             */
            private static readonly Regex MailmapIdentityRegex =
                new(
                    @"(?<name>[^<>]*?)\s*<(?<email>[^<>]+)>",
                    RegexOptions.Compiled
                );


            public IdentityFiltering(
                string repoName,
                string repoOwner)
                : base(repoName, repoOwner)
            {
            }


            // Main entry point
            public async Task<List<SharedAccountCandidate>>
                FindSharedAccountCandidates(IEnumerable<ContributorInvolvement> contributors, DateTimeOffset analysisStart, DateTimeOffset analysisEnd)
            {
                HashSet<string> allowedLogins = contributors.Select(contributor => contributor.Login).ToHashSet(StringComparer.OrdinalIgnoreCase);

                Console.WriteLine();
                Console.WriteLine("===== SHARED ACCOUNT ANALYSIS =====");


                // Raw commit data
                Console.WriteLine("Collecting commit identity evidence...");
                var rawEvidence = await GetCommitIdentityEvidence(analysisStart, analysisEnd);
                rawEvidence = rawEvidence.Where(commit => allowedLogins.Contains(commit.Login)).ToList();
                Console.WriteLine($"Commit identity records: " + $"{rawEvidence.Count}");

                // Load .mailmap
                Console.WriteLine("Loading repository .mailmap...");
                var mailmap = await GetMailmap();

                Console.WriteLine($"Mailmap email aliases loaded: " + $"{mailmap.Count}");

                // Normalize
                var normalized = rawEvidence.Select(commit => NormalizeCommit(commit, mailmap)).ToList();

                int mailmapChanges = normalized.Count(
                        commit =>
                            commit.WasChangedByMailmap
                    );


                Console.WriteLine(
                    $"Commits normalized using .mailmap: " +
                    $"{mailmapChanges}"
                );


                // --------------------------------------------------------------------
                // STEP 4: GROUP BY GITHUB LOGIN
                // --------------------------------------------------------------------

                var candidates =
                    normalized
                        .GroupBy(
                            commit =>
                                commit.Login,
                            StringComparer.OrdinalIgnoreCase
                        )
                        .Select(
                            group =>
                                BuildCandidate(group)
                        )
                        .Where(
                            candidate =>
                                candidate.IsCandidate
                        )
                        .OrderByDescending(
                            candidate =>
                                candidate.DistinctAuthorNames
                        )
                        .ThenByDescending(
                            candidate =>
                                candidate.DistinctEmailIdentities
                        )
                        .ThenByDescending(
                            candidate =>
                                candidate.CommitCount
                        )
                        .ToList();


                Console.WriteLine(
                    $"Shared-account candidates after normalization: " +
                    $"{candidates.Count}"
                );


                return candidates;
            }


            // ========================================================================
            // BUILD ONE CANDIDATE
            // ========================================================================

            private static SharedAccountCandidate
                BuildCandidate(
                    IGrouping<
                        string,
                        NormalizedIdentityEvidence
                    > group)
            {
                /*
                 * Names are compared after basic normalization so:
                 *
                 * "Dery Almas"
                 * "  Dery Almas  "
                 *
                 * do not count separately.
                 */

                var names =
                    group
                        .Select(
                            commit =>
                                commit.CanonicalAuthorName
                                    .Trim()
                        )
                        .Where(
                            name =>
                                !String.IsNullOrWhiteSpace(
                                    name
                                )
                        )
                        .GroupBy(
                            name =>
                                NormalizeNameKey(name)
                        )
                        .Select(
                            nameGroup =>
                                nameGroup.First()
                        )
                        .OrderBy(
                            name => name
                        )
                        .ToList();


                /*
                 * Display emails.
                 *
                 * These aren't directly used for candidate counting,
                 * because two GitHub noreply addresses can actually
                 * represent the same account ID.
                 */
                var emails =
                    group
                        .Select(
                            commit =>
                                commit.CanonicalAuthorEmail
                        )
                        .Where(
                            email =>
                                !String.IsNullOrWhiteSpace(
                                    email
                                )
                        )
                        .Distinct(
                            StringComparer.OrdinalIgnoreCase
                        )
                        .OrderBy(
                            email => email
                        )
                        .ToList();


                /*
                 * This is what we use for actual identity counting.
                 */
                var emailIdentityKeys =
                    group
                        .Select(
                            commit =>
                                commit.EmailIdentityKey
                        )
                        .Where(
                            identity =>
                                !String.IsNullOrWhiteSpace(
                                    identity
                                )
                        )
                        .Distinct(
                            StringComparer.OrdinalIgnoreCase
                        )
                        .OrderBy(
                            identity => identity
                        )
                        .ToList();


                var exampleShas =
                    group
                        .Select(
                            commit =>
                                commit.Sha
                        )
                        .Distinct()
                        .Take(5)
                        .ToList();


                return new SharedAccountCandidate
                {
                    Login =
                        group.Key,

                    CommitCount =
                        group.Count(),

                    AuthorNames =
                        names,

                    AuthorEmails =
                        emails,

                    EmailIdentityKeys =
                        emailIdentityKeys,

                    ExampleCommitShas =
                        exampleShas,

                    MailmapNormalizedCommits =
                        group.Count(
                            commit =>
                                commit.WasChangedByMailmap
                        )
                };
            }


            // ========================================================================
            // NORMALIZE ONE COMMIT
            // ========================================================================

            private static NormalizedIdentityEvidence
                NormalizeCommit(
                    IdentityCommitEvidence commit,
                    IReadOnlyDictionary<
                        string,
                        MailmapIdentity
                    > mailmap)
            {
                string originalName =
                    commit.AuthorName.Trim();

                string originalEmail =
                    NormalizeEmail(
                        commit.AuthorEmail
                    );


                string canonicalName =
                    originalName;

                string canonicalEmail =
                    originalEmail;

                bool changedByMailmap =
                    false;


                // --------------------------------------------------------------------
                // APPLY .mailmap
                // --------------------------------------------------------------------

                if (
                    !String.IsNullOrWhiteSpace(
                        originalEmail
                    )
                    &&
                    mailmap.TryGetValue(
                        originalEmail,
                        out MailmapIdentity? mapped
                    )
                )
                {
                    canonicalEmail =
                        mapped.CanonicalEmail;


                    if (
                        !String.IsNullOrWhiteSpace(
                            mapped.CanonicalName
                        )
                    )
                    {
                        canonicalName =
                            mapped.CanonicalName;
                    }


                    changedByMailmap =
                        !String.Equals(
                            originalEmail,
                            canonicalEmail,
                            StringComparison.OrdinalIgnoreCase
                        )
                        ||
                        !String.Equals(
                            originalName,
                            canonicalName,
                            StringComparison.OrdinalIgnoreCase
                        );
                }


                // --------------------------------------------------------------------
                // NORMALIZE GITHUB NOREPLY ACCOUNT IDS
                // --------------------------------------------------------------------

                string emailIdentityKey =
                    GetEmailIdentityKey(
                        canonicalEmail
                    );


                return new NormalizedIdentityEvidence
                {
                    Login =
                        commit.Login,

                    Sha =
                        commit.Sha,

                    OriginalAuthorName =
                        originalName,

                    OriginalAuthorEmail =
                        originalEmail,

                    CanonicalAuthorName =
                        canonicalName,

                    CanonicalAuthorEmail =
                        canonicalEmail,

                    EmailIdentityKey =
                        emailIdentityKey,

                    WasChangedByMailmap =
                        changedByMailmap
                };
            }


            // ========================================================================
            // EMAIL IDENTITY
            // ========================================================================

            private static string GetEmailIdentityKey(
                string email)
            {
                string normalized =
                    NormalizeEmail(email);


                if (
                    String.IsNullOrWhiteSpace(
                        normalized
                    )
                )
                {
                    return "";
                }


                /*
                 * Example:
                 *
                 * 260066004+anyone4551@users.noreply.github.com
                 * 260066004+carrotofpower@users.noreply.github.com
                 *
                 * become:
                 *
                 * github-id:260066004
                 */

                Match match =
                    GitHubNoreplyRegex.Match(
                        normalized
                    );


                if (match.Success)
                {
                    string id =
                        match.Groups["id"].Value;


                    return
                        $"github-id:{id}";
                }


                return
                    $"email:{normalized}";
            }


            // ========================================================================
            // MAILMAP
            // ========================================================================

            private async Task<
                Dictionary<string, MailmapIdentity>
            > GetMailmap()
            {
                string text =
                    await FetchMailmapText();


                return ParseMailmap(
                    text
                );
            }


            private async Task<string> FetchMailmapText()
            {
                try
                {
                    var contents =
                        await WithRateLimitRetry(
                            () =>
                                Client.Repository.Content
                                    .GetAllContents(
                                        RepoOwner,
                                        RepoName,
                                        ".mailmap"
                                    )
                        );

                    if (contents.Count == 0)
                    {
                        Console.WriteLine(
                            "No .mailmap file found."
                        );

                        return "";
                    }

                    /*
                     * Octokit.NET already decodes the Base64 content
                     * returned by GitHub's Contents API.
                     *
                     * Therefore RepositoryContent.Content is the actual
                     * text of the .mailmap file and must NOT be decoded
                     * again with Convert.FromBase64String().
                     */
                    return contents[0].Content ?? "";
                }
                catch (NotFoundException)
                {
                    Console.WriteLine(
                        "Repository has no .mailmap file."
                    );

                    return "";
                }
            }


            private static Dictionary<
                string,
                MailmapIdentity
            > ParseMailmap(
                string text)
            {
                Dictionary<
                    string,
                    MailmapIdentity
                > aliases =
                    new(
                        StringComparer.OrdinalIgnoreCase
                    );


                if (
                    String.IsNullOrWhiteSpace(
                        text
                    )
                )
                {
                    return aliases;
                }


                string[] lines =
                    text.Split(
                        [
                            "\r\n",
                    "\n",
                    "\r"
                        ],
                        StringSplitOptions.None
                    );


                foreach (string rawLine in lines)
                {
                    string line =
                        rawLine.Trim();


                    if (
                        String.IsNullOrWhiteSpace(
                            line
                        )
                        ||
                        line.StartsWith("#")
                    )
                    {
                        continue;
                    }


                    MatchCollection matches =
                        MailmapIdentityRegex.Matches(
                            line
                        );


                    if (matches.Count == 0)
                        continue;


                    /*
                     * In .mailmap, the first identity is the canonical
                     * identity.
                     */

                    string canonicalName =
                        matches[0]
                            .Groups["name"]
                            .Value
                            .Trim();


                    string canonicalEmail =
                        NormalizeEmail(
                            matches[0]
                                .Groups["email"]
                                .Value
                        );


                    if (
                        String.IsNullOrWhiteSpace(
                            canonicalEmail
                        )
                    )
                    {
                        continue;
                    }


                    MailmapIdentity canonical =
                        new()
                        {
                            CanonicalName =
                                canonicalName,

                            CanonicalEmail =
                                canonicalEmail
                        };


                    /*
                     * Also map the canonical email to itself.
                     *
                     * This lets .mailmap lines that only correct the
                     * author's displayed name still normalize properly.
                     */

                    aliases[canonicalEmail] =
                        canonical;


                    /*
                     * Any additional <email> entries on the line are
                     * aliases of the first/canonical identity.
                     */

                    for (
                        int i = 1;
                        i < matches.Count;
                        i++
                    )
                    {
                        string aliasEmail =
                            NormalizeEmail(
                                matches[i]
                                    .Groups["email"]
                                    .Value
                            );


                        if (
                            String.IsNullOrWhiteSpace(
                                aliasEmail
                            )
                        )
                        {
                            continue;
                        }


                        aliases[aliasEmail] =
                            canonical;
                    }
                }


                return aliases;
            }


            // ========================================================================
            // COMMIT COLLECTION
            // ========================================================================

            private async Task<
                List<IdentityCommitEvidence>
            > GetCommitIdentityEvidence(
                DateTimeOffset analysisStart,
                DateTimeOffset analysisEnd)
            {
                /*
                 * Keep v1 here.
                 *
                 * Your existing cache contains the RAW commit evidence,
                 * and that data has not changed.
                 *
                 * The new normalization happens AFTER loading it.
                 *
                 * This means you should not need to download the ~9000
                 * commits again.
                 */
                string cacheKey =
                    $"{RepoName}-identity-commits-v1-" +
                    $"{analysisStart:yyyyMMdd}-" +
                    $"{analysisEnd:yyyyMMdd}";


                return await Cache.GetIfCached(
                    () =>
                        FetchCommitIdentityEvidence(
                            analysisStart,
                            analysisEnd
                        ),
                    cacheKey
                );
            }


            private async Task<
                List<IdentityCommitEvidence>
            > FetchCommitIdentityEvidence(
                DateTimeOffset analysisStart,
                DateTimeOffset analysisEnd)
            {
                DateTimeOffset inclusiveEnd =
                    analysisEnd.AddSeconds(-1);


                CommitRequest request =
                    new()
                    {
                        Since =
                            analysisStart,

                        Until =
                            inclusiveEnd
                    };


                Console.WriteLine(
                    $"Fetching commits from " +
                    $"{analysisStart:yyyy-MM-dd} to " +
                    $"{inclusiveEnd:yyyy-MM-dd}..."
                );


                var commits =
                    await WithRateLimitRetry(
                        () =>
                            Client.Repository.Commit
                                .GetAll(
                                    RepoOwner,
                                    RepoName,
                                    request
                                )
                    );


                Console.WriteLine(
                    $"Fetched {commits.Count} commits."
                );


                List<IdentityCommitEvidence> result =
                    [];


                foreach (var commit in commits)
                {
                    /*
                     * commit.Author:
                     *
                     * GitHub account associated with the commit.
                     *
                     * commit.Commit.Author:
                     *
                     * Raw author name/email stored in Git.
                     */

                    if (commit.Author == null)
                        continue;


                    if (
                        String.IsNullOrWhiteSpace(
                            commit.Author.Login
                        )
                    )
                    {
                        continue;
                    }


                    if (commit.Commit == null)
                        continue;


                    if (commit.Commit.Author == null)
                        continue;


                    result.Add(
                        new IdentityCommitEvidence
                        {
                            Login =
                                commit.Author.Login,

                            Sha =
                                commit.Sha,

                            AuthorName =
                                commit.Commit.Author.Name
                                ?? "",

                            AuthorEmail =
                                commit.Commit.Author.Email
                                ?? "",

                            AuthoredAt =
                                commit.Commit.Author.Date
                        }
                    );
                }


                return result;
            }


            // ========================================================================
            // STRING NORMALIZATION
            // ========================================================================

            private static string NormalizeEmail(
                string? email)
            {
                if (
                    String.IsNullOrWhiteSpace(
                        email
                    )
                )
                {
                    return "";
                }


                return email
                    .Trim()
                    .ToLowerInvariant();
            }


            private static string NormalizeNameKey(
                string name)
            {
                /*
                 * Only used so trivial capitalization/spacing differences
                 * don't produce separate author names.
                 *
                 * "Dery Almas"
                 * "dery almas"
                 *
                 * therefore become the same comparison key.
                 */

                return String.Concat(
                    name
                        .Where(
                            character =>
                                Char.IsLetterOrDigit(
                                    character
                                )
                        )
                        .Select(
                            Char.ToLowerInvariant
                        )
                );
            }


            // ========================================================================
            // OUTPUT
            // ========================================================================

            public void PrintCandidates(
                IEnumerable<SharedAccountCandidate> candidates,
                int maximumToPrint = 50)
            {
                var candidateList =
                    candidates
                        .Take(maximumToPrint)
                        .ToList();


                Console.WriteLine();

                Console.WriteLine(
                    "===== POSSIBLE SHARED ACCOUNTS ====="
                );


                if (candidateList.Count == 0)
                {
                    Console.WriteLine(
                        "No accounts matched the candidate criteria " +
                        "after identity normalization."
                    );

                    return;
                }


                foreach (
                    var candidate
                    in candidateList
                )
                {
                    Console.WriteLine();

                    Console.WriteLine(
                        $"ACCOUNT: {candidate.Login}"
                    );


                    Console.WriteLine(
                        $"Commits: {candidate.CommitCount}"
                    );


                    Console.WriteLine(
                        $"Commits changed by .mailmap: " +
                        $"{candidate.MailmapNormalizedCommits}"
                    );


                    Console.WriteLine(
                        $"Distinct canonical author names: " +
                        $"{candidate.DistinctAuthorNames}"
                    );


                    foreach (
                        string name
                        in candidate.AuthorNames
                    )
                    {
                        Console.WriteLine(
                            $"    NAME: {name}"
                        );
                    }


                    Console.WriteLine(
                        $"Distinct canonical email identities: " +
                        $"{candidate.DistinctEmailIdentities}"
                    );


                    foreach (
                        string email
                        in candidate.AuthorEmails
                    )
                    {
                        Console.WriteLine(
                            $"    EMAIL: {email}"
                        );
                    }


                    Console.WriteLine(
                        "Identity keys:"
                    );


                    foreach (
                        string identity
                        in candidate.EmailIdentityKeys
                    )
                    {
                        Console.WriteLine(
                            $"    IDENTITY: {identity}"
                        );
                    }


                    Console.WriteLine(
                        "Example commits:"
                    );


                    foreach (
                        string sha
                        in candidate.ExampleCommitShas
                    )
                    {
                        Console.WriteLine(
                            $"    https://github.com/" +
                            $"{RepoOwner}/" +
                            $"{RepoName}/" +
                            $"commit/" +
                            $"{sha}"
                        );
                    }
                }
            }


            // ========================================================================
            // RATE LIMIT HANDLING
            // ========================================================================

            private async Task<T>
                WithRateLimitRetry<T>(
                    Func<Task<T>> action)
            {
                while (true)
                {
                    try
                    {
                        return await action();
                    }
                    catch (
                        RateLimitExceededException ex)
                    {
                        TimeSpan waitTime =
                            ex.Reset
                            - DateTimeOffset.UtcNow;


                        if (
                            waitTime
                            < TimeSpan.Zero
                        )
                        {
                            waitTime =
                                TimeSpan.Zero;
                        }


                        waitTime =
                            waitTime.Add(
                                TimeSpan.FromSeconds(2)
                            );


                        Console.WriteLine(
                            $"GitHub rate limit reached. " +
                            $"Waiting " +
                            $"{waitTime.TotalSeconds:F0} " +
                            $"seconds..."
                        );


                        await Task.Delay(
                            waitTime
                        );
                    }
                }
            }
        }
    }
}
