namespace Geef.Sdk.Runtime;

/// <summary>
/// Classifies exceptions as transient (worth retrying) or permanent (fail immediately).
/// Implement this interface with your infrastructure-specific logic (e.g. HTTP status codes)
/// and pass it to <see cref="ResilientReviewer"/> or similar decorators.
/// </summary>
public interface ITransientFaultClassifier
{
    /// <summary>
    /// Returns <see langword="true"/> if retrying the call could plausibly succeed.
    /// Returns <see langword="false"/> for permanent errors (e.g. auth failures, bad requests).
    /// </summary>
    bool IsTransient(Exception exception);
}
