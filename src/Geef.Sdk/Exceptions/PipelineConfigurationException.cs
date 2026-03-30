namespace Geef.Sdk.Exceptions;

/// <summary>
/// The pipeline configuration is invalid (e.g. missing provider).
/// </summary>
public sealed class PipelineConfigurationException : GeefException
{
    /// <inheritdoc />
    public PipelineConfigurationException(string message) : base(message) { }
}
