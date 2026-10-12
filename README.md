# Dragon Star Curier — SDK-uri open-source

[![CI](https://github.com/softprim/dragonstar-curier-sdk/actions/workflows/ci.yml/badge.svg)](https://github.com/softprim/dragonstar-curier-sdk/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

Documentație și clienți gratuiți (MIT) pentru **noul API Dragon Star Curier v1**
(`https://api.curierdragonstar.ro`), în limbajele folosite cel mai des de magazinele online și
de aplicațiile de facturare / ERP din România. Toate SDK-urile au aceleași metode, aceleași
reguli de erori și reîncercări, și teste automate.

> **English:** free, MIT-licensed clients for the new Dragon Star Curier (Romanian courier)
> API v1, in Node.js, PHP, Python, C#/.NET, Java and Go. See [the English guide](docs/api-guide.en.md).

| Limbaj | Folder | Cerințe | Dependențe |
|---|---|---|---|
| Node.js / TypeScript | [`node/`](node) | Node 18+ | niciuna |
| PHP | [`php/`](php) | PHP 7.4+ (ext-curl, ext-json) | niciuna |
| Python | [`python/`](python) | Python 3.8+ | niciuna |
| C# / .NET | [`dotnet/`](dotnet) | .NET Standard 2.0 / .NET 8 | System.Text.Json |
| Java | [`java/`](java) | Java 11+ | niciuna |
| Go | [`go/`](go) | Go 1.21+ | niciuna |
| cURL | [`examples/curl/`](examples/curl) | — | — |

## Documentație

- 📘 [Ghid API în română](docs/ghid-api.md): cheie API, autentificare, limite, erori, toate endpoint-urile, câmpurile AWB, statusuri, flux recomandat pentru magazine.
- 📗 [API guide in English](docs/api-guide.en.md)
- 📄 Specificația OpenAPI oficială (copie de referință): [`docs/openapi.v1.json`](docs/openapi.v1.json) (RO), [`docs/openapi.v1.en.json`](docs/openapi.v1.en.json) (EN)
- 🔗 Documentația oficială Dragon Star: https://api.curierdragonstar.ro/docs

## Exemplu rapid

**Node.js**

```js
const { DragonStarClient } = require('@softprim/dragonstar-curier');
const dsc = new DragonStarClient(process.env.DRAGONSTAR_API_KEY);

const { awb } = await dsc.createAwb({
  shipmentType: 'parcel', weight: 2.5, pieces: 1,
  recipientName: 'Ion Popescu', recipientAddress: 'Str. Memorandumului 1',
  recipientCounty: 'Cluj', recipientCity: 'Cluj-Napoca',
  recipientPhone: '0722123456', codAmount: 149.9, codPaymentType: 'cash',
});
const pdf = await dsc.getLabel(awb, 'A6');
```

**PHP**

```php
$dsc = new \SoftPrim\DragonStar\Client(getenv('DRAGONSTAR_API_KEY'));
$awb = $dsc->createAwb([
    'shipmentType' => 'parcel', 'weight' => 2.5, 'pieces' => 1,
    'recipientName' => 'Ion Popescu', 'recipientAddress' => 'Str. Memorandumului 1',
    'recipientCounty' => 'Cluj', 'recipientCity' => 'Cluj-Napoca',
])['awb'];
file_put_contents("awb-$awb.pdf", $dsc->getLabel($awb, 'A6'));
```

**Python**

```python
from dragonstar import DragonStarClient
dsc = DragonStarClient(os.environ["DRAGONSTAR_API_KEY"])
awb = dsc.create_awb({"shipmentType": "parcel", "weight": 2.5, "pieces": 1,
                      "recipientName": "Ion Popescu", "recipientAddress": "Str. Memorandumului 1",
                      "recipientCounty": "Cluj", "recipientCity": "Cluj-Napoca"})["awb"]
```

Exemple complete și instalare în README-ul fiecărui folder.

## Ce oferă toate SDK-urile

- Toate cele 18 operații din API v1: AWB (creare, estimare, ștergere), urmărire individuală și în
  lot (împărțire automată peste 100 AWB-uri), retururi, etichete PDF A4/A6 și ZPL, ridicări,
  borderouri, nomenclator (județe, localități, puncte de lucru).
- Erori structurate (`code` stabil, `errors` pe câmpuri, `retryAfter`).
- Reîncercare automată la 429 (limită de cereri), cu respectarea `Retry-After`.
- **Fără** reîncercare automată la `POST`, ca să nu se creeze AWB-uri duble.
- Constante pentru statusurile de expediție + etichete în română.
- Teste pe un server local fals: nu au nevoie de cheie API și nu ating API-ul real.

Contractul comun e descris în [CONTRIBUTING.md](CONTRIBUTING.md).

## Obținerea cheii API (pe scurt)

1. Intrați în **https://app.dscexpres.ro** cu contul principal (master).
2. Activați autentificarea în doi pași (*Setări → Autentificare în doi pași*).
3. *Setări → Acces API* → **Creează o cheie API**: denumire, expirare (max 12 luni), IP-urile publice ale serverelor.
4. Copiați cheia (se afișează o singură dată). Maximum 5 chei active; folosiți chei separate pentru test și producție.

Detalii în [ghidul API](docs/ghid-api.md#2-obținerea-cheii-api). API-ul vechi rămâne disponibil
cel puțin până la **31 martie 2027**, dar nu mai primește funcționalități noi.

## 🛒 Module pentru platforme de e-commerce

Aveți nevoie de un **modul gata de instalat** pentru **Shopify, WooCommerce, OpenCart, PrestaShop,
Magento** sau altă platformă? De o integrare cu ERP-ul, programul de facturare sau gestiunea voastră?
De **actualizări**, suport sau o funcționalitate nouă în SDK?

📧 Scrieți-ne la **[office@softprim.ro](mailto:office@softprim.ro)** — SoftPrim Technology SRL.

> Need a ready-made Shopify / WooCommerce / OpenCart / PrestaShop / Magento module, a custom
> integration or updates? Contact **office@softprim.ro**.

## Contribuții

Pull request-urile sunt binevenite: vezi [CONTRIBUTING.md](CONTRIBUTING.md). Probleme și
sugestii: [Issues](https://github.com/softprim/dragonstar-curier-sdk/issues).

## Notă legală

Proiect **neoficial**, întreținut de [SoftPrim Technology SRL](https://softprim.ro). Nu este afiliat
cu, sponsorizat sau aprobat de Dragon Star Curier SRL. „Dragon Star Curier” și „DSC” sunt mărci ale
proprietarilor lor. Codul este sub licență [MIT](LICENSE); fișierele de specificație OpenAPI din
`docs/` aparțin Dragon Star Curier SRL și sunt incluse doar ca referință. Pentru întrebări despre
contul și API-ul Dragon Star: `api@curierdragonstar.ro`.
