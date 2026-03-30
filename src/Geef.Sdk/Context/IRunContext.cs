namespace Geef.Sdk.Context;

/// <summary>
/// The typed context store that is passed through the entire pipeline.
/// Implementations MUST be thread-safe for read access (reviewers may run in parallel).
/// </summary>
public interface IRunContext
{
    /// <summary>Reads a value. Throws <see cref="KeyNotFoundException"/> if not present.</summary>
    T GetRequired<T>(ContextKey<T> key);

    /// <summary>Tries to read a value. Returns false if not present.</summary>
    bool TryGet<T>(ContextKey<T> key, out T? value);

    /// <summary>Checks whether a key is present.</summary>
    bool Contains<T>(ContextKey<T> key);

    /// <summary>
    /// Creates a NEW RunContext with the added/updated value.
    /// The original RunContext remains unchanged (immutability).
    /// </summary>
    IRunContext Set<T>(ContextKey<T> key, T value);

    /// <summary>
    /// Creates a NEW RunContext without the specified key.
    /// </summary>
    IRunContext Remove<T>(ContextKey<T> key);

    /// <summary>Returns all present key names.</summary>
    IReadOnlyCollection<string> Keys { get; }
}
