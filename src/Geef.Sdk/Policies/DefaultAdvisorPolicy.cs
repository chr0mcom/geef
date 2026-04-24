namespace Geef.Sdk.Policies;

/// <summary>
/// Default advisor policy that allows all consultations. Used when no explicit
/// policy is configured, so the SDK works out of the box.
/// </summary>
public sealed class DefaultAdvisorPolicy : IAdvisorPolicy
{
    /// <inheritdoc />
    public bool IsConsultationAllowed(AdvisorConsultationContext consultationContext) => true;
}
