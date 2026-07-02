namespace IncidentCompass.Application.Intake.FaultGrouping;

public interface IIntakeUnitOfWork
{
    Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken);
}
