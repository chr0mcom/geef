namespace Geef.Sdk.Results;

/// <summary>
/// Severity levels for review findings.
/// </summary>
public enum FindingSeverity
{
    /// <summary>Informational only. Does not block the loop.</summary>
    Info,

    /// <summary>Warning. Does not block the loop by default, but configurable via ConvergencePolicy.</summary>
    Warning,

    /// <summary>Error. Blocks the loop by default.</summary>
    Error,

    /// <summary>Critical blocker. May cause immediate abort depending on policy.</summary>
    Critical
}
