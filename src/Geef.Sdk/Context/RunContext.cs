using System.Collections.Immutable;

namespace Geef.Sdk.Context;

/// <summary>
/// Immutable implementation of the context store.
/// Every Set/Remove creates a new instance (persistent data structure).
/// Thread-safe for any number of concurrent readers.
/// </summary>
public sealed class RunContext : IRunContext
{
    private readonly ImmutableDictionary<string, object> _store;

    /// <summary>
    /// Initializes a new empty <see cref="RunContext"/>.
    /// </summary>
    public RunContext()
        => _store = ImmutableDictionary<string, object>.Empty;

    private RunContext(ImmutableDictionary<string, object> store)
        => _store = store;

    /// <inheritdoc />
    public T GetRequired<T>(ContextKey<T> key)
    {
        if (!_store.TryGetValue(key.Name, out var value))
            throw new KeyNotFoundException(
                $"Context key '{key.Name}' (type: {typeof(T).Name}) not found. " +
                $"Available keys: [{string.Join(", ", _store.Keys)}]");
        return (T)value;
    }

    /// <inheritdoc />
    public bool TryGet<T>(ContextKey<T> key, out T? value)
    {
        if (_store.TryGetValue(key.Name, out var raw))
        {
            value = (T)raw;
            return true;
        }
        value = default;
        return false;
    }

    /// <inheritdoc />
    public bool Contains<T>(ContextKey<T> key)
        => _store.ContainsKey(key.Name);

    /// <inheritdoc />
    public IRunContext Set<T>(ContextKey<T> key, T value)
        => new RunContext(_store.SetItem(key.Name, value!));

    /// <inheritdoc />
    public IRunContext Remove<T>(ContextKey<T> key)
        => new RunContext(_store.Remove(key.Name));

    /// <inheritdoc />
    public IReadOnlyCollection<string> Keys => _store.Keys.ToList();
}
