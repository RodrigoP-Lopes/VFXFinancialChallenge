namespace ForeignExchangeRateAPI.Models;

/// <summary>
/// Body of <c>PUT /api/rates/{baseCurrency}/{quoteCurrency}</c>. The pair comes from the route,
/// so only the prices can be changed.
/// </summary>
public class UpdateExchangeRateRequest {
    /// <summary>Price at which the market buys the base currency.</summary>
    public decimal Bid { get; set; }

    /// <summary>Price at which the market sells the base currency.</summary>
    public decimal Ask { get; set; }
}
