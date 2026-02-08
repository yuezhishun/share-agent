namespace PtyAgent.Api.Orchestration;

public interface IOrchestrationEngine
{
    Task RunTaskAsync(Guid taskId, CancellationToken cancellationToken);
}
