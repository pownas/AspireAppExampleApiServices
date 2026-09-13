# CLAUDE.md — Arkitekturöversikt för AspireApp1

Detta dokument beskriver hur hela lösningen hänger ihop: tjänster, dataflöden, spårning och felsökning.

## 1) Helhetsbild

Lösningen är en Aspire-baserad mikrotjänstdemo med:

- **AppHost** som orkestrerar alla tjänster
- **Web Frontend** (Blazor Server) för demo, visualisering och felsökning
- Flera **API-tjänster** och **Worker-tjänster** som bygger en längre anropskedja
- Ett gemensamt **StateStore** (SQL Server eller SQLite) för flow-run, steg, spans och hälsodata

Målet är att kunna:

- starta flöden
- följa flöden steg för steg
- se var ett flöde fastnat
- återstarta ett fastnat flöde
- felsöka med TraceId/CorrelationId/SpanId

## 2) Viktiga projekt och ansvar

- `AspireApp1.AppHost`
  - Startar och kopplar ihop alla tjänster.
  - Sätter global konfiguration för StateStore-provider/connection strings.

- `AspireApp1.Web`
  - UI-sidor:
    - Home (`/`)
    - Processflöde (`/processflow`)
    - Alla körningar (`/flowruns`)
    - Flödesutlösare (`/flowdemo`)
    - Återförsöksflöde (`/retrydemo`)
  - API-proxy för trace-uppslag och flödeskontroll (start/restart/status).

- `AspireApp1.StateStore`
  - EF Core-modeller + DbContext.
  - Provider-val (`SqlServer`/`Sqlite`) via `StateStore:Provider`.
  - Automatisk DB/schema-init vid startup.

- `AspireApp1.WorkerService1/2/3/4`
  - WS1/WS2/WS3 kör flödessteg.
  - WS4 övervakar tjänstehälsa via `/health`.

- `AspireApp1.ApiService*` (+ relaterade API-projekt)
  - Demo-API:er för synk/asynk-anrop, kedjor och felscenarier.

## 3) Dataflöde (från start till spårning)

1. Användaren startar flöde via Frontend (`/flowdemo` eller `/retrydemo`).
2. Web anropar WS1 (`/flow/start` eller `/flow/retry-demo/start`).
3. WS1 initierar `FlowRunRecord` + `FlowStepRecord` och driver steg vidare i kedjan.
4. Tjänster skriver status/spans/logik till StateStore.
5. Processflöde-sidan läser data via `TraceQueryService` och visualiserar:
   - steg X/N
   - aktiv tjänst
   - felorsak/stoppsteg
   - tidslinje/stegvy

## 4) Trace/Correlation/Span-sökning

`/processflow` accepterar:

- ren `traceId` (32 hex)
- `traceparent`
- Aspire `.../traces/detail/{traceId}` URL
- `correlationId`
- `spanId`

TraceQuery fallbackar så att träffar hittas robust även när data ligger på olika nivåer (FlowRun/FlowStep/Span).

## 5) Om flöden fastnar

Från Processflöde finns åtgärden **“Återstarta flöde”**:

- endpoint: `POST /api/flow/{flowRunId}/restart`
- startar en **ny körning** av samma flödestyp via WS1
- används när tidigare körning fastnat (t.ex. beroende tjänst var nere en stund)

## 6) Databas och drift

StateStore körs globalt via AppHost-konfiguration:

- default: `Sqlite`
- alternativ: `SqlServer`

Databasen/schema skapas automatiskt vid startup om den saknas.

## 7) UI-ytor för drift/felsökning

- **Home**: arkitektur + tjänstehälsa + live-statistik
- **FlowRuns**: tydlig lista över körningar, stegstatus och spårningslänkar
- **ProcessFlow**: detaljerad stegvy/tidslinje, felmarkeringar, expand/collapse, restart

## 8) Snabbstart för utvecklare

1. Starta `AspireApp1.AppHost`.
2. Öppna Web Frontend.
3. Starta ett demo-flöde i `/flowdemo`.
4. Öppna `/processflow` med trace/correlation.
5. Verifiera stegstatus, tjänstekedja och ev. restart-flöde.

## 9) Nyckelfiler att känna till

- `AspireApp1.AppHost/AppHost.cs`
- `AspireApp1.AppHost/appsettings.Development.json`
- `AspireApp1.StateStore/StateStoreDbRegistration.cs`
- `AspireApp1.StateStore/DatabaseInitializer.cs`
- `AspireApp1.Web/Program.cs`
- `AspireApp1.Web/TraceQueryService.cs`
- `AspireApp1.Web/Components/Pages/Home.razor`
- `AspireApp1.Web/Components/Pages/FlowRuns.razor`
- `AspireApp1.Web/Components/Pages/ProcessFlow.razor`

## 10) Kommentarer i koden
Kommentera gärna publika och privata metoder med syfte, parametrar och returvärden. Använd XML-kommentarer (summary och remarks) för att kunna generera dokumentation.
