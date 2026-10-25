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
{ "baseCurrency": "USD", "quoteCurrency": "EUR", "bid": 0.91, "ask": 0.92 }
```

A pair that is not stored is requested from Alpha Vantage and saved. Currencies missing from the `Currencies` table are inserted automatically. 
A Postman collection is included (`ForeignExchangeRateAPI.postman_collection.json`)

## Message queue

Creating a rate raises an `ExchangeRateCreatedEvent`, published to the durable `exchange-rates` queue on the local broker. A publish failure is logged, never returned to the client, because the rate is already stored. 
The queue is declared on the first publish, so it does not exist until a rate has been created.

## Tests

```
dotnet test
```

| Test | Verifies |
| --- | --- |
| `GetRates_ReturnsStoredRates` | Returns the rates currently stored in the database. |
| `CreateRate_PersistsRate` | Creates a rate and returns the same record on a later read. |
| `UpdateRate_ChangesPrices` | Updates the bid and ask of an existing rate. |
| `DeleteRate_RemovesRateFromList` | Soft deletes a rate so it no longer appears in the list. |
| `CreateRate_WithNewCurrencies_InsertsTheCurrencies` | Currencies that do not exist yet are inserted with the rate. |
| `GetRate_WhenNotStored_FetchesFromProviderAndStoresIt` | Fetches a missing pair from Alpha Vantage, stores it and inserts its currencies. |
