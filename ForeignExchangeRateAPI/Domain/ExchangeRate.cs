using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

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

    /// <summary>
    /// Data is not deleted, but marked as inactive (soft delete). Inactive pairs are not fetched 
    /// </summary>
    public bool Status { get; set; } = true;

    [Column(TypeName = "timestamp without time zone")]
    public DateTime CreatedAt { get; set; }

    [Column(TypeName = "timestamp without time zone")]
    public DateTime UpdatedAt { get; set; }
}