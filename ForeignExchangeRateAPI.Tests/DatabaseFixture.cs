using ForeignExchangeRateAPI.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ForeignExchangeRateAPI.Tests;

public class DatabaseFixture : IDisposable {
    public AppDbContext Context { get; }

    public IConfiguration Configuration { get; }

    public DatabaseFixture() {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        Context = new AppDbContext(options);

        Configuration = new ConfigurationBuilder()
            .AddUserSecrets(typeof(AppDbContext).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();
    }

    public void Dispose() {
        Context.Database.EnsureDeleted();
        Context.Dispose();
    }
}
