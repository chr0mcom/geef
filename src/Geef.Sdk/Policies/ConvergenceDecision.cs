namespace Geef.Sdk.Policies;

/// <summary>
/// Possible decisions of the convergence policy.
/// </summary>
public enum ConvergenceDecision
{
    /// <summary>Evaluation passed. Leave loop, proceed to Finalize.</summary>
    Approved,

    /// <summary>Evaluation failed, but progress is detectable. Continue iterating.</summary>
    Continue,

    /// <summary>Maximum iterations or time budget reached. Abort pipeline.</summary>
    StopMaxAttemptsReached,

    /// <summary>Stagnation detected (same findings over multiple rounds). Abort.</summary>
    StopStagnant,

    /// <summary>Regression detected (fixed issues reappeared). Abort.</summary>
    StopRegression,

    /// <summary>Critical security/safety blocker. Immediate abort.</summary>
    AbortCriticalBlocker,

    /// <summary>Automatic resolution seems impossible. Escalate to human.</summary>
    EscalateToHuman
}
