# AspireApp - Example Project
This is an example project for the AspireApp application. It demonstrates the basic structure and functionality of the application, including user authentication, data management, and UI components.


## Getting Started
To get started with the AspireApp1 example project, follow these steps:
1. Clone the repository
2. Run the application `AspireApp1.AppHost` to start the server

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
4. Kontrollera worker-loggar för samma `trace_id` och `correlation_id` vid async-jobb/retry/finalt fel.

## Frontend-visualisering av processflöde

För krav, specifikation och implementationsplan gällande frontend-visualisering av processflöde via `traceId`/`correlationId`, se:

- [Kravspecifikation: Frontend-visualisering av processflöde](docs/kravspecifikation-frontend-processflode.md)
- [Implementationsplan: Frontend-visualisering av processflöde](docs/implementationsplan-frontend-processflode.md)
