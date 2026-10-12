# Dragon Star Curier — SDK Java

Client Java pentru [API-ul Dragon Star Curier v1](../docs/openapi.v1.json): AWB-uri, cotații,
urmărire, etichete PDF/ZPL, ridicări, borderouri și nomenclatoare.

- Java 11+ (`java.net.http.HttpClient`), **fără dependențe la runtime** (are un parser JSON intern).
- Reîncercări automate sigure (un `POST` nu se repetă la 503, deci nu apar AWB-uri duble).
- Licență MIT, © SoftPrim Technology SRL.

## Instalare

Din surse, cu Maven:

```bash
cd java
mvn install          # instalează ro.softprim:dragonstar-curier:1.0.0 în ~/.m2
```

Apoi în `pom.xml`:

```xml
<dependency>
  <groupId>ro.softprim</groupId>
  <artifactId>dragonstar-curier</artifactId>
  <version>1.0.0</version>
</dependency>
```

Fără Maven, poți copia direct cele 5 fișiere din `src/main/java/ro/softprim/dragonstar/` în proiect.

## Pornire rapidă

```java
import ro.softprim.dragonstar.*;
import java.time.Duration;
import java.util.*;

DragonStarClient client = DragonStarClient.builder(System.getenv("DRAGONSTAR_API_KEY"))
        .timeout(Duration.ofSeconds(30))   // implicit 30 s
        .maxRetries(2)                     // implicit 2; 0 = fără reîncercări
        .build();

Map<String, Object> awb = new LinkedHashMap<>();
awb.put("shipmentType", "parcel");
awb.put("weight", 2.5);
awb.put("pieces", 1);
awb.put("recipientName", "Ion Popescu");
awb.put("recipientPhone", "0722000000");
awb.put("recipientAddress", "Str. Exemplu nr. 1");
awb.put("recipientCounty", "Cluj");          // numele județului, nu codul
awb.put("recipientCity", "Cluj-Napoca");
awb.put("codAmount", 150);

Map<String, Object> created = client.createAwb(awb);
long awbNumber = ((Number) created.get("awb")).longValue();
byte[] pdf = client.getLabel(awbNumber, "A6");
```

### Modelul de date

Corpurile cererilor sunt `Map<String, Object>` cu exact numele câmpurilor din API
(vezi `docs/openapi.v1.json`). Răspunsurile vin ca `Map<String, Object>` / `List<Object>`:
obiect JSON → `LinkedHashMap`, listă → `ArrayList`, întreg → `Long`, zecimal → `Double`,
plus `String`, `Boolean` și `null`. Pentru numere folosește `((Number) x).longValue()` /
`.doubleValue()`. Unde e ieftin există tipuri dedicate: `Map<String,String>` pentru județe,
`List<String>` pentru localități, `LocalityKm` pentru localități cu distanță, `ShipmentStatus`
pentru coduri de status.

## Metode

| Metodă | Endpoint | Rezultat |
|---|---|---|
| `createAwb(data)` | `POST /v1/awb` | `{awb, totalAmount, weight, pieces, codAmount, codPaymentType}` |
| `createAwbForBranch(pcId, data)` | `POST /v1/awb/branch` | idem |
| `quote(data)` | `POST /v1/awb/quote` | `{baseRate, …, netAmount, vatAmount, totalAmount}` |
| `quoteForBranch(pcId, data)` | `POST /v1/awb/quote/branch` | idem |
| `track(awb[, events])` | `GET /v1/awb/{awb}` | `{status, events?}` |
| `trackMany(awbs[, events])` | `POST /v1/awb/tracking` | `{count, data, notFound}` (loturi automate de 100) |
| `getReturns(awb)` | `GET /v1/awb/{awb}/return` | `{count, data}` |
| `deleteAwb(awb)` | `DELETE /v1/awb/{awb}` | — |
| `getLabel(awb[, format])` | `GET /v1/awb/{awb}/label[/A4\|A6]` | `byte[]` PDF |
| `getLabelZpl(awb[, parcel])` | `GET /v1/awb/{awb}/label/zpl/{nb}` | `String` ZPL |
| `createPickup(data)` | `POST /v1/pickups` | `{pickupId}` |
| `createPickupForBranch(pcId, data)` | `POST /v1/pickups/branch` | `{pickupId}` sau map gol |
| `createManifest()` | `POST /v1/manifests` | `{manifestId, shipmentCount}` |
| `getManifestPdf(id)` | `GET /v1/manifests/{id}/pdf` | `byte[]` PDF |
| `getCounties()` | `GET /v1/counties` | `Map<String,String>` cod → nume |
| `getLocalities(county)` | `GET /v1/counties/{county}/localities` | `List<String>` |
| `getLocalitiesWithKm(county)` | `GET /v1/counties/{county}/localities-km` | `List<LocalityKm>` |
| `getBranches()` | `GET /v1/branches` | `{masterId, count, data}` |

Metodele care primesc un AWB sau un ID de borderou au variante cu `String` și cu `long`.

### Exemple

```java
// AWB în numele unei sucursale (pcId = branchId din getBranches())
Map<String, Object> r = client.createAwbForBranch(454, awb);

// Cotație înainte de a crea AWB-ul
Map<String, Object> q = client.quote(awb);
double total = ((Number) q.get("totalAmount")).doubleValue();
Map<String, Object> qb = client.quoteForBranch(454, awb);

// Urmărire cu istoric (events=true implicit) sau doar status
Map<String, Object> t = client.track(94348148L);
Map<String, Object> s = client.track(94348148L, false);
Map<?, ?> status = (Map<?, ?>) t.get("status");
int code = ((Number) status.get("code")).intValue();
String eticheta = ShipmentStatus.label(code);          // "Livrat"
boolean gata = ShipmentStatus.isFinal(code);           // true pentru 3 și 7

// Urmărire în masă: peste 100 de AWB-uri se împart automat în loturi
Map<String, Object> many = client.trackMany(List.of(94348148L, 94348149L));
List<?> negasite = (List<?>) many.get("notFound");

// Returul unei expedieri
Map<String, Object> ret = client.getReturns(94348148L);

// Ștergere (doar înainte de borderou)
client.deleteAwb(94348148L);

// Etichete
byte[] implicit = client.getLabel(94348148L);          // formatul implicit al contului
byte[] a4 = client.getLabel(94348148L, "A4");          // doar "A4" sau "A6"
String zpl = client.getLabelZpl(94348148L, 1);         // colet nr. 1
java.nio.file.Files.write(java.nio.file.Path.of("eticheta.pdf"), a4);

// Ridicare de la sediu
Map<String, Object> pickup = new LinkedHashMap<>();
pickup.put("pickupDate", "2026-10-13");
pickup.put("pickupWindowStart", 9);
pickup.put("pickupWindowEnd", 17);
pickup.put("weight", 10);
pickup.put("parcels", 2);
Map<String, Object> p = client.createPickup(pickup);              // {pickupId}
Map<String, Object> pb = client.createPickupForBranch(454, pickup);

// Borderou (închide expedițiile zilei)
Map<String, Object> m = client.createManifest();
if (m.get("manifestId") != null) {
    byte[] borderou = client.getManifestPdf(((Number) m.get("manifestId")).longValue());
}

// Nomenclatoare
Map<String, String> judete = client.getCounties();                // {"CJ" -> "Cluj", ...}
List<String> localitati = client.getLocalities("Bistrița-Năsăud"); // codificat automat în URL
for (LocalityKm l : client.getLocalitiesWithKm("Cluj")) {
    System.out.println(l.name() + " — " + l.km() + " km");
}
Map<String, Object> sucursale = client.getBranches();
```

## Erori

Orice răspuns cu status ≥ 400 aruncă `DragonStarException` (unchecked):

```java
try {
    client.createAwb(awb);
} catch (DragonStarException e) {
    e.getStatus();      // 422
    e.getCode();        // "validation_failed" — ramifică pe acest câmp
    e.getTitle();
    e.getDetail();      // e.getMessage() = detail → title → "HTTP <status>"
    e.getType();
    e.getErrors();      // Map<String, List<String>>, ex. {weight=[required if ...]}
    e.getRetryAfter();  // Integer secunde sau null
}
```

Erorile de rețea și timeout-urile devin `DragonStarException` cu `getStatus() == 0` și
`getCode() == "network_error"` (cauza originală în `getCause()`). Validările locale
(format etichetă greșit, `parcel < 1`, cheie API lipsă) aruncă `IllegalArgumentException`
înainte de orice cerere.

## Reîncercări

- **429** `rate_limit_exceeded` se reîncearcă pentru orice metodă (cererea a fost refuzată,
  nu s-a creat nimic), după `Retry-After` secunde (implicit 1).
- **503** și erorile de rețea se reîncearcă **doar** pentru `GET` și `DELETE`. Un `POST`
  (AWB, ridicare, borderou) nu se repetă automat, ca să nu se creeze dubluri.
- `temporarily_banned` nu se reîncearcă. Așteptarea maximă este de 60 s.
- `maxRetries(0)` dezactivează reîncercările.

## Teste

```bash
cd java
mvn test
```

Testele pornesc un server HTTP local (`com.sun.net.httpserver.HttpServer`) și nu apelează
niciodată API-ul real.

---

## English

Java 11+ client for the Dragon Star Curier API v1, zero runtime dependencies.

**Install:** `cd java && mvn install`, then depend on `ro.softprim:dragonstar-curier:1.0.0`
(or copy the five source files into your project).

**Quick start:**

```java
DragonStarClient client = DragonStarClient.builder("your-api-key")
        .baseUrl("https://api.curierdragonstar.ro")   // default
        .timeout(Duration.ofSeconds(30))               // default
        .maxRetries(2)                                 // default
        .userAgent("my-shop/1.0")                      // default softprim-dragonstar-java/1.0.0
        .build();
Map<String, Object> created = client.createAwb(awbData);   // {awb, totalAmount, ...}
byte[] label = client.getLabel(((Number) created.get("awb")).longValue(), "A6");
```

Request bodies are `Map<String, Object>` using the API field names; responses are
`Map<String, Object>` / `List<Object>` trees (objects → `LinkedHashMap`, integers → `Long`,
decimals → `Double`). Typed helpers: `getCounties()` → `Map<String,String>`,
`getLocalities()` → `List<String>`, `getLocalitiesWithKm()` → `List<LocalityKm>`,
`ShipmentStatus` (`fromCode`, `label`, `isFinal`).

**Errors:** every status ≥ 400 throws `DragonStarException` with `getStatus()`, `getCode()`,
`getTitle()`, `getDetail()`, `getType()`, `getErrors()`, `getRetryAfter()`. Network errors:
status `0`, code `network_error`.

**Retries:** 429 is retried for every method (not `temporarily_banned`); 503 and network
errors only for GET/DELETE; waits `Retry-After` (default 1 s, capped at 60 s).
`trackMany` splits lists larger than 100 AWBs into batches and merges the results.

---

Module gata făcute pentru Shopify, WooCommerce, OpenCart, PrestaShop sau alte platforme, integrări personalizate și actualizări: office@softprim.ro
