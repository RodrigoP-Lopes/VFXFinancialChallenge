using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ForeignExchangeRateAPI.Domain;

/// <summary>
/// Currencies with ISO 4217 code
/// </summary>
[Table("Currencies")]
public class Currency {
    [Key]
    [MaxLength(3)]
    public string Code { get; set; } = string.Empty;

    public bool Status { get; set; } = true;

    [Column(TypeName = "timestamp without time zone")]
    public DateTime CreatedAt { get; set; }

    [Column(TypeName = "timestamp without time zone")]
    public DateTime UpdatedAt { get; set; }
}