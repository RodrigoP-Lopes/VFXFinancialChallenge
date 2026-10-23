using System.Net;
using ForeignExchangeRateAPI.Controllers;
using ForeignExchangeRateAPI.Domain;
using Microsoft.AspNetCore.Mvc;

namespace ForeignExchangeRateAPI.Tests;

public class ExchangeRatesTests : IClassFixture<DatabaseFixture> {

    private readonly DatabaseFixture _fixture;
    private readonly ExchangeRatesController _controller;

    public ExchangeRatesTests(DatabaseFixture fixture) {
        _fixture = fixture;
        _controller = new ExchangeRatesController(_fixture.Context, _fixture.Configuration);
    }

    [Fact]
    public async Task GetRates_ReturnsStoredRates() {
        await CreateRate("USD", "EUR", 0.91m, 0.92m);

        var result = await _controller.GetRates();

        var rates = Assert.IsAssignableFrom<IEnumerable<ExchangeRate>>(result.Value);
        Assert.Contains(rates, rate => rate.BaseCurrency == "USD" && rate.QuoteCurrency == "EUR");
    }

    [Fact]
    public async Task CreateRate_PersistsRate() {
        var result = await CreateRate("GBP", "JPY", 190.50m, 191.50m);

        var created = Assert.IsType<ExchangeRate>(result.Value);
        var fetched = await _controller.GetRate("GBP", "JPY");
        var fetchedRate = Assert.IsType<ExchangeRate>(fetched.Value);

        Assert.True(created.Id > 0);
        Assert.Equal(created, fetchedRate);
    }

    [Fact]
    public async Task UpdateRate_ChangesPrices() {
        await CreateRate("EUR", "GBP", 0.85m, 0.86m);

        var result = await _controller.UpdateRate("EUR", "GBP", CreateExchangeRate("EUR", "GBP", 0.87m, 0.88m));

        var updated = Assert.IsType<ExchangeRate>(result.Value);
        Assert.Equal(0.87m, updated.Bid);
        Assert.Equal(0.88m, updated.Ask);
    }

    [Fact]
    public async Task DeleteRate_RemovesRateFromList() {
        await CreateRate("CNY", "JPY", 20.10m, 20.20m);

        var result = await _controller.DeleteRate("CNY", "JPY");

        Assert.IsType<OkResult>(result);

        var rates = await _controller.GetRates();
        var stored = Assert.IsAssignableFrom<IEnumerable<ExchangeRate>>(rates.Value);
        Assert.DoesNotContain(stored, rate => rate.BaseCurrency == "CNY" && rate.QuoteCurrency == "JPY");
    }

    [Fact]
    public async Task CreateRate_WithNewCurrencies_InsertsTheCurrencies() {

        await CreateRate("CAD", "SEK", 6.50m, 6.60m);

        var codes = _fixture.Context.Currencies.Select(currency => currency.Code).ToList();
        Assert.Contains("CAD", codes);
        Assert.Contains("SEK", codes);
    }

    [Fact]
    public async Task GetRate_WhenNotStored_FetchesFromProviderAndStoresIt() {
        var result = await _controller.GetRate("BRL", "GBP");

        var rate = Assert.IsType<ExchangeRate>(result.Value);
        Assert.True(rate.Id > 0);
        Assert.NotNull(rate.Bid);
        Assert.NotNull(rate.Ask);
        Assert.Contains(_fixture.Context.ExchangeRates, stored => stored.BaseCurrency == "BRL" && stored.QuoteCurrency == "GBP");
    }

    private Task<ActionResult<ExchangeRate>> CreateRate(string baseCurrency, string quoteCurrency, decimal bid, decimal ask) {
        return _controller.CreateRate(CreateExchangeRate(baseCurrency, quoteCurrency, bid, ask));
    }

    private static ExchangeRate CreateExchangeRate(string baseCurrency, string quoteCurrency, decimal bid, decimal ask) {
        return new ExchangeRate {
            BaseCurrency = baseCurrency,
            QuoteCurrency = quoteCurrency,
            Bid = bid,
            Ask = ask
        };
    }
}