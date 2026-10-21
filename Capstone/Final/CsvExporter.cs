using System.Globalization;
using System.Text;
using Octokit;

namespace Capstone;

public record CsvExportOptions
{
    /// <summary>
    /// Column separator. Use ',' for English-locale Excel and ';' for Dutch/German/etc. locales,
    /// where Excel expects semicolons when you double-click a .csv.
    /// </summary>
    public char Delimiter { get; init; } = ',';

    /// <summary>Include title-adjacent free text (issue/PR bodies, comment bodies, review bodies). Makes files big.</summary>
    public bool IncludeBodies { get; init; } = false;

    /// <summary>Excel cells hold at most 32,767 characters, so bodies are cut off at this length.</summary>
    public int MaxBodyLength { get; init; } = 30000;
}

/// <summary>
/// Writes a RepositoryDataset to a set of related CSV files (one per kind of object), meant to be
/// opened in Excel or loaded into other tools. Nothing is aggregated or precomputed: one row per
/// object, with the raw fields and timestamps.
///
/// Files and how they relate:
///   pull_requests.csv   one row per PR                  key: number
///   issues.csv          one row per issue (no PRs)      key: number
///   reviews.csv         one row per PR review           pr_number     -> pull_requests.number
///   review_comments.csv one row per inline diff comment pr_number     -> pull_requests.number
///   comments.csv        conversation comments on both   parent_type ("issue" | "pull_request") + parent_number
///
/// Conventions: timestamps are UTC as "yyyy-MM-dd HH:mm:ss" (Excel parses these as dates),
/// booleans are 1/0 (so AVERAGE gives a rate), list fields (labels, assignees) are joined with "|".
/// Files are UTF-8 with BOM so Excel shows non-ASCII names and text correctly.
/// </summary>
public class CsvExporter
{
    private readonly string _outputDirectory;
    private readonly CsvExportOptions _options;

    private static string GetCSVDirectory()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null && !directory.GetFiles("*.sln").Any())
        {
            directory = directory.Parent;
        }

        if (directory == null)
        {
            throw new DirectoryNotFoundException("solution directory was not found");
        }

        var csvDirectory = directory + "\\.csv\\";
        Directory.CreateDirectory(csvDirectory);
        return csvDirectory;
    }
    
    public CsvExporter(CsvExportOptions? options = null)
    {
        _outputDirectory = GetCSVDirectory();
        _options = options ?? new CsvExportOptions();
    }

    /// <summary>Writes all CSV files and returns their paths.</summary>
    public List<string> Export(RepositoryDataset data)
    {
        Directory.CreateDirectory(_outputDirectory);
        return
        [
            WritePullRequests(data.PullRequests),
            WriteIssues(data.Issues),
            WriteReviews(data.PullRequests),
            WriteReviewComments(data.PullRequests),
            WriteComments(data)
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
        if (_options.IncludeBodies)
            cols.Add(Col<PullRequestData>("body", p => Body(p.PullRequest.Body)));

        return WriteCsv("pull_requests.csv", prs, cols);
    }

    private string WriteIssues(List<IssueData> issues)
    {
        var cols = new List<Column<IssueData>>
        {
            Col<IssueData>("number", i => i.Issue.Number),
            Col<IssueData>("title", i => i.Issue.Title),
            Col<IssueData>("state", i => i.Issue.State.StringValue),
            Col<IssueData>("author", i => i.Issue.User?.Login),
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
        if (_options.IncludeBodies)
            cols.Add(Col<IssueData>("body", i => Body(i.Issue.Body)));

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
            Col<Child<PullRequestReview>>("reviewer_association", c => c.Item.AuthorAssociation.StringValue),
            Col<Child<PullRequestReview>>("state", c => c.Item.State.StringValue),
            Col<Child<PullRequestReview>>("submitted_at", c => c.Item.SubmittedAt),
            Col<Child<PullRequestReview>>("commit_id", c => c.Item.CommitId),
            Col<Child<PullRequestReview>>("url", c => c.Item.HtmlUrl),
        };
        if (_options.IncludeBodies)
            cols.Add(Col<Child<PullRequestReview>>("body", c => Body(c.Item.Body)));

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
            Col<Child<PullRequestReviewComment>>("author_association", c => c.Item.AuthorAssociation.StringValue),
            Col<Child<PullRequestReviewComment>>("created_at", c => c.Item.CreatedAt),
            Col<Child<PullRequestReviewComment>>("updated_at", c => c.Item.UpdatedAt),
            Col<Child<PullRequestReviewComment>>("path", c => c.Item.Path),
            Col<Child<PullRequestReviewComment>>("url", c => c.Item.HtmlUrl),
        };
        if (_options.IncludeBodies)
            cols.Add(Col<Child<PullRequestReviewComment>>("body", c => Body(c.Item.Body)));

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
            Col<CommentRow>("author_association", c => c.Comment.AuthorAssociation.StringValue),
            Col<CommentRow>("created_at", c => c.Comment.CreatedAt),
            Col<CommentRow>("updated_at", c => c.Comment.UpdatedAt),
            Col<CommentRow>("url", c => c.Comment.HtmlUrl),
        };
        if (_options.IncludeBodies)
            cols.Add(Col<CommentRow>("body", c => Body(c.Comment.Body)));

        return WriteCsv("comments.csv", rows, cols);
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
        using var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true))
        {
            NewLine = "\r\n"
        };

        writer.WriteLine(string.Join(_options.Delimiter, columns.Select(c => Escape(c.Header))));

        int count = 0;
        foreach (var row in rows)
        {
            writer.WriteLine(string.Join(_options.Delimiter, columns.Select(c => Escape(Format(c.Get(row))))));
            count++;
        }

        Console.WriteLine($"Wrote {count} rows to {path}");
        return path;
    }

    private static string Format(object? value) => value switch
    {
        null => "",
        string s => s,
        DateTimeOffset d => d.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        bool b => b ? "1" : "0",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };

    /// <summary>RFC 4180 quoting: wrap in quotes if the value contains the delimiter, a quote or a line break.</summary>
    private string Escape(string value)
    {
        if (value.IndexOfAny(['"', '\r', '\n', _options.Delimiter]) < 0) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private string? Body(string? text)
    {
        if (text == null) return null;
        return text.Length <= _options.MaxBodyLength ? text : text[.._options.MaxBodyLength];
    }

    private static string? JoinLabels(IReadOnlyList<Label>? labels) =>
        labels == null ? null : string.Join("|", labels.Select(l => l.Name));
}
