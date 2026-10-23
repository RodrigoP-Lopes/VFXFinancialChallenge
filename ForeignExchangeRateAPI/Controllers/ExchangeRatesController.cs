using System.Text.Json;
using ForeignExchangeRateAPI.Data;
using ForeignExchangeRateAPI.Domain;
using ForeignExchangeRateAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ForeignExchangeRateAPI.Controllers;

[ApiController]
[Route("api/rates")]
public class ExchangeRatesController : ControllerBase {
    private readonly AppDbContext _dbContext;
    private readonly string _apiKey;

    private static DateTime Now => DateTime.SpecifyKind(new DateTime(DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond), DateTimeKind.Unspecified);

    public ExchangeRatesController(AppDbContext dbContext, IConfiguration configuration) {
        _dbContext = dbContext;
        _apiKey = configuration["ALPHA_VANTAGE_API_KEY"] ?? string.Empty;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ExchangeRate>>> GetRates() {
        return await _dbContext.ExchangeRates
            .Where(rate => rate.Status)
            .OrderBy(rate => rate.BaseCurrency)
            .ThenBy(rate => rate.QuoteCurrency)
            .ToListAsync();
    }

    [HttpGet("{baseCurrency}/{quoteCurrency}")]
    public async Task<ActionResult<ExchangeRate>> GetRate(string baseCurrency, string quoteCurrency) {
        var rate = await FindRate(baseCurrency, quoteCurrency);

        if (rate is not null) {
            return rate.Status ? rate : NotFound();
        }

        return await FetchAlphaVantageRate(baseCurrency, quoteCurrency);
    }

    [HttpPost]
    public async Task<ActionResult<ExchangeRate>> CreateRate(ExchangeRate request) {
        var baseCurrency = Normalize(request.BaseCurrency);
        var quoteCurrency = Normalize(request.QuoteCurrency);

        if (baseCurrency == quoteCurrency) {
            return BadRequest("Base and quote currencies must be different.");
        }

        var priceError = ValidatePrices(request);
        if (!string.IsNullOrEmpty(priceError)) {
            return BadRequest(priceError);
        }

        var existing = await _dbContext.ExchangeRates.FirstOrDefaultAsync(rate => rate.BaseCurrency == baseCurrency && rate.QuoteCurrency == quoteCurrency);

        if (existing is not null) {
            if (existing.Status) {
                return Conflict($"An exchange rate for {baseCurrency}/{quoteCurrency} already exists.");
            }

            existing.Bid = request.Bid;
            existing.Ask = request.Ask;
            existing.Status = true;
            existing.UpdatedAt = Now;
            await _dbContext.SaveChangesAsync();
            return existing;
        }

        await EnsureCurrenciesExistAsync(baseCurrency, quoteCurrency);

        var rate = new ExchangeRate {
            BaseCurrency = baseCurrency,
            QuoteCurrency = quoteCurrency,
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
    public async Task<ActionResult<ExchangeRate>> UpdateRate(string baseCurrency, string quoteCurrency, ExchangeRate request) {
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
    public async Task<IActionResult> DeleteRate(string baseCurrency, string quoteCurrency) {
        var rate = await FindRate(baseCurrency, quoteCurrency);

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

    private Task<ExchangeRate?> FindActiveRate(string baseCurrency, string quoteCurrency) {
        baseCurrency = Normalize(baseCurrency);
        quoteCurrency = Normalize(quoteCurrency);
        return _dbContext.ExchangeRates.FirstOrDefaultAsync(rate => rate.BaseCurrency == baseCurrency && rate.QuoteCurrency == quoteCurrency && rate.Status);
    }

    private Task<ExchangeRate?> FindRate(string baseCurrency, string quoteCurrency) {
        baseCurrency = Normalize(baseCurrency);
        quoteCurrency = Normalize(quoteCurrency);
        return _dbContext.ExchangeRates.FirstOrDefaultAsync(rate => rate.BaseCurrency == baseCurrency && rate.QuoteCurrency == quoteCurrency);
    }

    private async Task<ActionResult<ExchangeRate>> FetchAlphaVantageRate(string baseCurrency, string quoteCurrency) {
        baseCurrency = Normalize(baseCurrency);
        quoteCurrency = Normalize(quoteCurrency);

        string QUERY_URL = $"https://www.alphavantage.co/query?function=CURRENCY_EXCHANGE_RATE&from_currency={baseCurrency}&to_currency={quoteCurrency}&apikey={_apiKey}";
        Uri queryUri = new(QUERY_URL);

        AlphaVantageExchangeRateResponse? response;

        try {
            //Alpha Vantage example used WebClient but it is obsolete, so we use HttpClient instead
            using (HttpClient http = new()) {
                var json = await http.GetStringAsync(queryUri);
                response = JsonSerializer.Deserialize<AlphaVantageExchangeRateResponse>(json);
            }
        } catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException) {
            return Problem(detail: "The exchange rate provider is unavailable.");
        }

        if (response == null) {
            return Problem(detail: "The exchange rate provider returned an empty response.");
        }

        if (!string.IsNullOrEmpty(response.ErrorMessage)) {
            return Problem(detail: response.ErrorMessage);
        }

        if (!string.IsNullOrEmpty(response.Information)) {
            return Problem(detail: response.Information);
        }

        var quote = response.RealtimeCurrencyExchangeRate;

        if (quote == null) {
            return NotFound();
        }

        await EnsureCurrenciesExistAsync(baseCurrency, quoteCurrency);

        // We assume provider a valid bid, ask and lastRefreshed fields. Otherwise, we'd had tryParse validation here.
        var rate = new ExchangeRate {
            BaseCurrency = baseCurrency,
            QuoteCurrency = quoteCurrency,
            Bid = decimal.Parse(quote.BidPrice),
            Ask = decimal.Parse(quote.AskPrice),
            Status = true,
            CreatedAt = Now,
            UpdatedAt = DateTime.Parse(quote.LastRefreshed)
        };

        _dbContext.ExchangeRates.Add(rate);
        await _dbContext.SaveChangesAsync();
        return rate;
    }

    // We ensure the currencies exist in the Currencies table. If they don't, we add them
    private async Task EnsureCurrenciesExistAsync(string baseCurrency, string quoteCurrency) {
        await EnsureCurrencyExistAsync(baseCurrency);
        await EnsureCurrencyExistAsync(quoteCurrency);
    }

    private async Task EnsureCurrencyExistAsync(string currency) {
        var exists = await _dbContext.Currencies.AnyAsync(row => row.Code == currency);

        if (exists) {
            return;
        }

        _dbContext.Currencies.Add(new Currency {
            Code = currency,
            Status = true,
            CreatedAt = Now,
            UpdatedAt = Now
        });

        await _dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// Upper-cases a currency code so "usd" and "USD" are the same currency.
    /// </summary>
    private static string Normalize(string code) => code.ToUpperInvariant();

    private static string? ValidatePrices(ExchangeRate request) {
        if (request.Bid <= 0 || request.Ask <= 0) {
            return "Bid and ask must be greater than zero.";
        }

        return request.Ask < request.Bid ? "Ask must be greater than or equal to the bid." : null;
    }

}