namespace DaprTransactionalOutbox.Producer;

public interface IEventBus
{
    Task Publish<T>(string key, T eventData, CancellationToken cancellationToken = default);
}