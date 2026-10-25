using System.Text.Json;
using RabbitMQ.Client;

namespace ForeignExchangeRateAPI.Events;

/// <summary>
/// Publishes exchange rate events to a RabbitMQ queue.
/// </summary>
public sealed class RabbitMqExchangeRateEventPublisher : IExchangeRateEventPublisher, IDisposable {
    private const string QueueName = "exchange-rates";
    private readonly string _uri;
    private readonly ILogger<RabbitMqExchangeRateEventPublisher> _logger;
    private IConnection? _connection;
    private IChannel? _channel;

    public RabbitMqExchangeRateEventPublisher(IConfiguration configuration, ILogger<RabbitMqExchangeRateEventPublisher> logger) {
        _logger = logger;
        _uri = $"amqp://vfx:{configuration["RABBITMQ_PASSWORD"]}@localhost:5672";
    }

    public async Task PublishAsync(ExchangeRateCreatedEvent createdEvent) {
        try {
            if (_channel == null || !_channel.IsOpen) {
                _connection ??= await new ConnectionFactory { Uri = new Uri(_uri) }.CreateConnectionAsync();
                _channel = await _connection.CreateChannelAsync();

                await _channel.QueueDeclareAsync(QueueName, true, false, false, null);
            }

            var body = JsonSerializer.SerializeToUtf8Bytes(createdEvent);

            await _channel.BasicPublishAsync(string.Empty, QueueName, false, new BasicProperties { Persistent = true }, body);

            _logger.LogInformation("Published rate created event for {BaseCurrency}/{QuoteCurrency} to queue {Queue}.", createdEvent.BaseCurrency, createdEvent.QuoteCurrency, QueueName);
        } catch (Exception exception) {
            _logger.LogError(exception, "Failed to publish rate created event for {BaseCurrency}/{QuoteCurrency}.", createdEvent.BaseCurrency, createdEvent.QuoteCurrency);
        }
    }

    public void Dispose() {
        _channel?.Dispose();
        _connection?.Dispose();
    }
}
