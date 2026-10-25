using ForeignExchangeRateAPI.Domain;
using Microsoft.EntityFrameworkCore;

namespace ForeignExchangeRateAPI.Data;

public class AppDbContext : DbContext {
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) {
    }

    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();

    public DbSet<Currency> Currencies => Set<Currency>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        var rate = modelBuilder.Entity<ExchangeRate>();

        rate.Property(r => r.BaseCurrency).HasMaxLength(3);
        rate.Property(r => r.QuoteCurrency).HasMaxLength(3);

        rate.HasOne<Currency>()
            .WithMany()
            .HasForeignKey(r => r.BaseCurrency)
            .OnDelete(DeleteBehavior.Restrict);

        rate.HasOne<Currency>()
            .WithMany()
            .HasForeignKey(r => r.QuoteCurrency)
            .OnDelete(DeleteBehavior.Restrict);
    }
}