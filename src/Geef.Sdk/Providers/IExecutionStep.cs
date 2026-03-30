using Geef.Sdk.Context;
using Geef.Sdk.Results;

namespace Geef.Sdk.Providers;

/// <summary>
/// The "doer". Takes the current context (including any feedback from PreviousFindings)
/// and generates or modifies artifacts.
/// Implementations MUST NOT mutate the passed RunContext.
/// They MUST create a new context snapshot via context.Set() and return it in ExecutionResult.UpdatedContext.
/// </summary>
public interface IExecutionStep
{
    /// <summary>
    /// Runs the execution phase to generate or modify artifacts.
    /// </summary>
    Task<ExecutionResult> RunAsync(
        IRunContext context,
        CancellationToken cancellationToken = default);
}
