# Foreign Exchange Rates API

Developed by Rodrigo Lopes

Interview exercise: an ASP.NET Core Web API that performs CRUD operations of foreign exchange
rates in PostgreSQL and fetches missing pairs from [Alpha Vantage](https://www.alphavantage.co).

## Requirements

- .NET 8 SDK
- Docker
- An Alpha Vantage API key

## Configuration

Set the following environment variables
```
[Environment]::SetEnvironmentVariable('POSTGRES_PASSWORD', '<password>')
[Environment]::SetEnvironmentVariable('RABBITMQ_PASSWORD', '<password>')
[Environment]::SetEnvironmentVariable('ALPHA_VANTAGE_API_KEY', '<apiKey>')
```

`docker compose` uses the passwords for the database and the broker; the API builds its addresses from them: `localhost:5432` and `localhost:5672`, with the fixed database `vfx_exrates` and the user `vfx`. The API reads these variables when it starts, so restart it after changing them.

## Run

```
# from the repository root
docker compose up -d
cd ForeignExchangeRateAPI
dotnet ef database update
dotnet run
```

Swagger: `http://localhost:5156/swagger`

RabbitMQ: `http://localhost:15672` (user `vfx`)

## Database

The data can be inspected with any PostgreSQL client (e.g. DBeaver): host `localhost`, port `5432`, database `vfx_exrates`, user `vfx`, password from `POSTGRES_PASSWORD`.

## Endpoints

| Method | Route | Description |
| --- | --- | --- |
| GET | `/api/rates` | Active rates |
| GET | `/api/rates/{base}/{quote}` | Stored rate, or fetched from Alpha Vantage and stored |
| POST | `/api/rates` | Create a rate |
| PUT | `/api/rates/{base}/{quote}` | Update bid/ask |
| DELETE | `/api/rates/{base}/{quote}` | Soft delete |

```json
{ 
    "baseCurrency": "USD", 
    "quoteCurrency": "EUR", 
    "bid": 0.91, 
    "ask": 0.92 
}
```

A pair that is not stored is requested from Alpha Vantage and saved. Currencies missing from the `Currencies` table are inserted automatically. 
A Postman collection is included (`ForeignExchangeRateAPI.postman_collection.json`)

## Message queue

Creating a rate raises an `ExchangeRateCreatedEvent`, published to the durable `exchange-rates` queue on the local broker. A publish failure is logged, never returned to the client, because the rate is already stored. 
The queue is declared on the first publish, so it does not exist until a rate has been created.

## Tests

```
cd ForeignExchangeRateAPI
dotnet test
```

Each test runs against its own in-memory database.

| Test | Verifies |
| --- | --- |
| `GetRates_ReturnsStoredRatesOrderedByPair` | Returns the stored rates ordered by base currency and then by quote currency |
| `GetRates_WhenNoRatesAreStored_ReturnsEmptyCollection` | Returns an empty collection on an empty database |
| `GetRate_WhenSoftDeleted_ReturnsNotFound` | Returns 404 for a soft deleted pair |
| `GetRate_WithInvalidCurrencyCode_ReturnsBadRequest` | Returns 400 for a pair whose code is not three letters |
| `CreateRate_PersistsRateAndPublishesEvent` | Creates a rate, reads it back and raises the created event with its values |
| `CreateRate_InsertsMissingCurrenciesAndReusesExistingOnes` | Inserts the currencies that do not exist yet once, and stores a currency reused by two rates only once |
| `CreateRate_WhenPairIsActive_ReturnsConflictWithoutPublishingEvent` | Rejects a pair that is already active and raises no event |
| `CreateRate_WhenPairIsSoftDeleted_ReactivatesIt` | Reactivates a soft deleted pair instead of inserting a second row |
| `CreateRate_WithInvalidRequest_ReturnsBadRequest` | Returns 400 when the pair or the prices are invalid |
| `CreateRate_NormalizesCurrencyCodes` | Trims and upper-cases the codes, so a lower case request is stored and read back in the canonical form |
| `UpdateRate_ChangesPrices` | Updates the bid and ask of an existing rate |
| `UpdateRate_WhenPairIsMissing_ReturnsNotFound` | Returns 404 when the pair was never stored |
| `UpdateRate_WhenPairIsSoftDeleted_ReturnsNotFound` | Returns 404 when the pair was soft deleted and is updated again |
| `UpdateRate_WithInvalidCurrencyCode_ReturnsBadRequest` | Returns 400 for a pair whose code is not three letters |
| `DeleteRate_WhenPairIsMissing_ReturnsNotFound` | Returns 404 when the pair was never stored |
| `DeleteRate_WhenAlreadyDeleted_ReturnsOk` | Deleting an inactive pair again still reports success |
| `DeleteRate_WithInvalidCurrencyCode_ReturnsBadRequest` | Returns 400 for a pair whose code is not three letters |

## Limitations and improvements

- The Alpha Vantage integration is currently implemented in the controller (`FetchAlphaVantageRate`). It could be moved to a separate file/service (for example `Services/AlphaVantageExchangeRateService.cs`) so that the controller only orchestrates the request.

- There is no endpoint for currencies. The `Currencies` table is only filled implicitly when a rate is created. A `CurrenciesController` would make the currency data manageable.

- Hosted elsewhere, the environment variables would become GitHub repository secrets and a GitHub Actions pipeline could build, test and deploy the API.
