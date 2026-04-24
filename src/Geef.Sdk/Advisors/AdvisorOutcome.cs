namespace Geef.Sdk.Advisors;

/// <summary>
/// The outcome of an advisor consultation. Failure modes are signalled via this enum
/// rather than exceptions, mirroring the Anthropic <c>advisor_tool_result_error</c> behavior.
/// </summary>
public enum AdvisorOutcome
{
    /// <summary>The advisor was invoked and returned advice normally.</summary>
    Success,

    /// <summary>
    /// The orchestrator declined to invoke the advisor because a budget cap would have been exceeded.
    /// Synthetic outcome; the advisor itself was never called.
    /// </summary>
    BudgetExceeded,

    /// <summary>
    /// The advisor was invoked but its execution failed (timeout, exception, network error).
    /// Budget IS counted because the advisor was actually called.
    /// </summary>
    InfrastructureFailure,

    /// <summary>
    /// The orchestrator declined to invoke the advisor because the consultation policy rejected it,
    /// OR because no advisor was registered (default-call) or the named advisor was not found.
    /// Reserved for synthetic, non-invoked paths. A real <see cref="IAdvisor"/> implementation MUST NOT
    /// return this outcome — if a real advisor wants to signal "I have no useful advice", it returns
    /// <see cref="Success"/> with empty or minimal <c>AdviceText</c> (and may set
    /// <c>Confidence = AdvisorConfidence.Uncertain</c>). This rule keeps the budget-counting semantics
    /// unambiguous: <see cref="Success"/> and <see cref="InfrastructureFailure"/> always mean
    /// "advisor was invoked, count budget"; <see cref="NoApplicableAdvice"/> always means
    /// "advisor was not invoked, do not count budget".
    /// </summary>
    NoApplicableAdvice
}
