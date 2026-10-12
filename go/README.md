# Dragon Star Curier — SDK Go

Client Go pentru API-ul **Dragon Star Curier v1** (`https://api.curierdragonstar.ro`).
Fără dependențe externe (doar biblioteca standard), Go 1.21+. Licență MIT,
întreținut de SoftPrim Technology SRL.

## Instalare

```bash
go get github.com/softprim/dragonstar-curier-sdk/go
```

```go
import dragonstar "github.com/softprim/dragonstar-curier-sdk/go"
```

## Pornire rapidă

```go
ctx := context.Background()
client := dragonstar.NewClient(os.Getenv("DRAGONSTAR_API_KEY"))

awb, err := client.CreateAwb(ctx, &dragonstar.AwbCreateRequest{
    ShipmentType:     dragonstar.ShipmentParcel,
    Weight:           dragonstar.Float64(2.5),
    Pieces:           dragonstar.Int(1),
    RecipientName:    "Ion Popescu",
    RecipientAddress: "Str. Memorandumului 1",
    RecipientCounty:  "Cluj",        // numele județului, nu codul (CJ)
    RecipientCity:    "Cluj-Napoca",
    CodAmount:        dragonstar.Float64(150),
    CodPaymentType:   dragonstar.CodCash,
})
if err != nil {
    log.Fatal(err)
}
fmt.Println("AWB:", awb.Awb, "total:", awb.TotalAmount)
```

### Opțiuni client

```go
client := dragonstar.NewClient(apiKey,
    dragonstar.WithBaseURL("https://api.curierdragonstar.ro"), // implicit
    dragonstar.WithTimeout(30*time.Second),                   // per încercare; implicit 30s
    dragonstar.WithMaxRetries(2),                             // implicit 2; 0 = fără reîncercări
    dragonstar.WithUserAgent("magazinul-meu/1.0"),            // implicit softprim-dragonstar-go/1.0.0
    dragonstar.WithHTTPClient(&http.Client{}),                // proxy, transport propriu etc.
)
```

Cheia se trimite ca `Authorization: Bearer <cheie>`. Toate metodele primesc
`context.Context` ca prim argument (anulare, termene limită). Câmpurile opționale
numerice sunt pointeri — folosește `dragonstar.Float64(...)` și `dragonstar.Int(...)`.

## Metode

### Expediții (AWB)

```go
// Creare AWB
awb, err := client.CreateAwb(ctx, &dragonstar.AwbCreateRequest{ /* ... */ })

// Creare AWB în numele unei filiale (pcId)
awb, err := client.CreateAwbForBranch(ctx, 454, &dragonstar.AwbCreateRequest{ /* ... */ })

// Estimare cost (nu creează nimic)
q, err := client.Quote(ctx, &dragonstar.QuoteRequest{
    ShipmentType:    dragonstar.ShipmentParcel,
    Weight:          dragonstar.Float64(5),
    RecipientCounty: "Cluj",
    RecipientCity:   "Cluj-Napoca",
})
fmt.Println(q.NetAmount, q.VatAmount, q.TotalAmount)

// Estimare pentru o filială
q, err := client.QuoteForBranch(ctx, 454, &dragonstar.QuoteRequest{ /* ... */ })

// Urmărire (events=true include istoricul, cel mai nou primul)
tr, err := client.Track(ctx, 94348148, true)
fmt.Println(dragonstar.StatusLabel(tr.Status.StatusCode), dragonstar.IsFinal(tr.Status.StatusCode))

// Urmărire în masă: peste 100 AWB-uri se împart automat în loturi de 100
bulk, err := client.TrackMany(ctx, []int64{94348148, 94348149}, false)
fmt.Println(bulk.Count, bulk.NotFound)

// Retururi legate de un AWB
legs, err := client.GetReturns(ctx, 94348148)

// Ștergere (doar înainte de preluare / borderou / facturare)
err := client.DeleteAwb(ctx, 94348148)

// Etichetă PDF: "A4", "A6" sau "" (formatul din cont)
pdf, err := client.GetLabel(ctx, 94348148, "A6")
_ = os.WriteFile("eticheta.pdf", pdf, 0o644)

// Etichetă ZPL pentru coletul nr. 1 (100x150 mm, 203 DPI)
zpl, err := client.GetLabelZpl(ctx, 94348148, 1)
```

### Ridicări de la sediu

```go
p, err := client.CreatePickup(ctx, &dragonstar.PickupRequest{
    PickupDate:        "2026-10-13",
    PickupWindowStart: 10,
    PickupWindowEnd:   16,
    Parcels:           dragonstar.Int(3),
    Weight:            12,
})
fmt.Println(p.PickupID)

// Pentru o filială (PickupID poate fi 0 dacă API-ul nu întoarce corp)
p, err := client.CreatePickupForBranch(ctx, 454, &dragonstar.PickupRequest{ /* ... */ })
```

### Borderouri

```go
m, err := client.CreateManifest(ctx)
if m.ManifestID == nil {
    fmt.Println("Nu există expediții de închis")
} else {
    pdf, err := client.GetManifestPdf(ctx, *m.ManifestID)
    _ = os.WriteFile("borderou.pdf", pdf, 0o644)
}
```

### Nomenclatoare

```go
counties, err := client.GetCounties(ctx)            // map[string]string: "CJ" -> "Cluj"
locs, err := client.GetLocalities(ctx, "Cluj")      // []string
km, err := client.GetLocalitiesWithKm(ctx, "Cluj")  // []LocalityKm{{Name: "Cluj-Napoca", Km: 450}, ...}
br, err := client.GetBranches(ctx)                  // br.MasterID, br.Data[i].BranchID ...
```

Numele județului se codifică automat în URL (diacritice, spații).

### Statusuri

| Constantă | Cod | Etichetă |
|---|---|---|
| `StatusGenerated` | 1 | Generat |
| `StatusException` | 2 | Exceptie |
| `StatusDelivered` | 3 | Livrat |
| `StatusNotified` | 4 | Avizat |
| `StatusPickedUp` | 5 | Preluat |
| `StatusInTransit` | 6 | In tranzit |
| `StatusReturned` | 7 | Returnat |
| `StatusOutForDelivery` | 18 | Spre livrare |
| `StatusRedirected` | 19 | Redirectionat |
| `StatusAtDestinationHub` | 21 | In centru destinatie |

`StatusLabel(code)` întoarce eticheta în română (`""` pentru cod necunoscut);
`IsFinal(code)` este `true` pentru Livrat (3) și Returnat (7).

## Erori

Orice răspuns HTTP ≥ 400 devine un `*dragonstar.Error`:

```go
_, err := client.CreateAwb(ctx, req)

var e *dragonstar.Error
if errors.As(err, &e) {
    fmt.Println(e.Status, e.Code, e.Title, e.Detail, e.Type)
    for field, msgs := range e.Errors { // doar la validation_failed
        fmt.Println(field, msgs)
    }
    fmt.Println(e.RetryAfter) // din antetul Retry-After, dacă există
}

if dragonstar.IsCode(err, "awb_not_deletable") {
    // AWB-ul a fost deja preluat / pus pe borderou / facturat
}
```

- `Error()` întoarce `Detail`, altfel `Title`, altfel `"HTTP <status>"`.
- Erorile de rețea / timeout devin `*Error` cu `Status == 0` și
  `Code == "network_error"`; cauza originală e disponibilă prin `errors.Unwrap` / `errors.Is`.
- Argumentele invalide detectate local (format etichetă `"a4"`, colet `0`, cerere `nil`)
  întorc o eroare care împachetează `dragonstar.ErrInvalidArgument`, fără să se trimită cererea.

## Reîncercări automate

- **429** `rate_limit_exceeded` — se reîncearcă pentru orice metodă (cererea a fost refuzată,
  deci nu s-a creat nimic), după `Retry-After` secunde (implicit 1).
- **503** și erorile de rețea — se reîncearcă **doar** pentru `GET` și `DELETE`.
  Un `POST` (AWB, ridicare, borderou) **nu** se reîncearcă, ca să nu apară AWB-uri duble.
- `temporarily_banned` nu se reîncearcă.
- Așteptarea maximă per reîncercare: 60 s. Numărul de reîncercări: `WithMaxRetries` (implicit 2).

## Teste

```bash
go test ./...
```

Testele folosesc un server HTTP local (`httptest`) și nu apelează API-ul real.

---

## English

Go client for the **Dragon Star Curier API v1**. Standard library only, Go 1.21+, MIT.

```bash
go get github.com/softprim/dragonstar-curier-sdk/go
```

```go
client := dragonstar.NewClient(os.Getenv("DRAGONSTAR_API_KEY"))
awb, err := client.CreateAwb(ctx, &dragonstar.AwbCreateRequest{
    ShipmentType: dragonstar.ShipmentEnvelope,
    RecipientName: "Ion Popescu", RecipientAddress: "Str. Exemplu 1",
    RecipientCounty: "Cluj", RecipientCity: "Cluj-Napoca",
})
```

- Methods: `CreateAwb`, `CreateAwbForBranch`, `Quote`, `QuoteForBranch`, `Track`, `TrackMany`
  (auto-batched by 100), `GetReturns`, `DeleteAwb`, `GetLabel` (`"A4"`, `"A6"` or `""`),
  `GetLabelZpl`, `CreatePickup`, `CreatePickupForBranch`, `CreateManifest`, `GetManifestPdf`,
  `GetCounties`, `GetLocalities`, `GetLocalitiesWithKm`, `GetBranches`. All take a `context.Context` first.
- Options: `WithBaseURL`, `WithTimeout` (default 30s), `WithMaxRetries` (default 2),
  `WithUserAgent`, `WithHTTPClient`.
- Errors: every status ≥ 400 is a `*dragonstar.Error` (`Status`, `Code`, `Title`, `Detail`,
  `Type`, `Errors`, `RetryAfter`); use `errors.As` or `dragonstar.IsCode(err, "validation_failed")`.
  Network failures are `*Error` with `Status 0`, `Code "network_error"`.
- Retries: 429 (except `temporarily_banned`) for every method; 503 and network errors only for
  `GET`/`DELETE` — a `POST` is never retried on 503, to avoid duplicate AWBs. Waits honour
  `Retry-After`, capped at 60 s.

---

Module gata făcute pentru Shopify, WooCommerce, OpenCart, PrestaShop sau alte platforme, integrări personalizate și actualizări: office@softprim.ro
