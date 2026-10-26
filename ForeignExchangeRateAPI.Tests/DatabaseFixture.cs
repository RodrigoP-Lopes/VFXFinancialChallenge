using ForeignExchangeRateAPI.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ForeignExchangeRateAPI.Tests;

/// <summary>
/// Gives each test its own in-memory database and the API's configuration.
/// </summary>
public class DatabaseFixture {
    public IConfiguration Configuration { get; }

    public DatabaseFixture() {
        Configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();
    }

    /// <summary>
    /// Creates a context on a database of its own, so tests never see each other's data.
    /// </summary>
    public AppDbContext CreateContext() {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }
}
