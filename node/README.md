# Dragon Star Curier — SDK Node.js

Client Node.js pentru [API-ul Dragon Star Curier v1](https://api.curierdragonstar.ro/docs).
Fără dependențe, Node.js 18+, tipuri TypeScript incluse, funcționează cu `require` și `import`.

## Instalare

Din clona repo-ului:

```bash
git clone https://github.com/softprim/dragonstar-curier-sdk.git
npm install ./dragonstar-curier-sdk/node
```

Sau copiați fișierul [`src/index.js`](src/index.js) (și opțional `src/index.d.ts`) în proiect:
este un singur fișier, fără dependențe.

## Pornire rapidă

```js
const { DragonStarClient } = require('@softprim/dragonstar-curier');
// sau: import { DragonStarClient } from '@softprim/dragonstar-curier';

const dsc = new DragonStarClient(process.env.DRAGONSTAR_API_KEY);

const { awb, totalAmount } = await dsc.createAwb({
  shipmentType: 'parcel',          // envelope | parcel | pallet
  weight: 2.5,
  pieces: 1,
  recipientName: 'Ion Popescu',
  recipientAddress: 'Str. Memorandumului 1',
  recipientCounty: 'Cluj',         // NUMELE județului, nu codul CJ
  recipientCity: 'Cluj-Napoca',
  recipientPhone: '0722123456',
  codAmount: 149.9,
  codPaymentType: 'cash',
});

const pdf = await dsc.getLabel(awb, 'A6'); // Buffer
```

Opțiuni: `new DragonStarClient(key, { baseUrl, timeout /* ms, implicit 30000 */, maxRetries /* 2 */, userAgent, fetch })`.

## Metode

| Metodă | Ce face |
|---|---|
| `createAwb(data)` | Creează un AWB. |
| `createAwbForBranch(pcId, data)` | Creează un AWB în numele unui punct de lucru. |
| `quote(data)` / `quoteForBranch(pcId, data)` | Estimează costul (dacă estimarea e activată pe cont). |
| `track(awb, events = true)` | Status + traseu. `events = false` → doar status. |
| `trackMany(awbs, events = false)` | Oricâte AWB-uri; peste 100 se împart automat în loturi. |
| `getReturns(awb)` | Expedițiile de retur legate de un AWB. |
| `deleteAwb(awb)` | Șterge AWB-ul (doar înainte de preluare / borderou / facturare). |
| `getLabel(awb, format?)` | PDF (`Buffer`); `format` = `'A4'`, `'A6'` sau gol pentru formatul contului. |
| `getLabelZpl(awb, parcel = 1)` | Etichetă ZPL (text) pentru coletul `parcel`. |
| `createPickup(data)` / `createPickupForBranch(pcId, data)` | Comandă o ridicare. |
| `createManifest()` | Închide expedițiile într-un borderou → `{ manifestId, shipmentCount }` (`manifestId` null dacă nu era nimic). |
| `getManifestPdf(id)` | Borderoul PDF (`Buffer`). |
| `getCounties()` | `{ CJ: 'Cluj', ... }`. |
| `getLocalities(county)` | Lista localităților unui județ. |
| `getLocalitiesWithKm(county)` | `[{ name, km }]`. |
| `getBranches()` | Punctele de lucru ale contului. |

```js
// Urmărire
const t = await dsc.track(94348148);
console.log(t.status.descriptionRo, t.events?.length);

// Lot
const { data, notFound } = await dsc.trackMany([94348148, 94348149]);

// Ridicare
const { pickupId } = await dsc.createPickup({
  pickupDate: '2026-10-13', pickupWindowStart: 10, pickupWindowEnd: 14,
  parcels: 3, weight: 12,
});

// Borderou la final de zi
const { manifestId } = await dsc.createManifest();
if (manifestId) require('fs').writeFileSync('borderou.pdf', await dsc.getManifestPdf(manifestId));

// Statusuri
const { Status, statusLabel, isFinal } = require('@softprim/dragonstar-curier');
statusLabel(Status.OUT_FOR_DELIVERY); // 'Spre livrare'
```

## Erori și reîncercări

Orice răspuns de eroare aruncă `DragonStarError` cu `status`, `code` (stabil, ex.
`validation_failed`, `awb_not_deletable`, `ip_not_allowed`), `detail`, `errors` (câmp → mesaje)
și `retryAfter`.

```js
try {
  await dsc.deleteAwb(94348148);
} catch (err) {
  if (err.code === 'awb_not_deletable') { /* deja preluat sau în borderou */ }
  else throw err;
}
```

- **429** se reîncearcă automat (după `Retry-After`), pentru orice metodă.
- **503** și erorile de rețea se reîncearcă doar la `GET`/`DELETE`; un `POST` nu se repetă
  automat, ca să nu apară AWB-uri duble.

## Teste

```bash
npm test
```

---

## English

Zero-dependency Node.js 18+ client for the Dragon Star Curier API v1, with TypeScript types.
Install from a clone (`npm install ./dragonstar-curier-sdk/node`) or copy `src/index.js`.
Methods are listed in the table above; errors throw `DragonStarError` with a stable `code`.
429 responses are retried for every method; 503/network errors only for GET and DELETE.

---

Module gata făcute pentru Shopify, WooCommerce, OpenCart, PrestaShop sau alte platforme,
integrări personalizate și actualizări: **office@softprim.ro**
