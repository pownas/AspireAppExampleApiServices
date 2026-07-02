# Implementationsplan: Frontend-visualisering av processflöde

**Baseras på:** [Kravspecifikation – Frontend-visualisering av processflöde](kravspecifikation-frontend-processflode.md)
**Version:** 1.0
**Datum:** 2026-07-02

---

## 1. Sammanfattning

Den här implementationsplanen beskriver hur Blazor-sidan för processflödesvisualisering ska byggas in i det befintliga projektet `AspireApp1.Web`. Sidan nås via rutten `/processflow` och tillåter sökning via `traceId`, `correlationId` och `spanId`. Telemetridata hämtas från Aspire-dashboardens inbyggda OTLP-API via ett internt proxy-lager.

---

## 2. Arkitektur och ansvarsfördelning

### 2.1 Övergripande arkitektur

```
[Användare i webbläsare]
        │
        ▼
[Blazor Server – AspireApp1.Web]
    ProcessFlow.razor  (/processflow)
        │  (HttpClient via tjänstreference)
        ▼
[Proxy API-endpoint i AspireApp1.Web eller AspireApp1.ApiService]
    GET /api/traces/{traceId}
    GET /api/traces/correlation/{correlationId}
    GET /api/traces/span/{spanId}
        │  (OTLP/HTTP eller Aspire-dashboardens interna API)
        ▼
[Aspire-dashboard – telemetrilagring]
    OTLP-kompatibel endpoint för traces och loggar
```

### 2.2 Ansvarsfördelning

| Komponent | Ansvar |
|-----------|--------|
| `ProcessFlow.razor` | Sökinput, flödesvisualisering, detaljvy, filterhantering |
| `ProcessFlowApiClient.cs` | HTTP-klient mot proxy-API:et för telemetridata |
| `TraceProxyController.cs` (eller minimal API) | Vidarebefordrar förfrågningar till Aspire-dashboardens OTLP-API, aggregerar svar |
| Aspire-dashboard | Lagrar och exponerar OTLP-telemetri (spans, loggar) |
| Datamodeller (`TraceModel`, `SpanModel` m.fl.) | Representerar telemetridata som C#-klasser |

---

## 3. Delmoment och implementeringssteg

### Steg 1 – Datamodellering

Skapa C#-datamodeller som representerar telemetridata:

**Filer att skapa:** `AspireApp1.Web/Models/TraceModel.cs`

```
TraceModel
├── TraceId (string)
├── CorrelationId (string?)
├── OverallStatus (SpanStatus)
├── Spans (List<SpanModel>)
└── StartTime (DateTimeOffset)

SpanModel
├── SpanId (string)
├── ParentSpanId (string?)
├── ServiceName (string)
├── OperationName (string)
├── StartTime (DateTimeOffset)
├── Duration (TimeSpan?)
├── Status (SpanStatus)  // OK | Warning | Error | InProgress | Unknown
├── ErrorMessage (string?)
├── HttpStatusCode (int?)
└── LogEntries (List<LogEntryModel>)

LogEntryModel
├── Timestamp (DateTimeOffset)
├── Level (string)
├── Message (string)
└── Attributes (Dictionary<string, string>)

enum SpanStatus { OK, Warning, Error, InProgress, Unknown }
```

---

### Steg 2 – Proxy API-endpoint

Skapa ett internt API-lager som hämtar data från Aspire-dashboardens OTLP API:

**Alternativ A (rekommenderat):** Minimal API i `AspireApp1.Web/Program.cs`

```csharp
app.MapGet("/api/traces/{traceId}", async (string traceId, AspireDashboardClient client) =>
    await client.GetTraceAsync(traceId));

app.MapGet("/api/traces/correlation/{correlationId}", async (string correlationId, AspireDashboardClient client) =>
    await client.GetByCorrelationIdAsync(correlationId));

app.MapGet("/api/traces/span/{spanId}", async (string spanId, AspireDashboardClient client) =>
    await client.GetTraceBySpanIdAsync(spanId));
```

**Alternativ B:** Separat `TraceController` i `AspireApp1.ApiService` om dataaggregering är komplex.

**Fil att skapa:** `AspireApp1.Web/AspireDashboardClient.cs`

- Klient för att anropa Aspire-dashboardens OTLP-API (HTTP/gRPC).
- Konfigureras med tjänsteadressen via Aspire-tjänstreference eller konfigurationsfil.
- Transformerar OTLP-svar till `TraceModel`/`SpanModel`.

> **Not om Aspire-dashboard API:** Aspire-dashboards interna API är inte offentligt dokumenterat och kan ändras. Undersök om `Aspire.Dashboard.SDK` eller `OpenTelemetry.Exporter.OpenTelemetryProtocol` kan användas för att fråga befintlig OTLP-data. Alternativt kan man rikta OpenTelemetry-exportering mot en lokal Postgres/SQLite-databas som frontend kan fråga direkt.

---

### Steg 3 – Blazor-sida och UI-komponenter

**Fil att skapa:** `AspireApp1.Web/Components/Pages/ProcessFlow.razor`

**Ruttdefinition:**
```razor
@page "/processflow"
```

**UI-sektioner:**

#### 3.1 Sökformulär
- Tre separata sökfält (eller ett fält med typval):
  - **TraceId** – 32 hexadecimala tecken (partiell matchning tillåten, minst 16 tecken)
  - **CorrelationId** – fri sträng (GUID eller annat)
  - **SpanId** – 16 hexadecimala tecken (FR-21)
- Sökknapp och validering av inmatningsformat
- Felmeddelande: *"Inga flödessteg hittades för angivet ID."* vid tom träff (FR-5)

#### 3.2 Flödesstatus-badge
- Övergripande statusindikator: **"Flöde slutfört – OK"** (grön) eller **"Flöde misslyckades – Fel i steg X"** (röd) (FR-17)

#### 3.3 Stegvy (hierarkisk träd-vy)
- Vertikal eller horisontell timeline/flödesdiagram
- Varje span visas med:
  - `service.name`, span-namn, statussymbol (✅/⚠️/❌/🔄/❓)
  - Tidsstämpel och varaktighet i ms
  - Hierarkisk indragning baserad på `parent_span_id` (FR-14)
- Felsteg med röd bakgrund och synligt felmeddelande (FR-15)

#### 3.4 Tidslinjevy (Gantt)
- Kronologisk Gantt-liknande vy av spans längs en tidaxel (FR-19)
- Visa överlappande och asynkrona flöden

#### 3.5 Filtreringspanel
- Filter per tjänst (`service.name`), status och tidsintervall (FR-18)
- Växlingsknapp: **Stegvy** / **Tidslinjevy** (FR-20)

#### 3.6 Detaljvy per steg
- Expanderbar panel vid klick på ett steg (FR-16)
- Visar alla loggposter, fullständiga IDs, HTTP-statuskod, felmeddelande och stack trace

**Fil att uppdatera:** `AspireApp1.Web/Components/Layout/NavMenu.razor`
- Lägg till länk:
```razor
<div class="nav-item px-3">
    <NavLink class="nav-link" href="processflow">
        <span class="bi bi-diagram-3-fill" aria-hidden="true"></span> Processflöde
    </NavLink>
</div>
```

---

### Steg 4 – HTTP-klient för proxy API

**Fil att skapa:** `AspireApp1.Web/ProcessFlowApiClient.cs`

```csharp
public class ProcessFlowApiClient(HttpClient httpClient)
{
    public Task<TraceModel?> GetByTraceIdAsync(string traceId) =>
        httpClient.GetFromJsonAsync<TraceModel>($"/api/traces/{traceId}");

    public Task<TraceModel?> GetByCorrelationIdAsync(string correlationId) =>
        httpClient.GetFromJsonAsync<TraceModel>($"/api/traces/correlation/{correlationId}");

    public Task<TraceModel?> GetBySpanIdAsync(string spanId) =>
        httpClient.GetFromJsonAsync<TraceModel>($"/api/traces/span/{spanId}");
}
```

Registreras i `Program.cs` via `builder.Services.AddHttpClient<ProcessFlowApiClient>`.

---

### Steg 5 – Sökning och filtrering via spanId

**Flöde för spanId-sökning (FR-21):**

1. Användaren anger ett `spanId` (16 hex-tecken) i sökfältet.
2. Fronten validerar formatet (regex: `^[0-9a-f]{16}$`).
3. En GET-förfrågan skickas till `/api/traces/span/{spanId}`.
4. Proxy-endpointen frågar Aspire-dashboard efter det trace som innehåller spanet.
5. Det matchande spanet markeras visuellt (t.ex. med blå ram eller "fokusläge").
6. Hela trace-trädet visas med det sökta spanet expanderat/synligt i vyn.
7. Om inget matchande span hittas visas: *"Inga flödessteg hittades för angivet SpanId."*

---

### Steg 6 – Integration mot Aspire-dashboard

Aspire-dashboard exponerar telemetridata via OTLP (OpenTelemetry Protocol) över gRPC eller HTTP. Integrationen kan ske på ett av följande sätt:

| Alternativ | Beskrivning | Komplexitet |
|------------|-------------|-------------|
| **A. Aspire-dashboard strukturdatabas (SQLite)** | Direktläsning av Aspire-dashboardens interna SQLite-databas (om data exponeras lokalt). | Hög – ej publikt API |
| **B. OTLP-mottagare i applikationen** | `AspireApp1.Web` konfigureras som OTLP-mottagare och lagrar spans i minne eller databas. | Medel |
| **C. Forwarding via Aspire resource service** | Anropa Aspire Resource Service API (`localhost:PORT`) som exponerar resursdata och loggar. | Medel |
| **D. Extern OTLP-backend (t.ex. Seq, Jaeger)** | Konfigurera OpenTelemetry-exportör mot Seq/Jaeger och fråga deras API. | Låg-Medel |

**Rekommendation för första iteration:** Alternativ C eller D, beroende på tillgänglig infrastruktur i miljön. Aspires `IResourceLogSource` eller `IDashboardClient` (om tillgänglig i SDK) bör utredas.

**Konfiguration i `appsettings.json`:**
```json
{
  "AspireDashboard": {
    "Endpoint": "http://localhost:18888",
    "ApiKey": ""
  }
}
```

---

## 4. Tekniska risker och beroenden

| Risk | Sannolikhet | Påverkan | Åtgärd |
|------|-------------|----------|--------|
| Aspire-dashboardens interna API saknar publikt kontrakt och kan ändras | Hög | Hög | Kapsla in all dashboard-integration i `AspireDashboardClient.cs`. Använd ett abstraktionslager. |
| Asynkrona jobb (Worker) kan ha brutna trace-kontexter | Medel | Medel | Verifiera `correlation_id`-propagering i Worker-loggning. Se befintliga tester i `AspireApp1.Tests`. |
| Svarstid > 3 sekunder vid stora trace-mängder (NFR-1) | Medel | Medel | Implementera paginering och begränsa standardvyn till senaste 100 spans. |
| GDPR-maskering av känslig data i loggar (NFR-3) | Låg-Medel | Hög | Implementera filterlogik i proxy-endpointen som redigerar känsliga attribut. |
| Autentisering och auktorisering (NFR-4) | Medel | Hög | Lägg till `[Authorize(Roles = "Developer,Administrator")]` på Blazor-sidan när auth-infrastruktur finns. |

### Kvarstående öppna beslut

- **OTLP-integrationsmetod:** Slutligt val av hur Aspire-dashboard exponerar data (se avsnitt 3, Steg 2, alternativ A–D).
- **Datalagring:** Behöver spans lagras lokalt (cache) eller kan de alltid hämtas on-demand från Aspire-dashboard?
- **Autentiseringsstrategi:** NFR-4 kräver att en auth-mekanism finns på plats; utanför scope för denna plan om auth inte är implementerat i lösningen.
- **Mobilstöd:** NFR-2 kräver ≥ 1 024 px, men responsivitet under detta kan utredas i ett senare steg.

---

## 5. Verifieringssteg

### 5.1 Manuella tester

| Test | Förväntat resultat |
|------|--------------------|
| Sök med känt `traceId` | Flödesdiagram visas med korrekta steg och statusar |
| Sök med känt `correlationId` | Alla spans kopplade till correlation visas (inkl. asynkrona Worker-steg) |
| Sök med känt `spanId` | Rätt span markeras, hela trace-trädet visas |
| Sök med okänt ID | Meddelandet *"Inga flödessteg hittades..."* visas |
| Klicka på ett felsteg | Detaljvy öppnas med fullständig logginformation |
| Växla mellan Stegvy och Tidslinjevy | Vyn byter utan fel |
| Filtrera per tjänst | Endast valda tjänstens spans visas |
| Ange partiellt `traceId` (16 av 32 tecken) | Sökning fungerar och returnerar matchande traces |

### 5.2 Koppling till befintliga tester

- `AspireApp1.Tests/WorkerTraceContextTests.cs` – verifiera att Worker-spans bär korrekt `correlation_id` och `traceparent`, vilket är en förutsättning för att flödesvisualisering av asynkrona jobb ska fungera.
- Bygg projektet med: `dotnet build AspireApp1.slnx`
- Kör Worker-tester med: `dotnet test AspireApp1.Tests/AspireApp1.Tests.csproj --filter WorkerTraceContextTests`

### 5.3 Framtida automatiserade tester (förslag)

- Enhetstest för `ProcessFlowApiClient` med mockad HTTP-klient
- Enhetstest för spannträdskonstruktion (parent–child-relationer i `TraceModel`)
- Blazor-komponenttest med `bUnit` för sökformuläret och flödesvisningen

---

## 6. Implementeringsordning (prioriterad)

| Prioritet | Delmoment | Beroende av |
|-----------|-----------|-------------|
| 1 | Datamodeller (`TraceModel`, `SpanModel`, `LogEntryModel`) | – |
| 2 | Proxy API-endpoint (minimal API i `Program.cs`) | Datamodeller |
| 3 | `AspireDashboardClient.cs` (OTLP-integration) | Proxy-endpoint |
| 4 | `ProcessFlowApiClient.cs` (HTTP-klient i Web) | Proxy-endpoint |
| 5 | `ProcessFlow.razor` – sökformulär och grundläggande stegvy | Klient |
| 6 | Detaljvy per steg (expanderbar panel) | Stegvy |
| 7 | Filtreringspanel och tidslinjevy (Gantt) | Stegvy |
| 8 | NavMenu-länk | ProcessFlow.razor |
| 9 | Manuella verifieringstester | Alla ovanstående |

---

## 7. Relationer till befintlig dokumentation

- [Kravspecifikation – Frontend-visualisering av processflöde](kravspecifikation-frontend-processflode.md)
- [ReadMe.md](../ReadMe.md) – beskriver DIGG + W3C Trace Context-implementationen
- [W3C Trace Context Specification](https://www.w3.org/TR/trace-context/)
- [OpenTelemetry .NET](https://opentelemetry.io/docs/instrumentation/net/)
- [Aspire Dashboard](https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/dashboard/overview)
