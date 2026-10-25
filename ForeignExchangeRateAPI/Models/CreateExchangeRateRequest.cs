namespace ForeignExchangeRateAPI.Models;

/// <summary>
/// Body of <c>POST /api/rates</c>. Kept separate from the entity so a client cannot set the identifier,
/// the status or the timestamps.
/// </summary>
public class CreateExchangeRateRequest {
    /// <summary>ISO 4217 code of the currency being priced, for example USD.</summary>
    public string BaseCurrency { get; set; } = string.Empty;

    /// <summary>ISO 4217 code of the currency the price is expressed in, for example EUR.</summary>
    public string QuoteCurrency { get; set; } = string.Empty;

    /// <summary>Price at which the market buys the base currency.</summary>
    public decimal Bid { get; set; }

    /// <summary>Price at which the market sells the base currency.</summary>
    public decimal Ask { get; set; }
}
