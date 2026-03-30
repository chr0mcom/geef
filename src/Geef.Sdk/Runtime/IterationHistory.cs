namespace Geef.Sdk.Runtime;

/// <summary>
/// Complete history of all loop iterations.
/// Passed to the ConvergencePolicy for decision-making.
/// </summary>
public sealed class IterationHistory
{
    private readonly List<IterationRecord> _records = new();

    /// <summary>All recorded iterations.</summary>
    public IReadOnlyList<IterationRecord> Records => _records.AsReadOnly();

    /// <summary>Number of recorded iterations.</summary>
    public int Count => _records.Count;

    /// <summary>Adds an iteration record.</summary>
    public void Add(IterationRecord record) => _records.Add(record);

    /// <summary>
    /// Checks whether the finding fingerprints have not changed across the last N iterations (stagnation).
    /// </summary>
    public bool IsStagnant(int lookbackIterations = 3)
    {
        if (_records.Count < lookbackIterations) return false;

        var recent = _records.TakeLast(lookbackIterations).ToList();
        var firstSet = recent[0].FindingFingerprints;
        return recent.Skip(1).All(r => r.FindingFingerprints.SetEquals(firstSet));
    }

    /// <summary>
    /// Checks whether a finding fingerprint that had disappeared in a previous iteration
    /// has reappeared (regression).
    /// </summary>
    public bool HasRegression()
    {
        if (_records.Count < 3) return false;
        var current = _records[^1].FindingFingerprints;
        var previous = _records[^2].FindingFingerprints;
        var beforeThat = _records[^3].FindingFingerprints;

        return current.Any(fp => !previous.Contains(fp) && beforeThat.Contains(fp));
    }

    /// <summary>Total elapsed time across all iterations.</summary>
    public TimeSpan TotalElapsed =>
        _records.Count == 0
            ? TimeSpan.Zero
            : DateTimeOffset.UtcNow - _records[0].StartedAt;
}
