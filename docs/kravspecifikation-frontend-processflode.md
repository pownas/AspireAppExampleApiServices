# Kravspecifikation: Frontend-visualisering av processflöde via traceId/correlationId

## 1. Bakgrund och syfte

I dagens system loggas varje tjänsteanrop med strukturerade fält som `trace_id`, `span_id`, `parent_span_id`, `correlation_id` och `service.name`. Spårbarhet hanteras via W3C Trace Context (`traceparent`/`tracestate`) och OpenTelemetry. Det finns ett behov av att kunna visualisera hela processflödet i frontend, från det initiala anropet till det slutliga svaret, så att man tydligt kan se var ett flöde lyckades eller misslyckades.

Det centrala användningsfallet är att söka på ett `traceId` eller `correlationId` och få:
- Relevanta loggar kopplade till det angivna ID:t
- En grafisk representation av flödet i gränssnittet
- Tydlig markering av vilka steg som gick rätt, pågår eller misslyckades

---

## 2. Termer och definitioner

| Term | Beskrivning |
|------|-------------|
| `trace_id` | Unikt ID som spänner över hela det distribuerade anropet (W3C Trace Context). Formatet är 32 hexadecimala tecken (128-bitars). |
| `span_id` | ID för ett enskilt steg (span) inom ett trace. Formatet är 16 hexadecimala tecken (64-bitars). |
| `parent_span_id` | `span_id` för det anropande steget, används för att bygga upp trädet av anrop. |
| `correlation_id` | Applikationsspecifikt ID som skickas i `X-Correlation-Id`-headern och i jobbmeddelanden för att knyta ihop asynkrona flöden med det ursprungliga anropet. |
| `traceparent` | W3C-headern `00-<trace_id>-<span_id>-<flags>` som propageras i HTTP-headers. |
| Steg/span | En diskret operation i flödet, t.ex. `ApiService.CallApiServiceForecast` eller `Worker.ProcessJob`. |
| Processflöde | Den totala kedjan av steg som ett anrop genomgår från webfrontend till backend-tjänster och worker. |

---

## 3. Systemkontext

Systemet består av följande tjänster som tillsammans utgör ett processflöde:

```
webfrontend
    └── apiservice (ApiService.CallApiServiceForecast)
            └── apiserviceforecast (ApiServiceForecast.CallStaticWeather)
                    └── apiservicestaticweather
            └── workerservice1 (Worker.ProcessJob)
                    └── apiservicestaticweather (Worker.CallStaticWeather)
            └── apierrorservice (vid felflöden)
```

Felvägar:
- `/errorcall` → apiservice → apiserviceforecast → apierrorservice
- `/errorcall2` → apiservice → apierrorservice (direkt)

Varje steg loggar strukturerade fält:
```
trace_id, span_id, parent_span_id, service.name, timestamp_utc, correlation_id
```

---

## 4. Funktionella krav

### 4.1 Sökning

**FR-1:** Användaren ska kunna söka på ett `traceId` (32 hexadecimala tecken) i ett sökfält i frontend.

**FR-2:** Användaren ska kunna söka på ett `correlationId` (t.ex. ett GUID eller valfri sträng) i samma eller separata sökfält.

**FR-3:** Sökfältet ska acceptera `X-Correlation-Id`-värden direkt (samma värde som returneras i svarsheadern).

**FR-4:** Systemet ska returnera alla loggar och spänner (spans) som är kopplade till det sökta ID:t, oavsett om sökning sker via `traceId` eller `correlationId`.

**FR-5:** Vid tom träff ska ett tydligt meddelande visas: *"Inga flödessteg hittades för angivet ID."*

**FR-6:** Sökning ska stödja partiell matchning av `traceId` (minst de första 16 tecknen) för att underlätta manuell inmatning.

### 4.2 Koppling av loggar och flödessteg

**FR-7:** Alla loggposter med samma `trace_id` ska aggregeras till ett sammanhängande processflöde.

**FR-8:** Loggposter med samma `correlation_id` men potentiellt olika `trace_id` (t.ex. vid asynkrona jobb som startas från ett ursprungligt anrop) ska kunna visas som del av samma logiska flöde.

**FR-9:** Relationen mellan spans ska härledas från `parent_span_id`-fältet, vilket gör det möjligt att bygga ett träd av anrop.

**FR-10:** Varje span ska innehålla: `service.name`, `span_id`, `parent_span_id`, starttid (`timestamp_utc`), varaktighet (om tillgängligt), status (OK/Fel/Pågående) och eventuellt felmeddelande.

**FR-11:** Asynkrona jobb (t.ex. Worker) som är kopplade till ett anrop via `correlation_id` i jobbmeddelandet ska inkluderas i flödesvisningen.

### 4.3 Grafisk vy

**FR-12:** Processflödet ska visualiseras som ett vertikalt eller horisontellt stegdiagram (flödesdiagram/timeline) där varje steg representerar en span.

**FR-13:** Varje steg ska visas med:
- Tjänstens namn (`service.name`)
- Stegnamn (span-namn, t.ex. `ApiService.CallApiServiceForecast`)
- Status: ✅ **OK** (grön), ⚠️ **Varning** (gul), ❌ **Fel** (röd), 🔄 **Pågående** (blå)
- Tidsstämpel (`timestamp_utc`)
- Varaktighet i millisekunder (om tillgängligt)

**FR-14:** Hierarkin av anrop (parent → child spans) ska framgå tydligt med indragningar eller kopplingspiler i diagrammet.

**FR-15:** Felsteget ska markeras tydligt med röd bakgrund eller ikon, och felmeddelandet ska vara synligt direkt i flödesdiagrammet utan att behöva öppna detaljvy.

**FR-16:** Det ska finnas en detaljvy per steg (t.ex. expanderbar panel eller sidopanel) som visar:
- Alla loggposter för det steget
- Fullständiga `trace_id`, `span_id`, `parent_span_id`
- HTTP-statuskod (om tillämpligt)
- Komplett felmeddelande och stack trace (om tillgängligt)

**FR-17:** Systemet ska visa ett övergripande flödesstatus-badge: **"Flöde slutfört – OK"** (grönt) eller **"Flöde misslyckades – Fel i steg X"** (rött).

### 4.4 Filtrering och tidslinje

**FR-18:** Användaren ska kunna filtrera flödesvisningen per:
- Tjänst (`service.name`)
- Status (OK, Varning, Fel)
- Tidsintervall (från/till `timestamp_utc`)

**FR-19:** En tidslinje-vy ska finnas som visar alla spans längs en tidaxel, med möjlighet att se överlappande anrop och asynkrona flöden (likt Gantt-diagram).

**FR-20:** Det ska vara möjligt att växla mellan **Stegvy** (hierarkisk träd-vy) och **Tidslinjevy** (kronologisk Gantt-vy).

---

## 5. Definition av status per steg

| Status | Kriterier | Visning |
|--------|-----------|---------|
| **OK** | HTTP-svarskod 2xx, inga loggade fel (Error/Critical), span avslutad utan `ActivityStatusCode.Error` | Grön ✅ |
| **Varning** | HTTP-svarskod 4xx (utom 404 vid förväntad frånvaro), loggad Warning-nivå, timeout men återhämtat | Gul ⚠️ |
| **Fel** | HTTP-svarskod 5xx, loggad Error/Critical-nivå, undantag kastas, `ActivityStatusCode.Error` satt på span | Röd ❌ |
| **Pågående** | Span startad men inte avslutad (saknar sluttidsstämpel) | Blå 🔄 |
| **Okänd** | Otillräcklig data för att fastställa status | Grå ❓ |

---

## 6. Länk mellan backend-loggning, tracing och frontend

### 6.1 Datakällor

Frontend hämtar data från en dedikerad API-endpoint (t.ex. `/api/traces/{traceId}` och `/api/traces/correlation/{correlationId}`) som aggregerar information från:
1. **Strukturerade loggar** – loggposter med `trace_id`, `span_id`, `correlation_id` etc.
2. **OpenTelemetry-spans** – från Aspire-dashboardens telemetrilagring eller en OTLP-backend (t.ex. Jaeger, Zipkin, Azure Monitor).
3. **Jobbmeddelanden** – Worker-jobb som innehåller `correlationId` och `traceParent` i sin payload.

### 6.2 Propagering av kontext

```
HTTP-anrop in → traceparent-header → .NET Activity/OpenTelemetry → span skapas
                X-Correlation-Id-header → correlation_id i HttpContext.Items
                                        → loggas i varje tjänst
                                        → skickas i Worker-jobbmeddelande
                                        → returneras i svarsheader X-Correlation-Id
```

### 6.3 Identifierare-hierarki

```
traceId (1 per distribuerat anrop)
    └── spanId (1 per steg i kedjan)
            └── parent_spanId (koppling till föräldrasteg)

correlationId (1 per logiskt affärsflöde, kan spänna över multipla traces vid retries)
```

---

## 7. Tänkt användarflöde (Use Case)

### UC-1: Felsökning av misslyckat flöde

**Aktör:** Systemadministratör / Utvecklare

**Förutsättning:** Ett anrop har genomförts och returnerat ett felmeddelande till slutanvändaren. Användaren har tagit del av `X-Correlation-Id`-headern från svaret.

**Steg:**

1. Användaren navigerar till sidan **"Spårningsöversikt"** i frontend.
2. Användaren klistrar in `correlationId` (t.ex. `abc123def456`) i sökfältet och klickar på **"Sök"**.
3. Systemet hämtar alla loggposter och spans kopplade till detta `correlationId`.
4. Frontend visar ett **flödesdiagram** med alla steg i kedjan:
   - ✅ `webfrontend → ApiService.CallApiServiceForecast` (OK, 45 ms)
   - ✅ `apiservice → ApiServiceForecast.CallStaticWeather` (OK, 12 ms)
   - ❌ `apiservice → ApiService.ErrorFlowViaForecast` (FEL, 503 – Service Unavailable)
5. Det felande steget markeras tydligt i rött med felmeddelandet: *"Error flow failed in apiServiceForecast call. status_code=503"*.
6. Användaren klickar på det röda steget för att expandera detaljvyn.
7. Detaljvyn visar fullständig logginformation inkl. `trace_id`, `span_id`, `parent_span_id` och stack trace.
8. Användaren kan nu enkelt se exakt var i kedjan felet uppstod och vilka uppströms-steg som lyckades.

**Postcondition:** Användaren har identifierat felsteget och kan vidta åtgärder.

---

### UC-2: Verifiering av framgångsrikt asynkront flöde

**Aktör:** Systemadministratör

**Förutsättning:** Ett anrop har genomförts och en asynkron worker-job har köats.

**Steg:**

1. Användaren söker på `traceId` (kopierat från Aspire-dashboardens trace-vy).
2. Systemet visar hela flödet inkl. det asynkrona worker-jobbet:
   - ✅ `webfrontend → apiservice` (OK)
   - ✅ `apiservice → apiserviceforecast` (OK)
   - ✅ `apiservice → workerservice1` (jobbköat, OK)
   - ✅ `workerservice1 → Worker.ProcessJob` (OK, 120 ms)
   - ✅ `workerservice1 → Worker.CallStaticWeather` (OK)
3. Flödesstatus-badge visar **"Flöde slutfört – OK"** i grönt.

---

## 8. Icke-funktionella krav

**NFR-1:** Sidsvarstid för sökning och visning av ett flöde ska vara ≤ 3 sekunder vid upp till 1 000 loggposter.

**NFR-2:** Flödesvisningen ska vara responsiv och fungera på skärmar ≥ 1 024 px bredd.

**NFR-3:** Känslig information (t.ex. personuppgifter i loggposter) ska maskeras enligt GDPR-krav.

**NFR-4:** Åtkomst till spårningsöversikten ska kräva autentisering och auktorisering (minst roll "Developer" eller "Administrator").

**NFR-5:** Historik för traces ska bevaras i minst 30 dagar.

---

## 9. Acceptanskriterier

| ID | Kriterium | Verifiering |
|----|-----------|-------------|
| AC-1 | Kravspecifikationen finns dokumenterad för frontend-visualisering av processflödet | Dokumentet finns i repot under `docs/` |
| AC-2 | Kravspecifikationen beskriver sökning via `traceId` och/eller `correlationId` | Se avsnitt 4.1 (FR-1–FR-6) |
| AC-3 | Kravspecifikationen beskriver hur loggar och flödessteg kopplas till samma identifierare | Se avsnitt 4.2 (FR-7–FR-11) och avsnitt 6 |
| AC-4 | Kravspecifikationen beskriver en grafisk vy där det framgår var flödet lyckades eller misslyckades | Se avsnitt 4.3 (FR-12–FR-17) |
| AC-5 | Kravspecifikationen beskriver minst ett tänkt användarflöde från sökning till analys av resultat | Se avsnitt 7 (UC-1 och UC-2) |
| AC-6 | Definition av vad som räknas som "OK" respektive "Fel" i varje steg finns dokumenterad | Se avsnitt 5 |

---

## 10. Öppna frågor och avgränsningar

| Fråga | Status |
|-------|--------|
| Vilken teknisk plattform ska frontend byggas i? (Blazor, React, Angular etc.) | Öppen – beslutas i design-fasen |
| Vilken telemetribackend används som datakälla? (Aspire-dashboard, Jaeger, Azure Monitor) | Öppen – beroende av miljöval |
| Ska traces kunna exporteras som PDF/CSV? | Avgränsat – ingår ej i denna version |
| Ska notifieringar skickas vid felflöden? | Avgränsat – ingår ej i denna version |
| Behövs stöd för att söka via `spanId`? | Öppen – kan utredas i nästa iteration |

---

## 11. Relationer till befintlig dokumentation

- [ReadMe.md](../ReadMe.md) – beskriver DIGG + W3C Trace Context-implementationen och felsökning via `trace_id`
- [W3C Trace Context Specification](https://www.w3.org/TR/trace-context/) – standard för `traceparent`/`tracestate`
- [OpenTelemetry](https://opentelemetry.io/) – ramverk för telemetri och spårbarhet
