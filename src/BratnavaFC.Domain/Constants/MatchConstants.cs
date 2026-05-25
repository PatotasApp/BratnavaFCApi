namespace BratnavaFC.Domain.Constants;

/// <summary>Business-rule constants for match management.</summary>
public static class MatchConstants
{
    /// <summary>Maximum number of non-finalized matches allowed simultaneously per group.</summary>
    public const int MaxSimultaneousActiveMatches = 5;
}

/// <summary>StepKey string values shared between the backend (MatchHeaderDto) and the frontend wizard.</summary>
public static class MatchStepKeys
{
    public const string Create  = "create";
    public const string Accept  = "accept";
    public const string Teams   = "teams";
    public const string Playing = "playing";
    public const string Ended   = "ended";
    public const string Post    = "post";
    public const string Done    = "done";
}
