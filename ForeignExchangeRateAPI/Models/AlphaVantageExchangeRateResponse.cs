using System.Text.Json.Serialization;

namespace ForeignExchangeRateAPI.Models;

/// <summary>
/// Alpha Vantage response for the CURRENCY_EXCHANGE_RATE endpoint
/// </summary>
public class AlphaVantageExchangeRateResponse {
    [JsonPropertyName("Realtime Currency Exchange Rate")]
    public AlphaVantageExchangeRate? RealtimeCurrencyExchangeRate { get; set; }

    [JsonPropertyName("Error Message")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("Information")]
    public string? Information { get; set; }
}

public class AlphaVantageExchangeRate {
    [JsonPropertyName("1. From_Currency Code")]
    public string? FromCurrencyCode { get; set; }

    [JsonPropertyName("2. From_Currency Name")]
    public string? FromCurrencyName { get; set; }

    [JsonPropertyName("3. To_Currency Code")]
    public string? ToCurrencyCode { get; set; }

    [JsonPropertyName("4. To_Currency Name")]
    public string? ToCurrencyName { get; set; }

    [JsonPropertyName("5. Exchange Rate")]
    public string? ExchangeRate { get; set; }

    [JsonPropertyName("6. Last Refreshed")]
    public string? LastRefreshed { get; set; }

    [JsonPropertyName("7. Time Zone")]
    public string? TimeZone { get; set; }

    [JsonPropertyName("8. Bid Price")]
    public string? BidPrice { get; set; }

    [JsonPropertyName("9. Ask Price")]
    public string? AskPrice { get; set; }
}
