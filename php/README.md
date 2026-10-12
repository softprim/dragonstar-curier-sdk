# Dragon Star Curier — SDK PHP

Client PHP pentru API-ul **Dragon Star Curier v1** (`https://api.curierdragonstar.ro`):
AWB-uri, cotații, urmărire, etichete PDF/ZPL, ridicări, borderouri și nomenclatoare.

- PHP **7.4+** (merge și pe PHP 8.x), doar `ext-curl` și `ext-json`, **zero dependențe**.
- Rezultatele sunt array-uri asociative decodate din JSON; etichetele și borderourile sunt string-uri brute (bytes PDF / text ZPL).
- Reîncercări automate sigure (nu se creează niciodată AWB-uri duble).
- Licență MIT, realizat de SoftPrim Technology SRL.

## Instalare

### Cu Composer

Pachetul se instalează direct din repo-ul GitHub:

```json
{
    "repositories": [
        { "type": "vcs", "url": "https://github.com/softprim/dragonstar-curier-sdk" }
    ],
    "require": {
        "softprim/dragonstar-curier": "^1.0"
    }
}
```

```bash
composer update softprim/dragonstar-curier
```

```php
require 'vendor/autoload.php';
```

### Fără Composer

Copiați folderul `php/` în proiect și includeți un singur fișier:

```php
require '/cale/catre/php/dragonstar.php';
```

## Pornire rapidă

```php
use SoftPrim\DragonStar\Client;
use SoftPrim\DragonStar\DragonStarError;

$client = new Client(getenv('DRAGONSTAR_API_KEY'));

try {
    $awb = $client->createAwb([
        'shipmentType'     => 'parcel',
        'weight'           => 1.5,
        'recipientName'    => 'Ion Popescu',
        'recipientPhone'   => '0722123456',
        'recipientAddress' => 'Str. Memorandumului 1',
        'recipientCounty'  => 'Cluj',
        'recipientCity'    => 'Cluj-Napoca',
        'codAmount'        => 149.90,
    ]);
    echo "AWB: {$awb['awb']}, total {$awb['totalAmount']} RON\n";

    file_put_contents("awb-{$awb['awb']}.pdf", $client->getLabel($awb['awb'], 'A6'));
} catch (DragonStarError $e) {
    echo "Eroare {$e->getStatus()} ({$e->getErrorCode()}): {$e->getMessage()}\n";
}
```

### Opțiuni

```php
$client = new Client('cheia-api', [
    'baseUrl'    => 'https://api.curierdragonstar.ro', // implicit
    'timeout'    => 30,                                // secunde, per cerere
    'maxRetries' => 2,                                 // 0 = fără reîncercări
    'userAgent'  => 'magazinul-meu/1.0',               // implicit softprim-dragonstar-php/1.0.0
]);
```

## Metode

### Expediții (AWB)

```php
// Creare AWB — {awb, totalAmount, weight, pieces, codAmount, codPaymentType}
$awb = $client->createAwb($date);

// Creare AWB în numele unei sucursale (pcId din getBranches())
$awb = $client->createAwbForBranch(454, $date);

// Cotație (nu creează nimic) — {baseRate, distanceCost, weightCost, insuranceCost,
//                               isListRate, netAmount, vatAmount, totalAmount}
$pret = $client->quote(['shipmentType' => 'parcel', 'weight' => 2,
                        'recipientCounty' => 'Cluj', 'recipientCity' => 'Cluj-Napoca']);
$pret = $client->quoteForBranch(454, $date);

// Urmărire — {status, events}; al doilea parametru false = doar status
$t = $client->track(94348148);
$t = $client->track(94348148, false);

// Urmărire multiplă — {count, data[], notFound[]}
// Peste 100 AWB-uri: SDK-ul împarte automat în loturi de 100 și unește rezultatele.
$r = $client->trackMany([94348148, 94348149], /* events */ false);

// Retururi legate de un AWB — {count, data[]}
$retur = $client->getReturns(94348148);

// Ștergere (doar înainte de ridicare / borderou / facturare) — nu întoarce nimic
$client->deleteAwb(94348148);

// Etichetă PDF (bytes). Format: null (implicit cont), 'A4' sau 'A6' — exact așa.
$pdf = $client->getLabel(94348148);
$pdf = $client->getLabel(94348148, 'A4');

// Etichetă ZPL pentru coletul nr. N (N >= 1)
$zpl = $client->getLabelZpl(94348148, 1);
```

### Ridicări și borderouri

```php
// Comandă ridicare — {pickupId}
$p = $client->createPickup([
    'pickupDate'        => '2026-10-13',
    'pickupWindowStart' => '10:00',
    'pickupWindowEnd'   => '16:00',
    'weight'            => 10,
]);

// Ridicare pentru o sucursală — {pickupId} sau array gol
$p = $client->createPickupForBranch(454, $dateRidicare);

// Închidere borderou — {manifestId, shipmentCount}; manifestId = null dacă nu e nimic de închis
$m = $client->createManifest();
if ($m['manifestId'] !== null) {
    file_put_contents("borderou-{$m['manifestId']}.pdf", $client->getManifestPdf($m['manifestId']));
}
```

### Nomenclatoare

```php
// Județe: cod => nume, ex. ['CJ' => 'Cluj', 'B' => 'Bucuresti']
$judete = $client->getCounties();

// Localitățile unui județ (folosiți NUMELE județului; diacriticele/spațiile sunt codificate automat)
$localitati = $client->getLocalities('Cluj');            // ['Cluj-Napoca', ...]

// Localități cu distanța în km — [['name' => 'Cluj-Napoca', 'km' => 450], ...]
$cuKm = $client->getLocalitiesWithKm('Satu Mare');

// Sucursalele contului — {masterId, count, data[]}
$sucursale = $client->getBranches();
```

### Statusuri

```php
use SoftPrim\DragonStar\Status;

$t = $client->track(94348148, false);
$cod = $t['status']['statusCode'];

echo Status::label($cod);           // ex. "Livrat"
if (Status::isFinal($cod)) { ... }  // true pentru Livrat (3) și Returnat (7)
if ($cod === Status::OUT_FOR_DELIVERY) { ... }
```

| Constantă | Cod | Etichetă |
|---|---|---|
| `Status::GENERATED` | 1 | Generat |
| `Status::EXCEPTION` | 2 | Exceptie |
| `Status::DELIVERED` | 3 | Livrat |
| `Status::NOTIFIED` | 4 | Avizat |
| `Status::PICKED_UP` | 5 | Preluat |
| `Status::IN_TRANSIT` | 6 | In tranzit |
| `Status::RETURNED` | 7 | Returnat |
| `Status::OUT_FOR_DELIVERY` | 18 | Spre livrare |
| `Status::REDIRECTED` | 19 | Redirectionat |
| `Status::AT_DESTINATION_HUB` | 21 | In centru destinatie |

## Tratarea erorilor

Orice răspuns HTTP ≥ 400 aruncă `SoftPrim\DragonStar\DragonStarError` (extinde `\RuntimeException`):

| Metodă | Conținut |
|---|---|
| `getStatus()` | codul HTTP (0 la eroare de rețea / timeout) |
| `getErrorCode()` | codul API, ex. `validation_failed`, `awb_not_found`, `network_error` (sau `null`) |
| `getTitle()`, `getDetail()`, `getType()` | câmpurile din răspunsul problem+json |
| `getErrors()` | câmp → listă de mesaje (la `validation_failed`) |
| `getRetryAfter()` | antetul `Retry-After` în secunde, dacă există |
| `getMessage()` | `detail` → `title` → `"HTTP <status>"` |

`getCode()` (finală în PHP) întoarce tot codul HTTP; pentru codul API folosiți `getErrorCode()`.

```php
try {
    $client->createAwb($date);
} catch (DragonStarError $e) {
    if ($e->getErrorCode() === 'validation_failed') {
        foreach ($e->getErrors() as $camp => $mesaje) {
            echo "$camp: " . implode('; ', $mesaje) . "\n";
        }
    } elseif ($e->getStatus() === 0) {
        echo "Problemă de rețea: {$e->getMessage()}\n";
    } else {
        echo "Eroare {$e->getStatus()}: {$e->getMessage()}\n";
    }
}
```

Validările locale (format de etichetă greșit, de ex. `'a4'`, sau `parcel < 1` la ZPL) aruncă
`\InvalidArgumentException` înainte de a trimite cererea.

## Reîncercări

- **429** `rate_limit_exceeded` — se reîncearcă pentru orice metodă (cererea a fost refuzată, nu s-a creat nimic), după `Retry-After` secunde (implicit 1).
- **503** și erori de rețea — se reîncearcă **doar** pentru `GET` și `DELETE`. Un `POST` (AWB, ridicare, borderou) nu se reîncearcă automat, ca să nu apară AWB-uri duble.
- **429** `temporarily_banned` nu se reîncearcă (blocarea durează minute).
- Așteptarea pe reîncercare e plafonată la 60 s; numărul de reîncercări = `maxRetries` (implicit 2).

## Exemple și teste

```bash
DRAGONSTAR_API_KEY=cheia-ta php php/examples/create-awb.php
DRAGONSTAR_API_KEY=cheia-ta php php/examples/track.php 94348148

php php/tests/run.php   # pornește un server local fals (php -S); nu apelează API-ul real
```

---

## English

PHP client for the **Dragon Star Curier API v1**. PHP 7.4+, only `ext-curl` and `ext-json`, no dependencies.

**Install** — with Composer, add the VCS repository `https://github.com/softprim/dragonstar-curier-sdk`
and require `softprim/dragonstar-curier` (see the JSON above); without Composer,
`require 'php/dragonstar.php';`.

**Quick start**

```php
$client = new \SoftPrim\DragonStar\Client(getenv('DRAGONSTAR_API_KEY'), ['timeout' => 30, 'maxRetries' => 2]);
$awb = $client->createAwb([...]);                     // ['awb' => 94348148, 'totalAmount' => 9.83, ...]
file_put_contents('label.pdf', $client->getLabel($awb['awb'], 'A6'));
$status = $client->track($awb['awb'])['status'];
```

**Methods** — `createAwb`, `createAwbForBranch`, `quote`, `quoteForBranch`, `track`, `trackMany`
(auto-batches of 100), `getReturns`, `deleteAwb`, `getLabel` (raw PDF; format `null`/`'A4'`/`'A6'`),
`getLabelZpl`, `createPickup`, `createPickupForBranch`, `createManifest`, `getManifestPdf`,
`getCounties`, `getLocalities`, `getLocalitiesWithKm`, `getBranches`. Arrays in, arrays out.

**Errors** — any HTTP status ≥ 400 throws `DragonStarError` with `getStatus()`, `getErrorCode()`,
`getTitle()`, `getDetail()`, `getType()`, `getErrors()`, `getRetryAfter()`. Network errors and
timeouts throw it with status `0` and code `network_error`. Invalid local arguments throw
`\InvalidArgumentException`.

**Retries** — 429 (except `temporarily_banned`) is retried for every method after `Retry-After`;
503 and network errors are retried only for GET/DELETE, never for POST (no duplicate AWBs).
Each wait is capped at 60 s; `maxRetries` defaults to 2.

---

Module gata făcute pentru Shopify, WooCommerce, OpenCart, PrestaShop sau alte platforme, integrări personalizate și actualizări: office@softprim.ro
