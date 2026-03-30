namespace Geef.Sdk.Context;

/// <summary>
/// A typed key for the context store.
/// Each key is unique by its name and type-safe through its type parameter.
/// </summary>
/// <typeparam name="T">The type of the stored value.</typeparam>
public sealed record ContextKey<T>(string Name)
{
    /// <inheritdoc />
    public override string ToString() => $"ContextKey<{typeof(T).Name}>(\"{Name}\")";
}
