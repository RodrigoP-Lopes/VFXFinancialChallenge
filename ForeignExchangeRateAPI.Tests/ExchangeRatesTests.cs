using ForeignExchangeRateAPI.Controllers;
using ForeignExchangeRateAPI.Data;
using ForeignExchangeRateAPI.Domain;
using ForeignExchangeRateAPI.Events;
using ForeignExchangeRateAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace ForeignExchangeRateAPI.Tests;

public class ExchangeRatesTests : IClassFixture<DatabaseFixture>, IDisposable {

    private readonly DatabaseFixture _fixture;
    private readonly Mock<IExchangeRateEventPublisher> _publisher = new();
    private readonly AppDbContext _dbContext;
    private readonly ExchangeRatesController _controller;

    public ExchangeRatesTests(DatabaseFixture fixture) {
        _fixture = fixture;
        _dbContext = _fixture.CreateContext();
        _controller = new ExchangeRatesController(_dbContext, _fixture.Configuration, Mock.Of<ILogger<ExchangeRatesController>>(), _publisher.Object);
    }

    public void Dispose() => _dbContext.Dispose();

    #region GetRates

    /// <summary>
    /// Returns the stored rates ordered by base currency and then by quote currency
    /// </summary>
    [Fact]
    public async Task GetRates_ReturnsStoredRatesOrderedByPair() {
        await CreateRate("AED", "SAR", 1.02m, 1.03m);
        await CreateRate("USD", "EUR", 0.91m, 0.92m);

        var result = await _controller.GetRates();

        var ok = Assert.IsType<OkObjectResult>(result);
        var rates = Assert.IsAssignableFrom<IEnumerable<ExchangeRate>>(ok.Value).ToList();

        Assert.Contains(rates, rate => rate.BaseCurrency == "AED" && rate.QuoteCurrency == "SAR");
        Assert.Contains(rates, rate => rate.BaseCurrency == "USD" && rate.QuoteCurrency == "EUR");
        Assert.Equal(rates.OrderBy(rate => rate.BaseCurrency).ThenBy(rate => rate.QuoteCurrency), rates);
    }

    /// <summary>
    /// Returns an empty colletion on an empty database
    /// </summary>
    [Fact]
    public async Task GetRates_WhenNoRatesAreStored_ReturnsEmptyCollection() {
        var result = await _controller.GetRates();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<ExchangeRate>>(ok.Value));
    }

    #endregion

    #region GetRate

    /// <summary>
    /// Returns 404 for a soft deleted pair
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
    /// Returns 400 for a pair whose code is not three letters
    /// </summary>
    [Theory]
    [InlineData("US", "EUR")]
    [InlineData("USD", "EURO")]
    [InlineData("", "EUR")]
    public async Task GetRate_WithInvalidCurrencyCode_ReturnsBadRequest(string baseCurrency, string quoteCurrency) {
        var result = await _controller.GetRate(baseCurrency, quoteCurrency);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    #endregion

    #region CreateRate

    /// <summary>
    /// Creates a rate, returns the same record on a later read and raises the created event
    /// </summary>
    [Fact]
    public async Task CreateRate_PersistsRateAndPublishesEvent() {
        var result = await CreateRate("GBP", "JPY", 190.50m, 191.50m);

        var fetched = await _controller.GetRate("GBP", "JPY");
        var fetchedRate = Assert.IsType<ExchangeRate>(fetched.Value);

        _publisher.Verify(publisher => publisher.PublishAsync(It.Is<ExchangeRateCreatedEvent>(
            createdEvent => createdEvent.BaseCurrency == "GBP"
            && createdEvent.QuoteCurrency == "JPY"
            && createdEvent.Bid == 190.50m
            && createdEvent.Ask == 191.50m)));
    }

    /// <summary>
    /// Inserts the currencies that do not exist yet once
    /// </summary>
    [Fact]
    public async Task CreateRate_InsertsMissingCurrenciesAndReusesExistingOnes() {
        await CreateRate("CAD", "SEK", 6.50m, 6.60m);
        await CreateRate("CAD", "NOK", 7.40m, 7.50m);

        var codes = _dbContext.Currencies.Select(currency => currency.Code).ToList();

        Assert.Single(codes.Where(code => code == "CAD"));
        Assert.Single(codes.Where(code => code == "SEK"));
        Assert.Single(codes.Where(code => code == "NOK"));
    }

    /// <summary>
    /// Rejects a pair that is already active and raises no event
    /// </summary>
    [Fact]
    public async Task CreateRate_WhenPairIsActive_ReturnsConflictWithoutPublishingEvent() {
        await CreateRate("TRY", "INR", 3.01m, 3.02m);
        _publisher.Invocations.Clear();

        var result = await CreateRate("TRY", "INR", 3.10m, 3.11m);

        Assert.IsType<ConflictObjectResult>(result.Result);
        _publisher.Verify(publisher => publisher.PublishAsync(It.IsAny<ExchangeRateCreatedEvent>()), Times.Never);
    }

    /// <summary>
    /// Reactivates a soft deleted pair
    /// </summary>
    [Fact]
    public async Task CreateRate_WhenPairIsSoftDeleted_ReactivatesIt() {
        var created = await CreateRate("PLN", "CZK", 5.51m, 5.52m);
        var createdRate = Assert.IsType<ExchangeRate>(Assert.IsType<CreatedAtActionResult>(created.Result).Value);
        await _controller.DeleteRate("PLN", "CZK");

        var result = await CreateRate("PLN", "CZK", 5.61m, 5.62m);

        var reactivated = Assert.IsType<ExchangeRate>(result.Value);
        Assert.True(reactivated.Status);
        Assert.Equal(5.61m, reactivated.Bid);
        Assert.Equal(createdRate.Id, reactivated.Id);
        Assert.Single(_dbContext.ExchangeRates.Where(rate => rate.BaseCurrency == "PLN" && rate.QuoteCurrency == "CZK"));
    }

    /// <summary>
    /// Returns 400 when the pair or the prices are invalid
    /// </summary>
    [Theory]
    [InlineData("USDD", "EUR", 0.91, 0.92)]  // base code is not three letters
    [InlineData("gbp", "GBP", 1, 1.10)]      // base and quote are the same currency
    [InlineData("BBD", "TTD", 1.50, 1.40)]   // ask below the bid
    [InlineData("BBD", "TTD", 0, 0.50)]      // bid is not positive
    [InlineData("BBD", "TTD", 1, -0.50)]     // ask is not positive
    public async Task CreateRate_WithInvalidRequest_ReturnsBadRequest(string baseCurrency, string quoteCurrency, decimal bid, decimal ask) {
        var result = await CreateRate(baseCurrency, quoteCurrency, bid, ask);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    /// <summary>
    /// Codes are trimmed and upper-cased, so a lower case request is stored and read back in the canonical form
    /// </summary>
    [Fact]
    public async Task CreateRate_NormalizesCurrencyCodes() {
        var created = await CreateRate(" cad ", "nzd", 1.20m, 1.21m);

        var createdRate = Assert.IsType<ExchangeRate>(Assert.IsType<CreatedAtActionResult>(created.Result).Value);
        Assert.Equal("CAD", createdRate.BaseCurrency);
        Assert.Equal("NZD", createdRate.QuoteCurrency);

        var fetched = await _controller.GetRate("cad", "nzd");

        Assert.Equal("CAD", Assert.IsType<ExchangeRate>(fetched.Value).BaseCurrency);
    }

    #endregion

    #region UpdateRate

    /// <summary>
    /// Updates the bid and ask of an existing rate
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
    /// Returns 404 when the pair was never stored
    /// </summary>
    [Fact]
    public async Task UpdateRate_WhenPairIsMissing_ReturnsNotFound() {
        var result = await _controller.UpdateRate("ISK", "PHP", new UpdateExchangeRateRequest { Bid = 1m, Ask = 1.10m });

        Assert.IsType<NotFoundResult>(result.Result);
    }

    /// <summary>
    /// Returns 404 when the pair was soft deleted when trying to update it
    /// </summary>
    [Fact]
    public async Task UpdateRate_WhenPairIsSoftDeleted_ReturnsNotFound() {
        await CreateRate("ILS", "EGP", 10.10m, 10.20m);
        await _controller.DeleteRate("ILS", "EGP");

        var result = await _controller.UpdateRate("ILS", "EGP", new UpdateExchangeRateRequest { Bid = 10.30m, Ask = 10.40m });

        Assert.IsType<NotFoundResult>(result.Result);
    }

    /// <summary>
    /// Returns 400 for a pair whose code is not three letters
    /// </summary>
    [Theory]
    [InlineData("USDD", "EUR")]
    [InlineData("USD", "")]
    public async Task UpdateRate_WithInvalidCurrencyCode_ReturnsBadRequest(string baseCurrency, string quoteCurrency) {
        var result = await _controller.UpdateRate(baseCurrency, quoteCurrency, new UpdateExchangeRateRequest { Bid = 1m, Ask = 1.10m });

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    #endregion

    #region DeleteRate

    /// <summary>
    /// Returns 404 when the pair was never stored
    /// </summary>
    [Fact]
    public async Task DeleteRate_WhenPairIsMissing_ReturnsNotFound() {
        var result = await _controller.DeleteRate("VND", "NGN");

        Assert.IsType<NotFoundResult>(result);
    }

    /// <summary>
    /// Deleting an inactive pair again is a no-op that still reports success
    /// </summary>
    [Fact]
    public async Task DeleteRate_WhenAlreadyDeleted_ReturnsOk() {
        await CreateRate("ARS", "CLP", 900.10m, 900.20m);
        await _controller.DeleteRate("ARS", "CLP");

        var result = await _controller.DeleteRate("ARS", "CLP");

        Assert.IsType<OkResult>(result);
    }

    /// <summary>
    /// Returns 400 for a pair whose code is not three letters
    /// </summary>
    [Fact]
    public async Task DeleteRate_WithInvalidCurrencyCode_ReturnsBadRequest() {
        var result = await _controller.DeleteRate("E", "EUR");

        Assert.IsType<BadRequestObjectResult>(result);
    }

    #endregion

    #region Helpers

    private Task<ActionResult<ExchangeRate>> CreateRate(string baseCurrency, string quoteCurrency, decimal bid, decimal ask) {
        return _controller.CreateRate(new CreateExchangeRateRequest {
            BaseCurrency = baseCurrency,
            QuoteCurrency = quoteCurrency,
            Bid = bid,
            Ask = ask
        });
    }

    #endregion
}