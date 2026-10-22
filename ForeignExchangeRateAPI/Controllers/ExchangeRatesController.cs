using ForeignExchangeRateAPI.Data;
using ForeignExchangeRateAPI.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ForeignExchangeRateAPI.Controllers;

[ApiController]
[Route("api/rates")]
public class ExchangeRatesController : ControllerBase {
    private readonly AppDbContext _dbContext;

    public ExchangeRatesController(AppDbContext dbContext) => _dbContext = dbContext;

    private static DateTime Now => DateTime.UtcNow.AddHours(1);

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ExchangeRate>>> GetRates() {
        return await _dbContext.ExchangeRates
            .Where(rate => rate.Status)
            .OrderBy(rate => rate.BaseCurrency)
            .ThenBy(rate => rate.QuoteCurrency)
            .ToListAsync();
    }

    [HttpGet("{baseCurrency}/{quoteCurrency}")]
    public async Task<ActionResult<ExchangeRate>> GetRate(Currency baseCurrency, Currency quoteCurrency) {
        var rate = await FindActiveRate(baseCurrency, quoteCurrency);
        return rate is null ? NotFound() : rate;
    }

    [HttpPost]
    public async Task<ActionResult<ExchangeRate>> CreateRate(ExchangeRate request) {
        if (request.BaseCurrency == request.QuoteCurrency) {
            return BadRequest("Base and quote currencies must be different.");
        }

        var priceError = ValidatePrices(request);
        if (!string.IsNullOrEmpty(priceError)) {
            return BadRequest(priceError);
        }

        var existing = await _dbContext.ExchangeRates.FirstOrDefaultAsync(rate => rate.BaseCurrency == request.BaseCurrency && rate.QuoteCurrency == request.QuoteCurrency);

        if (existing is not null) {
            if (existing.Status) {
                return Conflict($"An exchange rate for {request.BaseCurrency}/{request.QuoteCurrency} already exists.");
            }

            existing.Bid = request.Bid;
            existing.Ask = request.Ask;
            existing.Status = true;
            existing.UpdatedAt = Now;
            await _dbContext.SaveChangesAsync();
            return existing;
        }

        var rate = new ExchangeRate {
            BaseCurrency = request.BaseCurrency,
            QuoteCurrency = request.QuoteCurrency,
            Bid = request.Bid,
            Ask = request.Ask,
            Status = true,
            CreatedAt = Now,
            UpdatedAt = Now
        };

        _dbContext.ExchangeRates.Add(rate);
        await _dbContext.SaveChangesAsync();
        return rate;
    }

    [HttpPut("{baseCurrency}/{quoteCurrency}")]
    public async Task<ActionResult<ExchangeRate>> UpdateRate(Currency baseCurrency, Currency quoteCurrency, ExchangeRate request) {
        var priceError = ValidatePrices(request);
        if (!string.IsNullOrEmpty(priceError)) {
            return BadRequest(priceError);
        }

        var rate = await FindActiveRate(baseCurrency, quoteCurrency);
        if (rate is null) {
            return NotFound();
        }

        rate.Bid = request.Bid;
        rate.Ask = request.Ask;
        rate.UpdatedAt = Now;
        await _dbContext.SaveChangesAsync();
        return rate;
    }

    [HttpDelete("{baseCurrency}/{quoteCurrency}")]
    public async Task<IActionResult> DeleteRate(Currency baseCurrency, Currency quoteCurrency) {
        var rate = await _dbContext.ExchangeRates.FirstOrDefaultAsync(item => item.BaseCurrency == baseCurrency && item.QuoteCurrency == quoteCurrency);

        if (rate is null) {
            return NotFound();
        }

        if (rate.Status) {
            rate.Status = false;
            rate.UpdatedAt = Now;
            await _dbContext.SaveChangesAsync();
        }

        return Ok();
    }

    private Task<ExchangeRate?> FindActiveRate(Currency baseCurrency, Currency quoteCurrency) =>
        _dbContext.ExchangeRates.FirstOrDefaultAsync(rate => rate.BaseCurrency == baseCurrency && rate.QuoteCurrency == quoteCurrency && rate.Status);

    private static string? ValidatePrices(ExchangeRate request) {
        if (request.Bid <= 0 || request.Ask <= 0) {
            return "Bid and ask must be greater than zero.";
        }

        return request.Ask < request.Bid ? "Ask must be greater than or equal to the bid." : null;
    }

}