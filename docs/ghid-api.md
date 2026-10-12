# Ghid API Dragon Star Curier v1

Ghid practic pentru integrarea cu noul API Dragon Star Curier, scris de SoftPrim pe baza
documentației oficiale. Sursa de adevăr rămâne documentația oficială:
**https://api.curierdragonstar.ro/docs** (specificația OpenAPI: [`openapi.v1.json`](openapi.v1.json),
[`openapi.v1.en.json`](openapi.v1.en.json), copii din 2026-10-12).

> English version: [api-guide.en.md](api-guide.en.md)

## Cuprins

1. [Calendarul migrării](#1-calendarul-migrării)
2. [Obținerea cheii API](#2-obținerea-cheii-api)
3. [Autentificare](#3-autentificare)
4. [Limite de cereri](#4-limite-de-cereri)
5. [Erori](#5-erori)
6. [Convenții](#6-convenții)
7. [Endpoint-uri](#7-endpoint-uri)
8. [Câmpurile unui AWB](#8-câmpurile-unui-awb)
9. [Ridicări](#9-ridicări)
10. [Statusuri de expediție](#10-statusuri-de-expediție)
11. [Flux recomandat pentru un magazin online](#11-flux-recomandat-pentru-un-magazin-online)

## 1. Calendarul migrării

- API-ul vechi **nu mai primește modificări** și rămâne disponibil **cel puțin până la 31 martie 2027**.
- Funcționalitățile noi apar doar în API-ul v1 (`https://api.curierdragonstar.ro`).
- Întrebări tehnice către Dragon Star: `api@curierdragonstar.ro`.

## 2. Obținerea cheii API

1. Autentificați-vă în aplicația web DSC (**https://app.dscexpres.ro**) cu contul **principal (master)** al companiei.
2. Activați **autentificarea în doi pași**: *Setări → Autentificare în doi pași* (Google Authenticator,
   Microsoft Authenticator etc.). Accesul API există doar pentru conturile protejate astfel.
3. *Setări → Acces API*, confirmați cu codul din aplicație și apăsați **„Creează o cheie API”**.
4. Completați denumirea, data de expirare (**maximum 12 luni**) și **adresele IP publice** ale
   serverelor de pe care veți apela API-ul.
5. Copiați cheia: **se afișează o singură dată**.

Observații:

- Maximum **5 chei active** simultan. Folosiți chei separate pentru test și producție.
- O cerere de pe un IP nelistat primește **403** cu `code: ip_not_allowed`. Pe hosting partajat
  sau în cloud, verificați IP-ul public de ieșire (ex. `curl https://ifconfig.me` rulat de pe server).
- Puneți în calendar data de expirare a cheii: după expirare, toate cererile primesc 401.

## 3. Autentificare

Cheia se trimite în unul dintre antete (dacă sunt ambele, câștigă `Authorization`):

```
Authorization: Bearer <cheie>
X-Api-Key: <cheie>
```

Nu puneți niciodată cheia în cod JavaScript din browser sau în aplicații mobile: apelați API-ul
doar de pe server.

## 4. Limite de cereri

Per cheie API și per rută:

| Metodă | Pe secundă | Pe minut |
|---|---|---|
| `GET` | 40 | 1200 |
| celelalte | 20 | 600 |

- Fiecare răspuns are `X-RateLimit-Limit` și `X-RateLimit-Remaining`.
- La depășire: **429** cu `code: rate_limit_exceeded` și `Retry-After` (1 s sau 60 s).
- Rutele publice (documentația, verificarea de disponibilitate, căi inexistente): 2/s și 30/min per IP.
  Sondarea repetată a căilor inexistente blochează IP-ul (`temporarily_banned`): 5 min după 5
  depășiri, 10 min după 10, o oră după 20.

Toate SDK-urile din acest repo reîncearcă automat la 429, respectând `Retry-After`.

## 5. Erori

Erorile respectă [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) (`Content-Type: application/problem+json`):

```json
{
  "type": "https://api.curierdragonstar.ro/errors/validation-failed",
  "title": "Validation failed",
  "status": 422,
  "code": "validation_failed",
  "detail": "The request has 2 invalid fields.",
  "errors": {
    "weight": ["required if shipmentType = parcel or pallet"],
    "recipientCity": ["required"]
  }
}
```

Logica se ramifică după **`code`** (stabil); `detail` e text pentru oameni și se poate schimba.

| HTTP | `code` | Când apare |
|---|---|---|
| 400 | `invalid_json`, `empty_body` | Corpul nu e un obiect JSON valid. |
| 401 | `unauthorized` | Cheie lipsă, greșită sau expirată. |
| 403 | `ip_not_allowed` | IP-ul nu e pe lista cheii. |
| 403 | `account_not_eligible` | Contul nu are acces la operațiune. |
| 403 | `quotes_not_enabled` | Estimarea de preț nu e activată pe cont. |
| 404 | `awb_not_found`, `return_not_found`, `manifest_not_found`, `county_not_found`, `endpoint_not_found` | Resursa nu există (sau e a altui client). |
| 405 | `method_not_allowed` | Metodă greșită; antetul `Allow` spune ce e permis. |
| 409 | `awb_not_deletable` | AWB-ul a fost preluat, inclus în borderou sau facturat. |
| 422 | `validation_failed` | Valori invalide; detalii în `errors`. |
| — | `manifest_empty` | Borderou fără expediții (codul HTTP nu e precizat în specificație). |
| 429 | `rate_limit_exceeded` | Limită depășită; așteptați `Retry-After`. |
| 429 | `temporarily_banned` | IP blocat temporar. |
| 503 | `service_unavailable` | Indisponibil temporar; se poate reîncerca. |

> **Atenție la reîncercări pentru `POST /v1/awb`:** dacă primiți 503 sau o eroare de rețea după
> trimitere, nu repetați orbește — verificați întâi (ex. după observații / referința comenzii) că
> AWB-ul nu a fost totuși creat. SDK-urile nu reîncearcă automat `POST`-urile din acest motiv.

## 6. Convenții

- Datele sunt ISO 8601 cu fus orar: `2026-09-07T15:25:28+03:00`.
- Sumele sunt numere JSON, în **RON**.
- Evenimentele de urmărire sunt ordonate **de la cel mai recent**; traseul e plafonat la 100 evenimente.
- Urmărirea în lot acceptă maximum **100 AWB-uri** pe cerere.
- Județul se trimite prin **nume** (`Cluj`), nu prin cod (`CJ`). Majusculele și diacriticele nu contează.

## 7. Endpoint-uri

Bază: `https://api.curierdragonstar.ro`

### Expediții

| Metodă | Cale | Descriere |
|---|---|---|
| `POST` | `/v1/awb` | Creează un AWB → `201` `{awb, totalAmount, weight, pieces, codAmount, codPaymentType}` + `Location` |
| `POST` | `/v1/awb/branch` | Ca mai sus, expeditor = punctul de lucru `pcId` (în corp) |
| `POST` | `/v1/awb/quote` | Estimează costul, nu creează nimic → `{baseRate, distanceCost, weightCost, insuranceCost, isListRate, netAmount, vatAmount, totalAmount}` |
| `POST` | `/v1/awb/quote/branch` | Estimare pentru un punct de lucru (`pcId` în corp) |
| `GET` | `/v1/awb/{awb}?events=true` | Status curent + traseu (`events=false` → doar status) |
| `POST` | `/v1/awb/tracking?events=false` | Corp `{"awbs": [..]}` (max 100) → `{count, data[], notFound[]}` |
| `GET` | `/v1/awb/{awb}/return` | Expedițiile de retur legate de AWB |
| `DELETE` | `/v1/awb/{awb}` | Șterge → `204`; `409 awb_not_deletable` dacă e prea târziu |
| `GET` | `/v1/awb/{awb}/label` | Etichetă PDF în formatul contului |
| `GET` | `/v1/awb/{awb}/label/{A4\|A6}` | Etichetă PDF în format anume (exact `A4`/`A6`, cu majusculă) |
| `GET` | `/v1/awb/{awb}/label/zpl/{nb}` | ZPL 100×150 mm la 203 DPI pentru coletul `nb` (1…`pieces`) |

### Ridicări

| Metodă | Cale | Descriere |
|---|---|---|
| `POST` | `/v1/pickups` | Comandă o ridicare → `201` `{pickupId}` |
| `POST` | `/v1/pickups/branch` | Ridicare pentru un punct de lucru (`pcId` în corp) |

### Borderouri

| Metodă | Cale | Descriere |
|---|---|---|
| `POST` | `/v1/manifests` | Închide expedițiile fără borderou → `201` `{manifestId, shipmentCount}`; `200` cu `manifestId: null` dacă nu e nimic de închis |
| `GET` | `/v1/manifests/{manifest}/pdf` | Borderoul în PDF |

După includerea în borderou, AWB-urile **nu mai pot fi șterse**.

### Nomenclator

| Metodă | Cale | Descriere |
|---|---|---|
| `GET` | `/v1/counties` | `{count, data: {"CJ": "Cluj", ...}}` |
| `GET` | `/v1/counties/{county}/localities` | `{count, data: ["Cluj-Napoca", ...]}` |
| `GET` | `/v1/counties/{county}/localities-km` | `{count, data: ["Cluj-Napoca:450", ...]}` |
| `GET` | `/v1/branches` | `{masterId, count, data: [{branchId, name, county, city, address}]}` |

## 8. Câmpurile unui AWB

Obligatorii: `shipmentType`, `recipientName`, `recipientAddress`, `recipientCounty`, `recipientCity`.

| Câmp | Tip | Reguli |
|---|---|---|
| `shipmentType` | `envelope` \| `parcel` \| `pallet` | Determină câmpurile obligatorii. |
| `weight` | number (kg) | Obligatoriu la colet (1–9999) și palet (20–9999). La plic e ignorat (se înregistrează 0.5). |
| `pieces` | integer | Obligatoriu la colet (1–99). La plic și palet se înregistrează 1. |
| `recipientName` | string 3–255 | Tipărit pe etichetă. |
| `recipientAddress` | string 4–255 | Tipărit pe etichetă. |
| `recipientCounty` | string | Numele județului (din `/v1/counties`). |
| `recipientCity` | string | Localitatea (din `/v1/counties/{county}/localities`). |
| `recipientContact` | string ≤100 | Persoană de contact, dacă diferă. |
| `recipientPhone` | string ≤50 | Obligatoriu și verificat (`07XXXXXXXX`) doar cu `smsNotification`. |
| `recipientEmail` | email ≤100 | |
| `recipientPostalCode` | string ≤20 | |
| `length`, `width`, `height` | number (cm) | Toate trei sau niciuna. Max 800 / 300 / 300. |
| `codAmount` | number 0–15000 | Ramburs. |
| `codPaymentType` | `cash` \| `bankOrder` \| `cheque` \| `account` | Obligatoriu când `codAmount > 0`. Contul poate schimba valoarea înregistrată (vezi răspunsul). |
| `insuredValue` | number 0–15000 | Valoare declarată (procent din contract). |
| `returnAwb` | bool | Retur AWB semnat. |
| `returnDocuments` | bool | Retur documente semnate (cost suplimentar). |
| `returnPackaging` | bool | Retur ambalaj (cost suplimentar). |
| `returnParcel` | bool | Retur colet (cost suplimentar). |
| `smsNotification` | bool | Necesită telefon mobil. |
| `openOnDelivery` | bool | Deschidere la livrare. |
| `saturdayDelivery` | bool | Livrare sâmbăta (cost suplimentar). |
| `deliverToBranch` | bool | Livrare la sediul curierului. |
| `recipientPays` | bool | Transport plătit de destinatar. |
| `reverseShipment` | bool | Expediere inversă; incompatibil cu `codPaymentType: account`. |
| `notes` | string ≤1000 | Observații tipărite pe etichetă (util pentru nr. comenzii). |
| `documentDetails` | string ≤1000 | Detalii documente însoțitoare. |

Pentru `/v1/awb/quote` se trimit aceleași câmpuri, fără cele ale destinatarului (în afară de județ și localitate) și fără `pieces`.

## 9. Ridicări

| Câmp | Reguli |
|---|---|
| `pickupDate` | `YYYY-MM-DD`, nu în trecut. Obligatoriu. |
| `pickupWindowStart` | Ora 9–16. Obligatoriu. |
| `pickupWindowEnd` | Ora 10–17. Obligatoriu. |
| `weight` | kg, minim 1. Obligatoriu. |
| `parcels` / `pallets` | Cel puțin unul > 0. |
| `pickupLocation` | Dacă e completat, `pickupAddress` devine obligatoriu. |
| `pickupAddress`, `pickupCounty`, `pickupCity` | Adresa de ridicare, dacă diferă de cea a contului. |
| `pickupContact`, `pickupPhone`, `pickupEmail` | Contact la ridicare. |
| `volume` | m³. |
| `notes` | ≤1000 caractere. |

## 10. Statusuri de expediție

`statusCode` este stabil și potrivit pentru logică:

| Cod | Status | Final? |
|---|---|---|
| 1 | Generat | |
| 2 | Excepție (problemă de livrare) | |
| 3 | Livrat | <img src="../.github/assets/icons/check.svg" width="16" height="16" alt="da"> |
| 4 | Avizat | |
| 5 | Preluat | |
| 6 | În tranzit | |
| 7 | Returnat | <img src="../.github/assets/icons/check.svg" width="16" height="16" alt="da"> |
| 18 | Spre livrare | |
| 19 | Redirecționat | |
| 21 | În centrul de destinație | |

Fiecare eveniment are și `isException` (problemă de livrare), `eventCode` (ex. `INC`) și `location`.

## 11. Flux recomandat pentru un magazin online

1. **La configurare:** încărcați `/v1/counties` și, la nevoie, localitățile; păstrați-le în cache
   (se schimbă rar) și folosiți-le pentru validarea adresei în checkout.
2. **Opțional, în checkout:** `POST /v1/awb/quote` pentru costul de livrare.
3. **La expedierea comenzii:** `POST /v1/awb` cu `notes` = numărul comenzii; salvați `awb` pe comandă.
4. **Etichetă:** `GET /v1/awb/{awb}/label/A6` (sau ZPL pentru imprimante termice).
5. **Anulare:** `DELETE /v1/awb/{awb}` cât timp coletul nu a fost preluat.
6. **Final de zi:** `POST /v1/manifests` + PDF-ul borderoului; la nevoie `POST /v1/pickups`.
7. **Urmărire:** un job periodic (ex. la 1–2 ore) cu `POST /v1/awb/tracking` pentru AWB-urile
   nefinalizate (loturi de 100); opriți urmărirea la statusurile finale 3 și 7.

---

Aveți nevoie de un modul gata făcut (Shopify, WooCommerce, OpenCart, PrestaShop, Magento),
de o integrare cu ERP-ul / facturarea sau de actualizări? Scrieți-ne la **office@softprim.ro**.
