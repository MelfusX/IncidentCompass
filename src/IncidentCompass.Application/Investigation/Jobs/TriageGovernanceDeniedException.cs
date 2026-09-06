namespace IncidentCompass.Application.Investigation.Jobs;

/// <summary>
/// Raised when backend governance refuses a worker tool call during an investigation attempt. Policy
/// grants and tool rules are attempt-invariant, so replaying the attempt would be denied again;
/// <see cref="TriageJobRunner"/> dead-letters instead of spending the retry budget. The denial itself
/// is already recorded as a durable policy decision in the triage ledger before this is thrown.
/// <see cref="ErrorCode"/> lands in the job's <c>last_error_code</c> column.
/// </summary>
internal sealed class TriageGovernanceDeniedException(string errorCode, string message) : Exception(message)
{
    public const string WorkerToolDeniedCode = "triage_governance_worker_tool_denied";

    public const string WorkerToolValidationFailedCode = "triage_governance_worker_tool_validation_failed";

    public string ErrorCode { get; } = errorCode;
}
