using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace ForeignExchangeRateAPI.Domain;

/// <summary>
/// Foreign exchange rate with each currency pair (e.g., USD/EUR) and 2 prices [bid and ask]
/// </summary>
[Index(nameof(BaseCurrency), nameof(QuoteCurrency), IsUnique = true)]
public class ExchangeRate {
    [Key]
    public int Id { get; set; }

    public Currency BaseCurrency { get; set; }

    public Currency QuoteCurrency { get; set; }

    [Precision(18, 8)]
    public decimal Bid { get; set; }

    [Precision(18, 8)]
    public decimal Ask { get; set; }

    public bool Status { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}