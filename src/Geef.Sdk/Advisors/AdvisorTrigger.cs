namespace Geef.Sdk.Advisors;

/// <summary>
/// Determines when the runner automatically consults a trigger-registered advisor.
/// Advisors registered without a trigger are consulted on-demand by providers that
/// implement <see cref="IAdvisorAware"/>.
/// </summary>
public enum AdvisorTrigger
{
    /// <summary>The advisor is consulted once, before the executor produces the first draft.</summary>
    BeforeFirstExecution,

    /// <summary>The advisor is consulted before every executor iteration, including the first.</summary>
    BeforeEveryExecution,

    /// <summary>The advisor is consulted when the pipeline fails to converge; the runner then
    /// attempts exactly one recovery pass before throwing <see cref="Exceptions.ConvergenceFailedException"/>.</summary>
    OnConvergenceFailure,
}
