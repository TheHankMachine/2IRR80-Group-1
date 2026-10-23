using System.Globalization;
using System.Text;
using System.Text.Json;
using Octokit;

namespace Capstone;

/// <summary>
/// Writes a RepositoryDataset to a set of related CSV files, meant to be loaded with pandas.read_csv
/// with no extra arguments. One row per object, raw fields only, nothing aggregated.
///
/// Files and how they relate:
///   pull_requests.csv   one row per PR                   key: number
///   issues.csv          one row per issue (no PRs)       key: number
///   reviews.csv         one row per PR review            pr_number     -> pull_requests.number
///   review_comments.csv one row per inline diff comment  pr_number     -> pull_requests.number
///   comments.csv        conversation comments on both    parent_type ("issue" | "pull_request") + parent_number
///                                                        -> issues.number or pull_requests.number
///   users.csv           one row per account profile      key: login   <- author, reviewer, merged_by, closed_by, assignees
///                       (current profile state; accounts deleted since are missing)
///   dataset.json        fetch options (period, sample size, seed), so you know what the data is a sample of
///
/// Issues and PRs share one number sequence per repo, so a number is unique across both files.
///
/// Conventions:
///   - comma separated, UTF-8 without BOM, "\n" line endings, RFC 4180 quoting
///   - timestamps are ISO 8601 UTC, e.g. "2024-03-01T12:34:56Z"; every timestamp column ends in "_at"
///   - booleans are "true"/"false"
///   - missing values are empty cells (pandas reads them as NaN/NA)
///   - list fields (labels, assignees) are joined with "|"  ->  df["labels"].str.split("|")
///   - *_type columns hold GitHub's account type ("User", "Bot", "Organization"), useful for filtering bots
/// </summary>
public class CsvExporter
{
    private const char Delimiter = ',';

    private readonly string _outputDirectory;

    public CsvExporter(string? outputDirectory = null)
    {
        _outputDirectory = Path.Combine(GetCsvDirectory(), outputDirectory ?? DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss-fff")) ;
    }

    private static string GetCsvDirectory()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null && !directory.GetFiles("*.sln").Any())
            directory = directory.Parent;

        if (directory == null)
            throw new DirectoryNotFoundException("solution directory was not found");

        return Path.Combine(directory.FullName, ".csv");
    }

    /// <summary>Writes all files and returns their paths.</summary>
    public List<string> Export(RepositoryDataset data)
    {
        Directory.CreateDirectory(_outputDirectory);
        return
        [
            WritePullRequests(data.PullRequests),
            WriteIssues(data.Issues),
            WriteReviews(data.PullRequests),
            WriteReviewComments(data.PullRequests),
            WriteComments(data),
            WriteUsers(data.Users),
            WriteMetadata(data.Options),
        ];
    }

    // ------------------------------------------------------------------
    // Tables
    // ------------------------------------------------------------------

    private string WritePullRequests(List<PullRequestData> prs)
    {
        var cols = new List<Column<PullRequestData>>
        {
            Col<PullRequestData>("number", p => p.PullRequest.Number),
            Col<PullRequestData>("title", p => p.PullRequest.Title),
            Col<PullRequestData>("state", p => p.PullRequest.State.StringValue),
            Col<PullRequestData>("author", p => p.PullRequest.User?.Login),
            Col<PullRequestData>("author_type", p => p.PullRequest.User?.Type),
            Col<PullRequestData>("created_at", p => p.PullRequest.CreatedAt),
            Col<PullRequestData>("updated_at", p => p.PullRequest.UpdatedAt),
            Col<PullRequestData>("closed_at", p => p.PullRequest.ClosedAt),
            Col<PullRequestData>("merged_at", p => p.PullRequest.MergedAt),
            Col<PullRequestData>("merged", p => p.PullRequest.Merged),
            Col<PullRequestData>("merged_by", p => p.PullRequest.MergedBy?.Login),
            Col<PullRequestData>("draft", p => p.PullRequest.Draft),
            Col<PullRequestData>("labels", p => JoinLabels(p.PullRequest.Labels)),
            Col<PullRequestData>("milestone", p => p.PullRequest.Milestone?.Title),
            Col<PullRequestData>("base_ref", p => p.PullRequest.Base?.Ref),
            Col<PullRequestData>("head_ref", p => p.PullRequest.Head?.Ref),
            Col<PullRequestData>("additions", p => p.PullRequest.Additions),
            Col<PullRequestData>("deletions", p => p.PullRequest.Deletions),
            Col<PullRequestData>("changed_files", p => p.PullRequest.ChangedFiles),
            Col<PullRequestData>("commits", p => p.PullRequest.Commits),
            Col<PullRequestData>("comment_count", p => p.PullRequest.Comments),
            Col<PullRequestData>("url", p => p.PullRequest.HtmlUrl),
        };

        return WriteCsv("pull_requests.csv", prs, cols);
    }

    private string WriteIssues(List<IssueData> issues)
    {
        var cols = new List<Column<IssueData>>
        {
            Col<IssueData>("number", i => i.Issue.Number),
            Col<IssueData>("title", i => i.Issue.Title),
            Col<IssueData>("state", i => i.Issue.State.StringValue),
            Col<IssueData>("state_reason", i => i.Issue.StateReason?.StringValue),
            Col<IssueData>("author", i => i.Issue.User?.Login),
            Col<IssueData>("author_type", i => i.Issue.User?.Type),
            Col<IssueData>("created_at", i => i.Issue.CreatedAt),
            Col<IssueData>("updated_at", i => i.Issue.UpdatedAt),
            Col<IssueData>("closed_at", i => i.Issue.ClosedAt),
            Col<IssueData>("closed_by", i => i.Issue.ClosedBy?.Login),
            Col<IssueData>("labels", i => JoinLabels(i.Issue.Labels)),
            Col<IssueData>("assignees", i => i.Issue.Assignees == null
                ? null
                : string.Join("|", i.Issue.Assignees.Select(a => a.Login))),
            Col<IssueData>("milestone", i => i.Issue.Milestone?.Title),
            Col<IssueData>("comment_count", i => i.Issue.Comments),
            Col<IssueData>("locked", i => i.Issue.Locked),
            Col<IssueData>("url", i => i.Issue.HtmlUrl),
        };

        return WriteCsv("issues.csv", issues, cols);
    }

    private string WriteReviews(List<PullRequestData> prs)
    {
        var rows = prs.SelectMany(p => p.Reviews.Select(r => new Child<PullRequestReview>(p.PullRequest.Number, r)));

        var cols = new List<Column<Child<PullRequestReview>>>
        {
            Col<Child<PullRequestReview>>("pr_number", c => c.ParentNumber),
            Col<Child<PullRequestReview>>("review_id", c => c.Item.Id),
            Col<Child<PullRequestReview>>("reviewer", c => c.Item.User?.Login),
            Col<Child<PullRequestReview>>("reviewer_type", c => c.Item.User?.Type),
            Col<Child<PullRequestReview>>("reviewer_association", c => c.Item.AuthorAssociation.StringValue),
            Col<Child<PullRequestReview>>("state", c => c.Item.State.StringValue),
            Col<Child<PullRequestReview>>("submitted_at", c => c.Item.SubmittedAt),
            Col<Child<PullRequestReview>>("commit_id", c => c.Item.CommitId),
            Col<Child<PullRequestReview>>("url", c => c.Item.HtmlUrl),
        };

        return WriteCsv("reviews.csv", rows, cols);
    }

    private string WriteReviewComments(List<PullRequestData> prs)
    {
        var rows = prs.SelectMany(p => p.ReviewComments.Select(c => new Child<PullRequestReviewComment>(p.PullRequest.Number, c)));

        var cols = new List<Column<Child<PullRequestReviewComment>>>
        {
            Col<Child<PullRequestReviewComment>>("pr_number", c => c.ParentNumber),
            Col<Child<PullRequestReviewComment>>("comment_id", c => c.Item.Id),
            Col<Child<PullRequestReviewComment>>("review_id", c => c.Item.PullRequestReviewId),
            Col<Child<PullRequestReviewComment>>("in_reply_to_id", c => c.Item.InReplyToId),
            Col<Child<PullRequestReviewComment>>("author", c => c.Item.User?.Login),
            Col<Child<PullRequestReviewComment>>("author_type", c => c.Item.User?.Type),
            Col<Child<PullRequestReviewComment>>("author_association", c => c.Item.AuthorAssociation.StringValue),
            Col<Child<PullRequestReviewComment>>("created_at", c => c.Item.CreatedAt),
            Col<Child<PullRequestReviewComment>>("updated_at", c => c.Item.UpdatedAt),
            Col<Child<PullRequestReviewComment>>("path", c => c.Item.Path),
            Col<Child<PullRequestReviewComment>>("url", c => c.Item.HtmlUrl),
        };

        return WriteCsv("review_comments.csv", rows, cols);
    }

    private string WriteComments(RepositoryDataset data)
    {
        var rows =
            data.PullRequests.SelectMany(p => p.Comments.Select(c => new CommentRow("pull_request", p.PullRequest.Number, c)))
            .Concat(data.Issues.SelectMany(i => i.Comments.Select(c => new CommentRow("issue", i.Issue.Number, c))));

        var cols = new List<Column<CommentRow>>
        {
            Col<CommentRow>("parent_type", c => c.ParentType),
            Col<CommentRow>("parent_number", c => c.ParentNumber),
            Col<CommentRow>("comment_id", c => c.Comment.Id),
            Col<CommentRow>("author", c => c.Comment.User?.Login),
            Col<CommentRow>("author_type", c => c.Comment.User?.Type),
            Col<CommentRow>("author_association", c => c.Comment.AuthorAssociation.StringValue),
            Col<CommentRow>("created_at", c => c.Comment.CreatedAt),
            Col<CommentRow>("updated_at", c => c.Comment.UpdatedAt),
            Col<CommentRow>("url", c => c.Comment.HtmlUrl),
        };

        return WriteCsv("comments.csv", rows, cols);
    }

    private string WriteUsers(List<User> users)
    {
        var cols = new List<Column<User>>
        {
            Col<User>("login", u => u.Login),
            Col<User>("id", u => u.Id),
            Col<User>("type", u => u.Type),
            Col<User>("name", u => u.Name),
            Col<User>("company", u => u.Company),
            Col<User>("blog", u => u.Blog),
            Col<User>("location", u => u.Location),
            Col<User>("email", u => u.Email), // only the PUBLIC profile email, usually empty
            Col<User>("bio", u => u.Bio),
            Col<User>("hireable", u => u.Hireable),
            Col<User>("site_admin", u => u.SiteAdmin),
            Col<User>("public_repos", u => u.PublicRepos),
            Col<User>("followers", u => u.Followers),
            Col<User>("following", u => u.Following),
            Col<User>("created_at", u => u.CreatedAt),
            Col<User>("updated_at", u => u.UpdatedAt),
            Col<User>("url", u => u.HtmlUrl),
        };

        return WriteCsv("users.csv", users, cols);
    }

    /// <summary>Records what the data is (repo, period, sample size, seed) next to the CSVs.</summary>
    private string WriteMetadata(FetchOptions o)
    {
        var meta = new
        {
            owner = o.Owner,
            repo = o.Repo,
            from = o.From,
            to = o.To,
            sample_size = o.SampleSize,
            seed = o.Seed,
            include_issues = o.IncludeIssues,
            include_pull_requests = o.IncludePullRequests,
            fetch_user_profiles = o.FetchUserProfiles,
            exported_at = DateTimeOffset.UtcNow,
        };

        string path = Path.Combine(_outputDirectory, "dataset.json");
        File.WriteAllText(path, JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Console.WriteLine($"Wrote metadata to {path}");
        return path;
    }

    // ------------------------------------------------------------------
    // CSV plumbing
    // ------------------------------------------------------------------

    private sealed record Column<T>(string Header, Func<T, object?> Get);
    private sealed record Child<T>(int ParentNumber, T Item);
    private sealed record CommentRow(string ParentType, int ParentNumber, IssueComment Comment);

    private static Column<T> Col<T>(string header, Func<T, object?> get) => new(header, get);

    private string WriteCsv<T>(string fileName, IEnumerable<T> rows, List<Column<T>> columns)
    {
        string path = Path.Combine(_outputDirectory, fileName);
        using var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            NewLine = "\n"
        };

        writer.WriteLine(string.Join(Delimiter, columns.Select(c => Escape(c.Header))));

        int count = 0;
        foreach (var row in rows)
        {
            writer.WriteLine(string.Join(Delimiter, columns.Select(c => Escape(Format(c.Get(row))))));
            count++;
        }

        Console.WriteLine($"Wrote {count} rows to {path}");
        return path;
    }

    private static string Format(object? value) => value switch
    {
        null => "",
        string s => s,
        DateTimeOffset d => d.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        Enum e => e.ToString(),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };

    /// <summary>RFC 4180 quoting: wrap in quotes if the value contains the delimiter, a quote or a line break.</summary>
    private static string Escape(string value)
    {
        if (value.IndexOfAny(['"', '\r', '\n', Delimiter]) < 0) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string? JoinLabels(IReadOnlyList<Label>? labels) =>
        labels == null ? null : string.Join("|", labels.Select(l => l.Name));
}
