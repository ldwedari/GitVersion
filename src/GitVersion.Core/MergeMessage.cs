using System.Text.RegularExpressions;
using GitVersion.Configuration;
using GitVersion.Extensions;

namespace GitVersion;

public class MergeMessage
{
    private static readonly IList<MergeMessageFormat> DefaultFormats = new List<MergeMessageFormat>
    {
        new("Default", @"^Merge (branch|tag) '(?<SourceBranch>[^']*)'(?: into (?<TargetBranch>[^\s]*))*"),
        new("SmartGit",  @"^Finish (?<SourceBranch>[^\s]*)(?: into (?<TargetBranch>[^\s]*))*"),
        new("BitBucketPull", @"^Merge pull request #(?<PullRequestNumber>\d+) (from|in) (?<Source>.*) from (?<SourceBranch>[^\s]*) to (?<TargetBranch>[^\s]*)"),
        new("BitBucketPullv7", @"^Pull request #(?<PullRequestNumber>\d+).*\r?\n\r?\nMerge in (?<Source>.*) from (?<SourceBranch>[^\s]*) to (?<TargetBranch>[^\s]*)"),
        new("GitHubPull", @"^Merge pull request #(?<PullRequestNumber>\d+) (from|in) (?:[^\s\/]+\/)?(?<SourceBranch>[^\s]*)(?: into (?<TargetBranch>[^\s]*))*"),
        new("RemoteTracking", @"^Merge remote-tracking branch '(?<SourceBranch>[^\s]*)'(?: into (?<TargetBranch>[^\s]*))*")
    };

    public MergeMessage(string mergeMessage, GitVersionConfiguration configuration)
    {
        mergeMessage.NotNull();

        if (mergeMessage == string.Empty) return;

        // Concatenate configuration formats with the defaults.
        // Ensure configurations are processed first.
        var allFormats = configuration.MergeMessageFormats
            .Select(x => new MergeMessageFormat(x.Key, x.Value))
            .Concat(DefaultFormats);

        foreach (var format in allFormats)
        {
            var match = format.Pattern.Match(mergeMessage);
            if (!match.Success)
                continue;

            FormatName = format.Name;
            var sourceBranch = match.Groups["SourceBranch"].Value;
            MergedBranch = GetMergedBranchName(sourceBranch);

            if (match.Groups["TargetBranch"].Success)
            {
                TargetBranch = match.Groups["TargetBranch"].Value;
            }

            if (int.TryParse(match.Groups["PullRequestNumber"].Value, out var pullNumber))
            {
                PullRequestNumber = pullNumber;
            }

            Version = ParseVersion(configuration.LabelPrefix, configuration.SemanticVersionFormat);

            break;
        }
    }

    public string? FormatName { get; }
    public string? TargetBranch { get; }
    public ReferenceName? MergedBranch { get; }

    public bool IsMergedPullRequest => PullRequestNumber != null;
    public int? PullRequestNumber { get; }
    public SemanticVersion? Version { get; }

    private SemanticVersion? ParseVersion(string? tagPrefix, SemanticVersionFormat versionFormat)
    {
        if (tagPrefix is null || MergedBranch is null)
            return null;
        // Remove remotes and branch prefixes like release/ feature/ hotfix/ etc
        var toMatch = Regex.Replace(MergedBranch.WithoutOrigin, @"^(\w+[-/])*", "", RegexOptions.IgnoreCase);
        toMatch = Regex.Replace(toMatch, $"^{tagPrefix}", "");
        // We don't match if the version is likely an ip (i.e starts with http://)
        var versionMatch = new Regex(@"^(?<!://)\d+\.\d+(\.*\d+)*");
        var version = versionMatch.Match(toMatch);

        if (version.Success && SemanticVersion.TryParse(version.Value, tagPrefix, out var val, versionFormat))
        {
            return val;
        }

        return null;
    }

    private class MergeMessageFormat
    {
        public MergeMessageFormat(string name, string pattern)
        {
            Name = name;
            Pattern = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }

        public string Name { get; }

        public Regex Pattern { get; }
    }

    private ReferenceName GetMergedBranchName(string mergedBranch)
    {
        if (FormatName == "RemoteTracking" && !mergedBranch.StartsWith(ReferenceName.RemoteTrackingBranchPrefix))
        {
            mergedBranch = $"{ReferenceName.RemoteTrackingBranchPrefix}{mergedBranch}";
        }
        return ReferenceName.FromBranchName(mergedBranch);
    }
}
