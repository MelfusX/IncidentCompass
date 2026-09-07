namespace IncidentCompass.Tester;

internal sealed record DemoActionGateResult(bool Passed, string Detail)
{
    private static readonly HashSet<string> ForbiddenEventTypes = new(StringComparer.Ordinal)
    {
        "ActionProposed",
        "ApprovalDecision",
        "ActionDispatchStarted",
        "ActionCompleted"
    };

    public static DemoActionGateResult Evaluate(FaultLedgerResponse ledger)
    {
        var forbidden = ledger.Events.FirstOrDefault(static item => ForbiddenEventTypes.Contains(item.EventType));
        return forbidden is null
            ? new(true, "no action lifecycle events observed")
            : new(false, "unexpected action lifecycle event " + forbidden.EventType);
    }

    public static async Task<DemoActionGateResult> ObserveAsync(
        int readCount,
        TimeSpan interval,
        Func<Guid, CancellationToken, Task<FaultLedgerResponse>> readLedger,
        Guid faultId,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < readCount; index++)
        {
            await Task.Delay(interval, cancellationToken);
            var gate = Evaluate(await readLedger(faultId, cancellationToken));
            if (!gate.Passed)
            {
                return gate;
            }
        }

        return new DemoActionGateResult(
            true,
            "no action lifecycle events observed across " + readCount + " bounded ledger reads");
    }
}
