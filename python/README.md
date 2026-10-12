# Dragon Star Curier — SDK Python

Client Python pentru **API-ul Dragon Star Curier v1**: creare AWB, cotații, urmărire,
etichete PDF/ZPL, comenzi de ridicare, borderouri și nomenclatoare (județe, localități,
puncte de lucru).

- Python **3.8+**, doar biblioteca standard (`urllib`) — **zero dependențe**.
- Tipuri complete (`py.typed`, `TypedDict` pentru cereri și răspunsuri).
- Reîncercări automate sigure (nu creează AWB-uri duble).
- Licență MIT, dezvoltat de **SoftPrim Technology SRL**.

## Instalare

```bash
pip install "git+https://github.com/softprim/dragonstar-curier-sdk#subdirectory=python"
```

## Pornire rapidă

```python
from dragonstar import DragonStarClient, DragonStarError

client = DragonStarClient("cheia-ta-api")

try:
    res = client.create_awb({
        "shipmentType": "parcel",
        "weight": 2,
        "pieces": 1,
        "recipientName": "Ion Popescu",
        "recipientPhone": "0700000000",
        "recipientAddress": "Str. Memorandumului 1",
        "recipientCounty": "Cluj",          # numele județului, nu codul
        "recipientCity": "Cluj-Napoca",
        "codAmount": 150,
        "codPaymentType": "cash",
    })
    print("AWB:", res["awb"], "total:", res["totalAmount"], "RON")
except DragonStarError as e:
    print(e.status, e.code, e, e.errors)
```

Cheia API se generează din contul Dragon Star (Setări → Acces API) și este legată de
adresele IP publice de la care se fac cererile.

### Opțiuni client

```python
client = DragonStarClient(
    api_key="cheia-ta-api",
    base_url="https://api.curierdragonstar.ro",  # implicit
    timeout=30,                                  # secunde, per cerere
    max_retries=2,                               # 0 dezactivează reîncercările
    user_agent=None,                             # implicit softprim-dragonstar-python/1.0.0
)
```

Clientul poate fi folosit și ca manager de context: `with DragonStarClient(key) as client: ...`.

## Metode

### AWB

```python
# Creare AWB -> {awb, totalAmount, weight, pieces, codAmount, codPaymentType}
res = client.create_awb({...})

# Creare AWB pentru un punct de lucru (pcId din get_branches())
res = client.create_awb_for_branch(454, {...})

# Cotație (preț fără creare) -> {baseRate, distanceCost, weightCost, insuranceCost,
#                                isListRate, netAmount, vatAmount, totalAmount}
q = client.quote({"shipmentType": "parcel", "weight": 5,
                  "recipientCounty": "Cluj", "recipientCity": "Cluj-Napoca"})
q = client.quote_for_branch(454, {...})

# Ștergere AWB (204, nu întoarce nimic)
client.delete_awb(94348148)
```

### Urmărire

```python
from dragonstar import Status
from dragonstar.status import label, is_final

t = client.track(94348148)                 # cu istoric (events=True)
t = client.track(94348148, events=False)   # doar statusul curent
code = t["status"]["statusCode"]
print(label(code), is_final(code), code == Status.DELIVERED)

# Mai multe AWB-uri; peste 100 sunt împărțite automat în loturi de 100
batch = client.track_many([94348148, 94348149], events=False)
for item in batch["data"]:
    print(item["awb"], item["status"]["statusCode"])
print("negăsite:", batch["notFound"])

# Retururi legate de un AWB -> {count, data[]}
r = client.get_returns(94348148)
```

### Etichete

```python
pdf = client.get_label(94348148)          # format implicit al contului
pdf = client.get_label(94348148, "A6")    # doar "A4" sau "A6" (exact)
open("awb.pdf", "wb").write(pdf)

zpl = client.get_label_zpl(94348148, 1)   # text ZPL pentru coletul 1 (parcel >= 1)
```

### Ridicări (pickup)

```python
p = client.create_pickup({
    "pickupDate": "2026-10-13",
    "pickupWindowStart": 10,
    "pickupWindowEnd": 16,
    "parcels": 3,
    "weight": 12,
})
print(p["pickupId"])

p = client.create_pickup_for_branch(454, {...})   # {pickupId} sau None
```

### Borderouri (manifest)

```python
m = client.create_manifest()           # {manifestId, shipmentCount}
if m["manifestId"] is not None:        # None = nu existau expediții de închis
    pdf = client.get_manifest_pdf(m["manifestId"])
```

### Nomenclatoare

```python
client.get_counties()                   # {"CJ": "Cluj", "B": "Bucuresti", ...}
client.get_localities("Cluj")           # ["Cluj-Napoca", "Turda", ...]
client.get_localities_with_km("Cluj")   # [{"name": "Cluj-Napoca", "km": 450}, ...]
client.get_branches()                   # {masterId, count, data: [{branchId, name, ...}]}
```

Numele județului se codifică automat în URL (diacritice și spații sunt în regulă).

### Coduri de status

| Cod | Constantă | Etichetă |
|---|---|---|
| 1 | `Status.GENERATED` | Generat |
| 2 | `Status.EXCEPTION` | Exceptie |
| 3 | `Status.DELIVERED` | Livrat |
| 4 | `Status.NOTIFIED` | Avizat |
| 5 | `Status.PICKED_UP` | Preluat |
| 6 | `Status.IN_TRANSIT` | In tranzit |
| 7 | `Status.RETURNED` | Returnat |
| 18 | `Status.OUT_FOR_DELIVERY` | Spre livrare |
| 19 | `Status.REDIRECTED` | Redirectionat |
| 21 | `Status.AT_DESTINATION_HUB` | In centru destinatie |

`is_final(code)` întoarce `True` pentru 3 (Livrat) și 7 (Returnat).

## Erori

Orice răspuns HTTP ≥ 400 aruncă `DragonStarError`, cu atributele:

| Atribut | Descriere |
|---|---|
| `status` | codul HTTP (0 pentru erori de rețea sau argumente invalide) |
| `code` | codul stabil din API, ex. `validation_failed`, `awb_not_found`, `rate_limit_exceeded`; `network_error` pentru rețea/timeout; `invalid_argument` pentru validări locale |
| `title`, `detail`, `type` | din documentul problem+json (RFC 9457) |
| `errors` | dict câmp → listă de mesaje (la `validation_failed`) |
| `retry_after` | antetul `Retry-After`, în secunde, dacă există |

Mesajul excepției este `detail`, apoi `title`, apoi `"HTTP <status>"`.

```python
try:
    client.delete_awb(94348148)
except DragonStarError as e:
    if e.code == "awb_not_deletable":
        ...
    elif e.code == "validation_failed":
        for field, messages in e.errors.items():
            print(field, messages)
```

Folosiți `code` pentru decizii în cod; `detail` este text pentru oameni și se poate schimba.

## Reîncercări

- **429 `rate_limit_exceeded`** — se reîncearcă pentru orice metodă (cererea a fost
  refuzată, nu s-a creat nimic), după `Retry-After` secunde (implicit 1).
- **503 și erori de rețea** — se reîncearcă **doar** pentru `GET` și `DELETE`. Un `POST`
  (creare AWB, ridicare, borderou) **nu** se reîncearcă automat, ca să nu apară AWB-uri duble.
- **429 `temporarily_banned`** — nu se reîncearcă (blocarea durează minute).
- Așteptarea per reîncercare este plafonată la 60 s; numărul de reîncercări = `max_retries`
  (implicit 2, `0` le dezactivează).

## Exemple

```bash
export DRAGONSTAR_API_KEY=cheia-ta-api
python examples/create_awb.py
python examples/track.py 94348148 94348149
```

## Teste

Testele rulează pe un server HTTP local fals (nu apelează API-ul real):

```bash
python -m unittest discover -s python/tests -t python
```

---

## English

Python client for the **Dragon Star Curier API v1** (AWB creation, quotes, tracking,
PDF/ZPL labels, pickups, manifests, counties/localities/branches). Python 3.8+,
standard library only, fully typed, MIT licensed, by SoftPrim Technology SRL.

**Install**

```bash
pip install "git+https://github.com/softprim/dragonstar-curier-sdk#subdirectory=python"
```

**Quick start**

```python
from dragonstar import DragonStarClient, DragonStarError

client = DragonStarClient("your-api-key")
res = client.create_awb({"shipmentType": "envelope", "recipientName": "John Doe",
                         "recipientAddress": "1 Example St", "recipientCounty": "Cluj",
                         "recipientCity": "Cluj-Napoca"})
print(res["awb"])
print(client.track(res["awb"])["status"])
```

Methods: `create_awb`, `create_awb_for_branch`, `quote`, `quote_for_branch`, `track`,
`track_many` (auto-batches by 100), `get_returns`, `delete_awb`, `get_label` (bytes; format
`"A4"`/`"A6"`), `get_label_zpl` (str), `create_pickup`, `create_pickup_for_branch`,
`create_manifest`, `get_manifest_pdf` (bytes), `get_counties`, `get_localities`,
`get_localities_with_km`, `get_branches`.

Errors raise `DragonStarError` (`status`, `code`, `title`, `detail`, `type`, `errors`,
`retry_after`). Branch on `code`. 429 is retried for every method (except
`temporarily_banned`); 503 and network errors are retried only for GET/DELETE, so a POST is
never sent twice. Waits are capped at 60 s; `max_retries=0` disables retries.

---

Module gata făcute pentru Shopify, WooCommerce, OpenCart, PrestaShop sau alte platforme, integrări personalizate și actualizări: office@softprim.ro
