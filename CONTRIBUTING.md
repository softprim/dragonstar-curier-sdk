# Contribuții / Contributing

Mulțumim că vrei să contribui! Acest repo conține câte un client (SDK) pentru API-ul
Dragon Star Curier v1 în mai multe limbaje. Toate SDK-urile respectă **același contract**,
descris mai jos, ca un dezvoltator să poată trece ușor de la un limbaj la altul.

Thanks for contributing! Every SDK in this repo follows the **same contract** below.

- Deschide un *issue* înainte de schimbări mari. / Open an issue before large changes.
- Fiecare schimbare de comportament vine cu teste. / Behaviour changes need tests.
- Testele **nu** apelează API-ul real: folosesc un server HTTP local fals.
  / Tests never hit the real API; they use a local fake HTTP server.
- Fără dependențe externe la runtime, dacă se poate evita. / No runtime dependencies where avoidable.

Întrebări, module pentru platforme (Shopify, WooCommerce, OpenCart, PrestaShop, Magento...):
**office@softprim.ro**.

---

## Contractul SDK (SDK contract)

### Configurare (client options)

| Opțiune | Implicit | Descriere |
|---|---|---|
| `apiKey` | — (obligatoriu) | Cheia API. Trimisă ca `Authorization: Bearer <cheie>`. |
| `baseUrl` | `https://api.curierdragonstar.ro` | Fără slash final. |
| `timeout` | 30 secunde | Timeout per cerere. |
| `maxRetries` | 2 | Reîncercări automate (vezi mai jos). `0` le dezactivează. |
| `userAgent` | `softprim-dragonstar-<limbaj>/<versiune>` | Antetul `User-Agent`. |

Antete trimise mereu: `Authorization: Bearer <apiKey>`, `Accept` (JSON; la etichete/borderou PDF și ZPL se acceptă și `application/pdf` / `application/octet-stream` + `application/problem+json`),
`User-Agent`. Pentru corpuri JSON: `Content-Type: application/json`.

### Reîncercări (retries)

- **429** (`rate_limit_exceeded`) → se reîncearcă pentru **orice** metodă (cererea a fost
  refuzată, deci nu s-a creat nimic). Se așteaptă `Retry-After` secunde (implicit 1).
- **503** (`service_unavailable`) și erori de rețea → se reîncearcă **doar** pentru `GET`
  și `DELETE`. Un `POST` (creare AWB, ridicare, borderou) **nu** se reîncearcă automat,
  ca să nu se creeze AWB-uri duble.
- `temporarily_banned` (429) **nu** se reîncearcă (blocarea durează minute).
- Așteptarea maximă per reîncercare este plafonată la 60 s.

### Erori

Orice răspuns ≥ 400 aruncă o excepție `DragonStarError` (sau echivalentul idiomatic) cu:

| Câmp | Sursă |
|---|---|
| `status` | codul HTTP |
| `code` | câmpul `code` din problem+json (ex. `validation_failed`); `null`/gol dacă lipsește |
| `title`, `detail`, `type` | din problem+json |
| `errors` | map câmp → listă de mesaje (doar la `validation_failed`) |
| `retryAfter` | antetul `Retry-After`, în secunde, dacă există |
| mesaj | `detail` → `title` → `"HTTP <status>"` |

Erorile de rețea / timeout se propagă cu tipul nativ al limbajului sau sunt împachetate
într-o `DragonStarError` cu `status = 0` și `code = "network_error"`.

### Metode

Numele sunt în camelCase; fiecare limbaj le adaptează idiomatic (PHP/Java/JS: camelCase,
Python/Go: snake_case/PascalCase, C#: PascalCase + `Async`).

| Metodă | HTTP | Rezultat |
|---|---|---|
| `createAwb(data)` | `POST /v1/awb` | `{awb, totalAmount, weight, pieces, codAmount, codPaymentType}` |
| `createAwbForBranch(pcId, data)` | `POST /v1/awb/branch` (corp = data + `pcId`) | idem |
| `quote(data)` | `POST /v1/awb/quote` | `{baseRate, distanceCost, weightCost, insuranceCost, isListRate, netAmount, vatAmount, totalAmount}` |
| `quoteForBranch(pcId, data)` | `POST /v1/awb/quote/branch` | idem |
| `track(awb, events = true)` | `GET /v1/awb/{awb}?events=true\|false` | `{status, events?}` |
| `trackMany(awbs, events = false)` | `POST /v1/awb/tracking?events=…` corp `{awbs}` | `{count, data[], notFound[]}`. **Peste 100 AWB-uri: împarte automat în loturi de 100 și unește rezultatele.** |
| `getReturns(awb)` | `GET /v1/awb/{awb}/return` | `{count, data[]}` |
| `deleteAwb(awb)` | `DELETE /v1/awb/{awb}` | nimic (204) |
| `getLabel(awb, format = null)` | `GET /v1/awb/{awb}/label[/{A4\|A6}]` | bytes PDF |
| `getLabelZpl(awb, parcel = 1)` | `GET /v1/awb/{awb}/label/zpl/{nb}` | text ZPL |
| `createPickup(data)` | `POST /v1/pickups` | `{pickupId}` |
| `createPickupForBranch(pcId, data)` | `POST /v1/pickups/branch` | `{pickupId}` sau gol |
| `createManifest()` | `POST /v1/manifests` (fără corp) | `{manifestId, shipmentCount}`; `manifestId` = null când nu există expediții de închis |
| `getManifestPdf(manifestId)` | `GET /v1/manifests/{id}/pdf` | bytes PDF |
| `getCounties()` | `GET /v1/counties` | map cod → nume (ex. `CJ → Cluj`) — câmpul `data` |
| `getLocalities(county)` | `GET /v1/counties/{county}/localities` | listă de nume — câmpul `data` |
| `getLocalitiesWithKm(county)` | `GET /v1/counties/{county}/localities-km` | listă `{name, km}` parsată din `"Nume:km"` (separă la **ultimul** `:`) |
| `getBranches()` | `GET /v1/branches` | `{masterId, count, data[]}` |

Reguli:

- `county` se codifică în URL (`encodeURIComponent` / `rawurlencode`), pentru diacritice și spații.
- `format` pentru etichetă: doar `A4` sau `A6` (exact, cu majusculă); altă valoare → eroare locală
  înainte de cerere.
- Parametrul `events` se trimite ca `true` / `false` (text).
- `getLabelZpl` validează `parcel >= 1` local.

### Constante de status

```
1  GENERATED        Generat
2  EXCEPTION        Exceptie
3  DELIVERED        Livrat
4  NOTIFIED         Avizat
5  PICKED_UP        Preluat
6  IN_TRANSIT       In tranzit
7  RETURNED         Returnat
18 OUT_FOR_DELIVERY Spre livrare
19 REDIRECTED       Redirectionat
21 AT_DESTINATION_HUB In centru destinatie
```

Fiecare SDK expune aceste constante plus o funcție/metodă care întoarce eticheta în română
pentru un cod, și o funcție `isFinal(code)` (true pentru 3 = Livrat și 7 = Returnat).

### Teste

Fiecare SDK are teste care pornesc un server HTTP local și verifică cel puțin:

1. antetul `Authorization: Bearer …` și `User-Agent`;
2. `createAwb` trimite JSON-ul corect și întoarce AWB-ul;
3. o eroare 422 devine `DragonStarError` cu `code = validation_failed` și `errors`;
4. 429 cu `Retry-After: 0` se reîncearcă și reușește;
5. un `POST` care primește 503 **nu** se reîncearcă;
6. `trackMany` cu 150 AWB-uri face 2 cereri și unește `data` + `notFound`;
7. `getLabel` întoarce bytes neschimbați și refuză formatul `a4`;
8. `getLocalitiesWithKm` parsează `"Cluj-Napoca:450"`;
9. județul cu diacritice / spații este codificat în URL.
