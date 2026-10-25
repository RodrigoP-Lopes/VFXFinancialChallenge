using ForeignExchangeRateAPI.Controllers;
using ForeignExchangeRateAPI.Domain;
using ForeignExchangeRateAPI.Events;
using ForeignExchangeRateAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace ForeignExchangeRateAPI.Tests;

public class ExchangeRatesTests : IClassFixture<DatabaseFixture> {

    private readonly DatabaseFixture _fixture;
    private readonly Mock<IExchangeRateEventPublisher> _publisher = new();
    private readonly ExchangeRatesController _controller;

    public ExchangeRatesTests(DatabaseFixture fixture) {
        _fixture = fixture;
        _controller = new ExchangeRatesController(_fixture.Context, _fixture.Configuration, Mock.Of<ILogger<ExchangeRatesController>>(), _publisher.Object);
    }

    /// <summary>
    /// Returns the rates currently stored in the database.
    /// </summary>
    [Fact]
    public async Task GetRates_ReturnsStoredRates() {
        await CreateRate("USD", "EUR", 0.91m, 0.92m);

        var result = await _controller.GetRates();

        var ok = Assert.IsType<OkObjectResult>(result);
        var rates = Assert.IsAssignableFrom<IEnumerable<ExchangeRate>>(ok.Value);
        Assert.Contains(rates, rate => rate.BaseCurrency == "USD" && rate.QuoteCurrency == "EUR");
    }

    /// <summary>
    /// Creates a rate and returns the same record on a later read.
    /// </summary>
    [Fact]
    public async Task CreateRate_PersistsRate() {
        var result = await CreateRate("GBP", "JPY", 190.50m, 191.50m);

        var createdAt = Assert.IsType<CreatedAtActionResult>(result.Result);
        var created = Assert.IsType<ExchangeRate>(createdAt.Value);
        var fetched = await _controller.GetRate("GBP", "JPY");
        var fetchedRate = Assert.IsType<ExchangeRate>(fetched.Value);

        Assert.True(created.Id > 0);
        Assert.Equal(created, fetchedRate);
    }

    /// <summary>
    /// Updates the bid and ask of an existing rate.
    /// </summary>
    [Fact]
    public async Task UpdateRate_ChangesPrices() {
        await CreateRate("EUR", "GBP", 0.85m, 0.86m);

        var result = await _controller.UpdateRate("EUR", "GBP", new UpdateExchangeRateRequest { Bid = 0.87m, Ask = 0.88m });

        var updated = Assert.IsType<ExchangeRate>(result.Value);
        Assert.Equal(0.87m, updated.Bid);
        Assert.Equal(0.88m, updated.Ask);
    }

    /// <summary>
    /// Soft deletes a rate so it no longer appears in the list.
    /// </summary>
    [Fact]
    public async Task DeleteRate_RemovesRateFromList() {
        await CreateRate("CNY", "JPY", 20.10m, 20.20m);

        var result = await _controller.DeleteRate("CNY", "JPY");

        Assert.IsType<NoContentResult>(result);

        var rates = await _controller.GetRates();
        var okRates = Assert.IsType<OkObjectResult>(rates);
        var stored = Assert.IsAssignableFrom<IEnumerable<ExchangeRate>>(okRates.Value);
        Assert.DoesNotContain(stored, rate => rate.BaseCurrency == "CNY" && rate.QuoteCurrency == "JPY");
    }

    /// <summary>
    /// Ensures that when a new rate is created with currencies that do not exist in the database, those currencies are also inserted.
    /// </summary>
    [Fact]
    public async Task CreateRate_WithNewCurrencies_InsertsTheCurrencies() {

        await CreateRate("CAD", "SEK", 6.50m, 6.60m);

        var codes = _fixture.Context.Currencies.Select(currency => currency.Code).ToList();
        Assert.Contains("CAD", codes);
        Assert.Contains("SEK", codes);
    }

    /// <summary>
    /// Rejects a pair that is already active.
    /// </summary>
    [Fact]
    public async Task CreateRate_WhenPairIsActive_ReturnsConflict() {

        await CreateRate("TRY", "INR", 3.01m, 3.02m);

        var result = await CreateRate("TRY", "INR", 3.10m, 3.11m);

        Assert.IsType<ConflictObjectResult>(result.Result);
    }

    /// <summary>
    /// Returns 404 for a soft deleted pair without fetching it from the provider again.
    /// </summary>
    [Fact]
    public async Task GetRate_WhenSoftDeleted_ReturnsNotFound()
    {
        await CreateRate("HUF", "RON", 0.0123m, 0.0124m);
        await _controller.DeleteRate("HUF", "RON");

        var result = await _controller.GetRate("HUF", "RON");

        Assert.IsType<NotFoundResult>(result.Result);
    }

    /// <summary>
    /// Reactivates a soft deleted pair instead of inserting a second row.
    /// </summary>
    [Fact]
    public async Task CreateRate_WhenPairIsSoftDeleted_ReactivatesIt() {
        var created = await CreateRate("PLN", "CZK", 5.51m, 5.52m);
        await _controller.DeleteRate("PLN", "CZK");

        var result = await CreateRate("PLN", "CZK", 5.61m, 5.62m);

        var reactivated = Assert.IsType<ExchangeRate>(result.Value);
        Assert.True(reactivated.Status);
        Assert.Equal(5.61m, reactivated.Bid);
    }

    /// <summary>
    /// Returns 400 for a currency code that is not three letters
    /// </summary>
    [Fact]
    public async Task CreateRate_WithInvalidCurrencyCode_ReturnsBadRequest() {
        var result = await CreateRate("USDD", "EUR", 0.91m, 0.92m);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    /// <summary>
    /// Returns 400 when the ask is lower than the bid.
    /// </summary>
    [Fact]
    public async Task CreateRate_WithAskBelowBid_ReturnsBadRequest() {
        var result = await CreateRate("JPY", "KRW", 1.50m, 1.40m);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    private Task<ActionResult<ExchangeRate>> CreateRate(string baseCurrency, string quoteCurrency, decimal bid, decimal ask) {
        return _controller.CreateRate(new CreateExchangeRateRequest {
            BaseCurrency = baseCurrency,
            QuoteCurrency = quoteCurrency,
            Bid = bid,
            Ask = ask
        });
    }
}