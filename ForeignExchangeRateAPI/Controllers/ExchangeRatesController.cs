using ForeignExchangeRateAPI.Data;
using ForeignExchangeRateAPI.Domain;
using ForeignExchangeRateAPI.Events;
using ForeignExchangeRateAPI.Models;
using ForeignExchangeRateAPI.Services;
using Microsoft.AspNetCore.Mvc;

namespace ForeignExchangeRateAPI.Controllers;

/// <summary>
/// CRUD endpoints for exchange rates
/// </summary>
[ApiController]
[Route("api/rates")]
public class ExchangeRatesController : ControllerBase {
    private readonly ExchangeRateService _exchangeRateService;

    public ExchangeRatesController(AppDbContext dbContext, IConfiguration configuration, ILogger<ExchangeRatesController> logger, IExchangeRateEventPublisher eventPublisher) {
        _exchangeRateService = new ExchangeRateService(dbContext, configuration, logger, eventPublisher);
    }

    /// <summary>
    /// Gets all active exchange rates, ordered by base currency and then quote currency
    /// </summary>
    /// <returns></returns>
    [HttpGet]
    public async Task<ActionResult> GetRates() {
        return Ok(await _exchangeRateService.GetActiveRatesAsync());
    }

    /// <summary>
    /// Gets the exchange rate for a specific currency pair. If the pair does not exist in the database, it fetches the rate from Alpha Vantage API and stores it in the database.
    /// </summary>
    /// <param name="baseCurrency"></param>
    /// <param name="quoteCurrency"></param>
    /// <returns></returns>
    [HttpGet("{baseCurrency}/{quoteCurrency}")]
    public async Task<ActionResult<ExchangeRate>> GetRate(string baseCurrency, string quoteCurrency) {
        return await _exchangeRateService.GetRateAsync(baseCurrency, quoteCurrency);
    }

    /// <summary>
    /// Creates a new exchange rate for a currency pair, if it does not already exist. If the pair exists but is inactive, it reactivates it and updates the prices.
    /// </summary>
    /// <param name="request"></param>
    /// <returns></returns>
    [HttpPost]
    public async Task<ActionResult<ExchangeRate>> CreateRate(CreateExchangeRateRequest request) {
        return await _exchangeRateService.CreateRateAsync(request);
    }

    /// <summary>
    /// Updates the bid and ask prices for an existing exchange rate
    /// </summary>
    /// <param name="baseCurrency"></param>
    /// <param name="quoteCurrency"></param>
    /// <param name="request"></param>
    /// <returns></returns>
    [HttpPut("{baseCurrency}/{quoteCurrency}")]
    public async Task<ActionResult<ExchangeRate>> UpdateRate(string baseCurrency, string quoteCurrency, UpdateExchangeRateRequest request) {
        return await _exchangeRateService.UpdateRateAsync(baseCurrency, quoteCurrency, request);
    }

    /// <summary>
    /// Soft deletes an exchange rate by marking it as inactive
    /// </summary>
    /// <param name="baseCurrency"></param>
    /// <param name="quoteCurrency"></param>
    /// <returns></returns>
    [HttpDelete("{baseCurrency}/{quoteCurrency}")]
    public async Task<IActionResult> DeleteRate(string baseCurrency, string quoteCurrency) {
        return await _exchangeRateService.DeleteRateAsync(baseCurrency, quoteCurrency);
    }
}
