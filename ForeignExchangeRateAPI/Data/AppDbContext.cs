using ForeignExchangeRateAPI.Domain;
using Microsoft.EntityFrameworkCore;

namespace ForeignExchangeRateAPI.Data;

public class AppDbContext : DbContext {
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) {
    }

    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        var rate = modelBuilder.Entity<ExchangeRate>();

        rate.Property(r => r.BaseCurrency).HasConversion<string>().HasMaxLength(3);
        rate.Property(r => r.QuoteCurrency).HasConversion<string>().HasMaxLength(3);
    }
}