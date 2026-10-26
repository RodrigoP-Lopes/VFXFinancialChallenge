using System.Text.Json;
using ForeignExchangeRateAPI.Data;
using ForeignExchangeRateAPI.Domain;
using ForeignExchangeRateAPI.Events;
using ForeignExchangeRateAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace ForeignExchangeRateAPI.Services;

/// <summary>
/// Exchange rate logic behind the endpoints: reading, creating, updating, deleting and fetching missing rates.
/// </summary>
public class ExchangeRateService {
    private readonly AppDbContext _dbContext;
    private readonly string _apiKey;
    private readonly ILogger _logger;
    private readonly IExchangeRateEventPublisher _eventPublisher;
    //Alpha Vantage example used WebClient but it is obsolete, so we use HttpClient instead
    private readonly HttpClient _http;

    private static DateTime Now => new (DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond);

    public ExchangeRateService(AppDbContext dbContext, IConfiguration configuration, ILogger logger, IExchangeRateEventPublisher eventPublisher) {
        _dbContext = dbContext;
        _apiKey = configuration["ALPHA_VANTAGE_API_KEY"] ?? string.Empty;
        _logger = logger;
        _eventPublisher = eventPublisher;
        _http = new HttpClient();
    }

    #region GetRates

    /// <summary>
    /// Gets all active exchange rates, ordered by base currency and then quote currency
    /// </summary>
    public async Task<IReadOnlyList<ExchangeRate>> GetActiveRatesAsync() {
        var rates = await _dbContext.ExchangeRates
            .AsNoTracking()
            .Where(rate => rate.Status)
            .OrderBy(rate => rate.BaseCurrency)
            .ThenBy(rate => rate.QuoteCurrency)
            .ToListAsync();

        if (rates.Count == 0) {
            _logger.LogInformation("No exchange rates found.");
        } else {
            _logger.LogInformation("Returning {Count} exchange rates.", rates.Count);
        }

        return rates;
    }

    #endregion

    #region GetRate

    /// <summary>
    /// Gets the exchange rate for a specific currency pair. If the pair does not exist in the database, it fetches the rate from Alpha Vantage API and stores it in the database.
    /// </summary>
    public async Task<ActionResult<ExchangeRate>> GetRateAsync(string baseCurrency, string quoteCurrency) {
        var normalizedBase = NormalizeCurrencyCode(baseCurrency);
        var normalizedQuote = NormalizeCurrencyCode(quoteCurrency);

        if (normalizedBase is null || normalizedQuote is null) {
            return BadRequest("Currency codes must be three letters.");
        }

        var rate = await FindRate(normalizedBase, normalizedQuote);

        if (rate is not null) {
            _logger.LogDebug("Rate {BaseCurrency}/{QuoteCurrency} served from the database with status {Status}.", rate.BaseCurrency, rate.QuoteCurrency, rate.Status);
            return rate.Status ? rate : NotFound();
        }

        return await FetchAlphaVantageRate(normalizedBase, normalizedQuote);
    }

    #endregion

    #region Alpha Vantage

    /// <summary>
    /// Fetches the exchange rate from Alpha Vantage API and stores it in the database. If the currencies do not exist, they are created.
    /// </summary>
    private async Task<ActionResult<ExchangeRate>> FetchAlphaVantageRate(string baseCurrency, string quoteCurrency) {
        _logger.LogInformation("Rate {BaseCurrency}/{QuoteCurrency} is not stored; calling Alpha Vantage.", baseCurrency, quoteCurrency);

        string QUERY_URL = $"https://www.alphavantage.co/query?function=CURRENCY_EXCHANGE_RATE&from_currency={baseCurrency}&to_currency={quoteCurrency}&apikey={_apiKey}";
        Uri queryUri = new(QUERY_URL);

        AlphaVantageExchangeRateResponse? response;

        try {
            var json = await _http.GetStringAsync(queryUri);
            response = JsonSerializer.Deserialize<AlphaVantageExchangeRateResponse>(json);
        } catch (JsonException exception) {
            _logger.LogError(exception, "Alpha Vantage returned an unreadable payload for {BaseCurrency}/{QuoteCurrency}.", baseCurrency, quoteCurrency);
            return Problem(statusCode: StatusCodes.Status502BadGateway, detail: "The exchange rate provider returned an unreadable response.");
        } catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException) {
            _logger.LogError(exception, "Alpha Vantage request for {BaseCurrency}/{QuoteCurrency} failed.", baseCurrency, quoteCurrency);
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, detail: "The exchange rate provider is unavailable.");
        }

        if (response == null) {
            _logger.LogWarning("Alpha Vantage returned an empty response for {BaseCurrency}/{QuoteCurrency}.", baseCurrency, quoteCurrency);
            return Problem(statusCode: StatusCodes.Status502BadGateway, detail: "The exchange rate provider returned an empty response.");
        }

        if (!string.IsNullOrEmpty(response.ErrorMessage)) {
            _logger.LogWarning("Alpha Vantage rejected {BaseCurrency}/{QuoteCurrency}: {ProviderMessage}", baseCurrency, quoteCurrency, response.ErrorMessage);
            return Problem(statusCode: StatusCodes.Status502BadGateway, detail: "The exchange rate provider rejected the request.");
        }

        if (!string.IsNullOrEmpty(response.Information)) {
            _logger.LogWarning("Alpha Vantage quota or key problem for {BaseCurrency}/{QuoteCurrency}: {ProviderMessage}", baseCurrency, quoteCurrency, response.Information);
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, detail: "The exchange rate provider is temporarily unavailable.");
        }

        var quote = response.RealtimeCurrencyExchangeRate;

        if (quote == null) {
            _logger.LogWarning("Alpha Vantage returned no quote for {BaseCurrency}/{QuoteCurrency}.", baseCurrency, quoteCurrency);
            return NotFound();
        }

        await EnsureCurrenciesExistAsync(baseCurrency, quoteCurrency);

        // We assume provider a valid bid, ask and lastRefreshed fields but we check anyway
        if (!decimal.TryParse(quote.BidPrice, out var bid) || !decimal.TryParse(quote.AskPrice, out var ask) || !DateTime.TryParse(quote.LastRefreshed, out var lastRefreshed)) {
            _logger.LogWarning("Alpha Vantage returned an unreadable rate for {BaseCurrency}/{QuoteCurrency}: bid {Bid}, ask {Ask}, last refreshed {LastRefreshed}.", baseCurrency, quoteCurrency, quote.BidPrice, quote.AskPrice, quote.LastRefreshed);
            return Problem(statusCode: StatusCodes.Status502BadGateway, detail: "The exchange rate provider returned an invalid data.");
        }

        var rate = new ExchangeRate {
            BaseCurrency = baseCurrency,
            QuoteCurrency = quoteCurrency,
            Bid = bid,
            Ask = ask,
            Status = true,
            CreatedAt = Now,
            UpdatedAt = lastRefreshed
        };

        await SaveNewRateAsync(rate, "Alpha Vantage");

        return rate;
    }

    #endregion

    #region CreateRate

    /// <summary>
    /// Creates a new exchange rate for a currency pair, if it does not already exist. If the pair exists but is inactive, it reactivates it and updates the prices.
    /// </summary>
    public async Task<ActionResult<ExchangeRate>> CreateRateAsync(CreateExchangeRateRequest request) {
        var baseCurrency = NormalizeCurrencyCode(request.BaseCurrency);
        var quoteCurrency = NormalizeCurrencyCode(request.QuoteCurrency);

        if (baseCurrency is null || quoteCurrency is null) {
            return BadRequest("Currency codes must be three letters.");
        }

        if (baseCurrency == quoteCurrency) {
            return BadRequest("Base and quote currencies must be different.");
        }

        var priceError = ValidatePrices(request.Bid, request.Ask);
        if (!string.IsNullOrEmpty(priceError)) {
            return BadRequest(priceError);
        }

        var existing = await FindRate(baseCurrency, quoteCurrency);

        if (existing is not null) {
            if (existing.Status) {
                return Conflict($"An exchange rate for {baseCurrency}/{quoteCurrency} already exists.");
            }

            existing.Bid = request.Bid;
            existing.Ask = request.Ask;
            existing.Status = true;
            existing.UpdatedAt = Now;
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Reactivated rate {BaseCurrency}/{QuoteCurrency}.", baseCurrency, quoteCurrency);
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

        await SaveNewRateAsync(rate, "Internal");

        return new CreatedAtActionResult("GetRate", "ExchangeRates", new { baseCurrency = rate.BaseCurrency, quoteCurrency = rate.QuoteCurrency }, rate);
    }

    #endregion

    #region UpdateRate

    /// <summary>
    /// Updates the bid and ask prices for an existing exchange rate
    /// </summary>
    public async Task<ActionResult<ExchangeRate>> UpdateRateAsync(string baseCurrency, string quoteCurrency, UpdateExchangeRateRequest request) {
        var normalizedBase = NormalizeCurrencyCode(baseCurrency);
        var normalizedQuote = NormalizeCurrencyCode(quoteCurrency);

        if (normalizedBase is null || normalizedQuote is null) {
            return BadRequest("Currency codes must be three letters.");
        }

        var priceError = ValidatePrices(request.Bid, request.Ask);
        if (!string.IsNullOrEmpty(priceError)) {
            return BadRequest(priceError);
        }

        var rate = await FindRate(normalizedBase, normalizedQuote, activeOnly: true);
        if (rate is null) {
            return NotFound();
        }

        rate.Bid = request.Bid;
        rate.Ask = request.Ask;
        rate.UpdatedAt = Now;
        await _dbContext.SaveChangesAsync();
        _logger.LogInformation("Updated rate {BaseCurrency}/{QuoteCurrency} to bid {Bid} and ask {Ask}.", rate.BaseCurrency, rate.QuoteCurrency, rate.Bid, rate.Ask);
        return rate;
    }

    #endregion

    #region DeleteRate

    /// <summary>
    /// Soft deletes an exchange rate by marking it as inactive
    /// </summary>
    public async Task<IActionResult> DeleteRateAsync(string baseCurrency, string quoteCurrency) {
        var normalizedBase = NormalizeCurrencyCode(baseCurrency);
        var normalizedQuote = NormalizeCurrencyCode(quoteCurrency);

        if (normalizedBase is null || normalizedQuote is null) {
            return BadRequest("Currency codes must be three letters.");
        }

        var rate = await FindRate(normalizedBase, normalizedQuote);

        if (rate is null) {
            return NotFound();
        }

        if (!rate.Status) {
            _logger.LogInformation("Rate {BaseCurrency}/{QuoteCurrency} was already inactive.", rate.BaseCurrency, rate.QuoteCurrency);
            return Ok();
        }

        rate.Status = false;
        rate.UpdatedAt = Now;
        await _dbContext.SaveChangesAsync();
        _logger.LogInformation("Soft deleted rate {BaseCurrency}/{QuoteCurrency}.", rate.BaseCurrency, rate.QuoteCurrency);

        return Ok();
    }

    #endregion

    #region Helpers

    private Task<ExchangeRate?> FindRate(string baseCurrency, string quoteCurrency, bool activeOnly = false) =>
        _dbContext.ExchangeRates.FirstOrDefaultAsync(rate => rate.BaseCurrency == baseCurrency && rate.QuoteCurrency == quoteCurrency && (!activeOnly || rate.Status));

    // Adds the rate and raises the created event, so both insert paths behave the same.
    private async Task<ExchangeRate> SaveNewRateAsync(ExchangeRate rate, string source) {
        _dbContext.ExchangeRates.Add(rate);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Stored new rate {BaseCurrency}/{QuoteCurrency} from {Source} with bid {Bid} and ask {Ask}.", rate.BaseCurrency, rate.QuoteCurrency, source, rate.Bid, rate.Ask);
        await _eventPublisher.PublishAsync(ToEvent(rate));

        return rate;
    }

    private static ExchangeRateCreatedEvent ToEvent(ExchangeRate rate) =>
        new(rate.BaseCurrency, rate.QuoteCurrency, rate.Bid, rate.Ask, rate.CreatedAt);

    // We ensure the currencies exist in the Currencies table. If they don't, we add them
    private async Task EnsureCurrenciesExistAsync(string baseCurrency, string quoteCurrency) {
        await EnsureCurrencyExistAsync(baseCurrency);
        await EnsureCurrencyExistAsync(quoteCurrency);
    }

    /// <summary>
    /// Adds a currency to the Currencies table if it does not already exist
    /// </summary>
    /// <param name="currency"></param>
    /// <returns></returns>
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
    }

    // Trims and upper-cases a currency code, returning null when it is not three letters.
    private static string? NormalizeCurrencyCode(string? code) {
        var normalized = code?.Trim().ToUpperInvariant();

        return normalized?.Length == 3 ? normalized : null;
    }

    private static string? ValidatePrices(decimal bid, decimal ask) {
        if (bid <= 0 || ask <= 0) {
            return "Bid and ask must be greater than zero.";
        }

        return ask < bid ? "Ask must be greater than or equal to the bid." : null;
    }

    private static BadRequestObjectResult BadRequest(string message) => new(message);

    private static ConflictObjectResult Conflict(string message) => new(message);

    private static NotFoundResult NotFound() => new();

    private static OkResult Ok() => new();

    private static ObjectResult Problem(int statusCode, string detail) =>
        new(new ProblemDetails {
            Status = statusCode,
            Title = ReasonPhrases.GetReasonPhrase(statusCode),
            Detail = detail
        }) { StatusCode = statusCode };

    #endregion
}
