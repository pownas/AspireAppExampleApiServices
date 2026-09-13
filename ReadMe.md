# AspireApp - Example Project
This is an example project for the AspireApp application. It demonstrates the basic structure and functionality of the application, including user authentication, data management, and UI components.


## Getting Started
To get started with the AspireApp1 example project, follow these steps:
1. Clone the repository
2. Run the application `AspireApp1.AppHost` to start the server

## Arkitekturöversikt

För en samlad helhetsbeskrivning av hur alla tjänster, flöden, spårning och datalagring hänger ihop, se:

- [CLAUDE.md](CLAUDE.md)

## DIGG + W3C Trace Context (spårbarhet)
- Tjänsterna använder W3C Trace Context (`traceparent`, `tracestate`) via .NET `Activity`/OpenTelemetry.
- `trace_id`, `span_id`, `service.name`, `timestamp_utc` och `correlation_id` loggas strukturerat.
- Korrelationskontext skickas från `apiserviceforecast` till `workerservice1` som jobbmeddelande.
- Worker fortsätter samma trace med `traceparent` och propagaterar vidare vid utgående anrop.
- Spans är namngivna per steg (t.ex. `ApiService.CallApiServiceForecast`, `ApiServiceForecast.CallStaticWeather`, `Worker.ProcessJob`, `Worker.CallStaticWeather`) för tydliga Aspire-grafer.
- Felvägar (`/errorcall`, `/errorcall2`) loggar var i kedjan felet uppstår med `trace_id`, `span_id`, `parent_span_id` och `correlation_id`.

### Felsökning via `trace_id`
1. Starta `AspireApp1.AppHost`.
2. Kör anrop från `webfrontend` till backend (exempel: väderflödet).
3. Öppna trace-vyn i Aspire dashboard och följ samma `trace_id` genom tjänstekedjan.
4. På sidan **Processflöde** kan du söka med:
   - ren `trace_id` (32 hex-tecken)
   - full `traceparent` (`00-<trace_id>-<span_id>-<flags>`)
   - Aspire URL-format, t.ex. `https://.../traces/detail/<trace_id>`
5. Processflöde visar stegindikering i formatet **Steg X/N** samt markerar var flödet fastnat med tjänst och felorsak.
6. Kontrollera worker-loggar för samma `trace_id` och `correlation_id` vid async-jobb/retry/finalt fel.

## Frontend-visualisering av processflöde

För krav, specifikation och implementationsplan gällande frontend-visualisering av processflöde via `traceId`/`correlationId`, se:

- [Kravspecifikation: Frontend-visualisering av processflöde](docs/kravspecifikation-frontend-processflode.md)
- [Implementationsplan: Frontend-visualisering av processflöde](docs/implementationsplan-frontend-processflode.md)

## StateStore databas (SQLite eller SQL Server)

StateStore kan köras med både SQLite och SQL Server via konfiguration i `AspireApp1.AppHost/appsettings.Development.json` (globalt för hela lösningen).

```json
{
  "StateStore": {
    "Provider": "Sqlite"
  }
}
```

- **Default är `Provider = "Sqlite"`**
- `Provider = "SqlServer"` använder `ConnectionStrings:statestoreSqlServer`
- `Provider = "Sqlite"` använder delad fil i LocalApplicationData (sätts i AppHost om `ConnectionStrings:statestore` saknas)
- Databasen och tabellerna skapas automatiskt vid uppstart om de saknas (gäller både SQL Server och SQLite)
- AppHost skickar alltid båda nycklarna till tjänsterna:
  - `ConnectionStrings:statestore` = SQLite-anslutning
  - `ConnectionStrings:statestoreSqlServer` = SQL Server-anslutning

### Så växlar du provider
1. Öppna `AspireApp1.AppHost/appsettings.Development.json`.
2. Sätt `StateStore:Provider` till `Sqlite` eller `SqlServer`.
3. Om du väljer `SqlServer`, kontrollera att `ConnectionStrings:statestoreSqlServer` pekar på en tillgänglig SQL Server. SQLite kräver ingen extern databas.
4. Starta om `AspireApp1.AppHost`.

Startsidan i frontend visar nu aktiv provider under rubriken **Aktiv StateStore DB**.

Sidan `/flowruns` visar alla senaste flödeskörningar och länkar vidare till `/processflow`.

## Retry- och Intermittent-demo med konfigurerbar simulering

Formulären på `/retrydemo` och `/intermittentdemo` hämtar default-profiler från WorkerService1 (`GET /flow/simulation/profiles`) och skickar valda värden vid start av flöde.

Default-profilerna sätts i `AspireApp1.WorkerService1/appsettings.json` under `FlowSimulationProfiles`:

```json
{
  "FlowSimulationProfiles": {
    "RetryDemo": {
      "RetryAttempts": 3,
      "RetryDelayMs": 10000
    },
    "IntermittentDemo": {
      "RetryAttempts": 3,
      "NormalMinDelayMs": 10,
      "NormalMaxDelayMs": 500,
      "SlowMinDelayMs": 5000,
      "SlowMaxDelayMs": 30000,
      "SlowCallProbabilityPercent": 20,
      "Http500ProbabilityPercent": 15,
      "RetryDelayMs": 1000
    }
  }
}
```

- **RetryDemo** kör legacy-beteende som default: 3 försök med 10 sekunder mellan försök.
- **IntermittentDemo** kör sannolikhetsstyrd intermittent simulering (slow + HTTP 500) med 1–3 försök.
- `RetryAttempts` klampas till 1–3 innan körning.
