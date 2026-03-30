using Geef.Sdk.Context;
using Geef.Sdk.Results;

namespace Geef.Sdk.Providers;

/// <summary>
/// The finalization step. Only called when evaluation was successful.
/// Prepares the final artifacts from the context for output
/// (e.g. git commit, save file, API response).
/// </summary>
/// <typeparam name="TOutput">The application-specific output type.</typeparam>
public interface IFinalizer<TOutput>
{
    /// <summary>
    /// Finalizes the pipeline run and produces the output.
    /// </summary>
    Task<FinalizeResult<TOutput>> FinalizeAsync(
        IRunContext context,
        CancellationToken cancellationToken = default);
}
