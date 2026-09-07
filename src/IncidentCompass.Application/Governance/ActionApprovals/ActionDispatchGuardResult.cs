namespace IncidentCompass.Application.Governance.ActionApprovals;

internal sealed record ActionDispatchGuardResult(
    bool MayInvoke,
    bool Simulate,
    string? FailureCode)
{
    public static ActionDispatchGuardResult Invoke { get; } = new(true, false, null);

    public static ActionDispatchGuardResult DryRun { get; } = new(false, true, null);

    public static ActionDispatchGuardResult Fail(string failureCode) => new(false, false, failureCode);
}
