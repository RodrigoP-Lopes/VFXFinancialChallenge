namespace ForeignExchangeRateAPI.Events;

/// <summary>
/// Announces that a new exchange rate was stored.
/// </summary>
public record ExchangeRateCreatedEvent(string BaseCurrency, string QuoteCurrency, decimal Bid, decimal Ask, DateTime CreatedAt);
