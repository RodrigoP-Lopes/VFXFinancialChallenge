namespace ForeignExchangeRateAPI.Events;

/// <summary>
/// Publishes exchange rate events to a message queue.
/// </summary>
public interface IExchangeRateEventPublisher {
    Task PublishAsync(ExchangeRateCreatedEvent @event);
}
