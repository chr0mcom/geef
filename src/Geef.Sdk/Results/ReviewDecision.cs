namespace Geef.Sdk.Results;

/// <summary>
/// Possible decisions a reviewer can make.
/// </summary>
public enum ReviewDecision
{
    /// <summary>No issues found. Artifacts are acceptable.</summary>
    Approved,

    /// <summary>Issues found. Execution must be repeated.</summary>
    Rejected,

    /// <summary>Approved, but with warnings that should be documented.</summary>
    ApprovedWithWarnings,

    /// <summary>Reviewer suggests retry, but cannot name a clear issue.</summary>
    RetrySuggested,

    /// <summary>Reviewer is not applicable for this kind of artifact.</summary>
    NotApplicable,

    /// <summary>
    /// Reviewer could not be executed technically (e.g. API timeout, infrastructure error).
    /// NOT to be confused with content-based rejection.
    /// </summary>
    Failed
}
