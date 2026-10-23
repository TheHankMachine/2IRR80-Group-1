namespace Capstone.Final
{
    public class BotAssessment
    {
        public required string Login { get; init; }

        // Measure 1: GitHub explicitly identifies the account as a bot
        public bool HasGithubBotMarker { get; init; }
        // Supporting heuristic
        public bool HasBotLikeName { get; init; }
        // Measure 2: Activity pattern looks strongly automated
        public bool HasAutomatedActivityPattern { get; init; }
        public bool IsBot { get; init; }
        public required string Reason { get; init; }
    }


    public static class BotFiltering
    {
        /*
         * Accounts manually confirmed to be bots.
         * i ended up not adding anything because realistically we only needed one example lol
         */
        private static readonly HashSet<string> ManuallyVerifiedBots =
            new(StringComparer.OrdinalIgnoreCase)
            {
                // Example:
                // "some-automation-account"
            };


        /*
         * Accounts whose usernames look automated but which we manually confirmed are human.
         * once again only one example so ehh
         */
        private static readonly HashSet<string> ManuallyVerifiedHumans =
            new(StringComparer.OrdinalIgnoreCase)
            {
            "DungenRobot"
            };

        private static readonly string[] BotLikeWords =
        [
        "bot",
        "robot",
        "automation",
        "automated",
        "copilot",
        "dependabot"
        ];

        public static BotAssessment Assess(ContributorInvolvement contributor)
        {
            string login = contributor.Login;


            // Explicit bot verification
            bool githubBotMarker = login.EndsWith("[bot]", StringComparison.OrdinalIgnoreCase);

            // Inlcuidng bot-like words
            string lowerLogin = login.ToLowerInvariant();
            bool botLikeName = BotLikeWords.Any(word => lowerLogin.Contains(word));

            // Automated looking activity

            /* What this means:
             * - authors no PRs
             * authors no issues
             * ibly performs review activity
             * reviews a large number of PRs
             */
            bool automatedActivityPattern = contributor.PullRequestsAuthored == 0
                && contributor.IssuesAuthored == 0 && contributor.PullRequestsReviewed >= 20;
            bool manuallyVerifiedBot = ManuallyVerifiedBots.Contains(login);
            bool manuallyVerifiedHuman = ManuallyVerifiedHumans.Contains(login);


            // Final classificaiton
            bool isBot = !manuallyVerifiedHuman && (githubBotMarker || manuallyVerifiedBot);
            string reason;

            if (manuallyVerifiedHuman)
            {
                reason = "Manually verified human despite bot-like characteristics";
            }
            else if (githubBotMarker)
            {
                reason = "GitHub login has explicit [bot] marker";
            }
            else if (manuallyVerifiedBot)
            {
                reason = "Manually verified automated account";
            }
            else if (botLikeName && automatedActivityPattern)
            {
                reason = "Bot-like username and automated-looking activity; manual review required";
            }
            else if (botLikeName)
            {
                reason = "Bot-like username; manual review required";
            }
            else if (automatedActivityPattern)
            {
                reason = "Automated-looking activity pattern; manual review required";
            }
            else
            {
                reason = "No bot evidence";
            }

            return new BotAssessment
            {
                Login = login,
                HasGithubBotMarker = githubBotMarker,
                HasBotLikeName = botLikeName,
                HasAutomatedActivityPattern = automatedActivityPattern,
                IsBot = isBot,
                Reason = reason
            };
        }


        // Remove verified bots
        public static List<ContributorInvolvement> FilterBots(IEnumerable<ContributorInvolvement> contributors)
        {
            return contributors.Where(contributor => !Assess(contributor).IsBot).ToList();
        }


        // Print ^.^
        public static void PrintBotAnalysis(IEnumerable<ContributorInvolvement> contributors)
        {
            var assessments = contributors.Select(Assess).ToList();
            var definiteBots = assessments.Where(assessment => assessment.IsBot)
                    .OrderBy(assessment => assessment.Login).ToList();


            var candidates = assessments.Where(assessment => !assessment.IsBot
                    && (assessment.HasBotLikeName || assessment.HasAutomatedActivityPattern))
                    .OrderBy(assessment => assessment.Login).ToList();

            Console.WriteLine();
            Console.WriteLine("===== BOT FILTERING =====");
            Console.WriteLine($"Verified bots: {definiteBots.Count}");

            foreach (var bot in definiteBots)
            {
                Console.WriteLine($"BOT: {bot.Login} | {bot.Reason}");
            }

            Console.WriteLine();
            Console.WriteLine($"Candidates requiring manual inspection: " + $"{candidates.Count}");

            foreach (var candidate in candidates)
            {
                Console.WriteLine($"CANDIDATE: {candidate.Login}" + $" | bot-name={candidate.HasBotLikeName}" +
                    $" | automated-pattern=" + $"{candidate.HasAutomatedActivityPattern}" + $" | {candidate.Reason}");
            }
        }
    }
}
