# SoftPrim.DragonStar — SDK .NET pentru API-ul Dragon Star Curier v1

Client C# pentru [API-ul Dragon Star Curier v1](../docs/openapi.v1.json): AWB-uri, cotații,
urmărire, etichete PDF/ZPL, ridicări, borderouri, nomenclatoare.

- Ținte: **netstandard2.0** (merge din .NET Framework 4.6.1+, util pentru ERP-uri mai vechi) și **net8.0**.
- O singură dependență: `System.Text.Json` (doar pe netstandard2.0; pe net8.0 e inclus în runtime).
- Licență MIT, © SoftPrim Technology SRL.

## Instalare

Pachetul NuGet `SoftPrim.DragonStar` urmează să fie publicat. Până atunci:

```bash
git clone https://github.com/softprim/dragonstar-curier-sdk.git
cd dragonstar-curier-sdk/dotnet
dotnet build -c Release
# sau pachet local:
dotnet pack src/SoftPrim.DragonStar -c Release -o ./nupkg
```

Referință directă din proiectul tău:

```xml
<ItemGroup>
  <ProjectReference Include="..\dragonstar-curier-sdk\dotnet\src\SoftPrim.DragonStar\SoftPrim.DragonStar.csproj" />
</ItemGroup>
```

Când pachetul apare pe NuGet: `dotnet add package SoftPrim.DragonStar`.

## Pornire rapidă

```csharp
using SoftPrim.DragonStar;

using var client = new DragonStarClient(Environment.GetEnvironmentVariable("DRAGONSTAR_API_KEY")!);

var awb = await client.CreateAwbAsync(new AwbCreateRequest
{
    ShipmentType     = ShipmentType.Parcel,
    Weight           = 5,
    Pieces           = 1,
    RecipientName    = "Ion Popescu",
    RecipientAddress = "Str. Florilor 1",
    RecipientCounty  = "Cluj",          // numele județului, nu codul "CJ"
    RecipientCity    = "Cluj-Napoca",
    RecipientPhone   = "0722123456",
    CodAmount        = 150,
    CodPaymentType   = CodPaymentType.Cash,
});

Console.WriteLine($"AWB {awb.Awb}, total {awb.TotalAmount} lei");
byte[] pdf = await client.GetLabelAsync(awb.Awb, LabelFormat.A6);
File.WriteAllBytes($"awb-{awb.Awb}.pdf", pdf);
```

Un exemplu complet de consolă este în [`examples/QuickStart`](examples/QuickStart/Program.cs):

```bash
$env:DRAGONSTAR_API_KEY="cheia_ta"      # PowerShell
dotnet run --project examples/QuickStart -- 94348148
```

## Configurare

```csharp
var client = new DragonStarClient(apiKey, new DragonStarClientOptions
{
    BaseUrl    = "https://api.curierdragonstar.ro",   // implicit
    Timeout    = TimeSpan.FromSeconds(30),            // implicit, per cerere
    MaxRetries = 2,                                   // implicit; 0 = fără reîncercări
    UserAgent  = "softprim-dragonstar-dotnet/1.0.0",  // implicit
});

// Cu un HttpClient extern (ex. IHttpClientFactory în ASP.NET Core).
// Clientul extern nu este eliberat de Dispose().
var client2 = new DragonStarClient(apiKey, httpClient, options);
```

`DragonStarClient` este thread-safe: creați o singură instanță și refolosiți-o.
Toate metodele sunt asincrone și acceptă un `CancellationToken` opțional.

## Metode

### AWB

```csharp
// Creare AWB (POST /v1/awb) și creare pentru o sucursală (POST /v1/awb/branch)
AwbCreated a  = await client.CreateAwbAsync(request);
AwbCreated a2 = await client.CreateAwbForBranchAsync(pcId: 454, request);

// Cotație (POST /v1/awb/quote, /v1/awb/quote/branch)
Quote q = await client.QuoteAsync(new QuoteRequest
{
    ShipmentType = ShipmentType.Envelope, RecipientCounty = "Cluj", RecipientCity = "Dej",
});
Quote q2 = await client.QuoteForBranchAsync(454, quoteRequest);
Console.WriteLine($"{q.NetAmount} + TVA {q.VatAmount} = {q.TotalAmount}");

// Urmărire (GET /v1/awb/{awb}?events=true|false)
Tracking t = await client.TrackAsync(94348148);                 // cu istoric
Tracking s = await client.TrackAsync(94348148, events: false);  // doar status
int code = t.Status?.StatusCode ?? 0;
Console.WriteLine(ShipmentStatus.Label(code));                  // "Livrat"
bool done = ShipmentStatus.IsFinal(code);                       // true pt. Livrat / Returnat

// Urmărire în masă (POST /v1/awb/tracking). Peste 100 de AWB-uri,
// SDK-ul împarte automat în loturi de 100 și unește rezultatele.
BulkTracking bulk = await client.TrackManyAsync(new long[] { 94348148, 94348149 });
foreach (var item in bulk.Data) Console.WriteLine($"{item.Awb}: {item.Status?.DescriptionRo}");
Console.WriteLine($"Negăsite: {string.Join(", ", bulk.NotFound)}");

// Retururi (GET /v1/awb/{awb}/return)
ReturnList r = await client.GetReturnsAsync(94348148);

// Ștergere (DELETE /v1/awb/{awb})
await client.DeleteAwbAsync(94348148);

// Etichetă PDF (GET /v1/awb/{awb}/label[/A4|A6]); null = formatul setat pe cont.
// Formatul trebuie să fie exact "A4" sau "A6" — "a4" aruncă ArgumentException înainte de cerere.
byte[] label = await client.GetLabelAsync(94348148);
byte[] a4    = await client.GetLabelAsync(94348148, LabelFormat.A4);

// Etichetă ZPL pentru coletul n (de la 1) (GET /v1/awb/{awb}/label/zpl/{n})
string zpl = await client.GetLabelZplAsync(94348148, parcel: 2);
```

### Ridicări și borderouri

```csharp
PickupCreated p = await client.CreatePickupAsync(new PickupRequest
{
    PickupDate = DateTime.Today.AddDays(1),   // trimis ca yyyy-MM-dd
    PickupWindowStart = 10,
    PickupWindowEnd   = 14,
    Parcels = 3,
    Weight  = 12.5m,
});
PickupCreated? pb = await client.CreatePickupForBranchAsync(454, pickupRequest); // null dacă API-ul nu întoarce corp

ManifestResult m = await client.CreateManifestAsync();   // POST /v1/manifests
if (m.ManifestId is long id)
    File.WriteAllBytes($"borderou-{id}.pdf", await client.GetManifestPdfAsync(id));
else
    Console.WriteLine("Nu există expediții de închis.");
```

### Nomenclatoare

```csharp
IReadOnlyDictionary<string, string> counties = await client.GetCountiesAsync(); // "CJ" → "Cluj"
IReadOnlyList<string> localities = await client.GetLocalitiesAsync("Bistrița-Năsăud"); // codificat automat în URL
IReadOnlyList<LocalityKm> km = await client.GetLocalitiesWithKmAsync("Cluj");
Console.WriteLine($"{km[0].Name}: {km[0].Km} km");      // "Cluj-Napoca: 450 km"

BranchList branches = await client.GetBranchesAsync();
foreach (var b in branches.Data) Console.WriteLine($"{b.BranchId} {b.Name} — {b.City}");
```

### Constante

| Clasă | Valori |
|---|---|
| `ShipmentType` | `Envelope`, `Parcel`, `Pallet` |
| `CodPaymentType` | `Cash`, `BankOrder`, `Cheque`, `Account` |
| `LabelFormat` | `A4`, `A6` |
| `ShipmentStatus` | `Generated` 1, `Exception` 2, `Delivered` 3, `Notified` 4, `PickedUp` 5, `InTransit` 6, `Returned` 7, `OutForDelivery` 18, `Redirected` 19, `AtDestinationHub` 21; `Label(int)`, `IsFinal(int)` |

Câmpurile `null` din cereri nu se trimit deloc în JSON.

## Erori

Orice răspuns HTTP ≥ 400 aruncă `DragonStarException`:

```csharp
try
{
    await client.CreateAwbAsync(request);
}
catch (DragonStarException ex) when (ex.Code == "validation_failed")
{
    foreach (var (field, messages) in ex.Errors)
        Console.WriteLine($"{field}: {string.Join("; ", messages)}");
}
catch (DragonStarException ex)
{
    // ex.Status (cod HTTP; 0 = eroare de rețea/timeout), ex.Code (ex. "awb_not_found"),
    // ex.Title, ex.Detail, ex.Type, ex.RetryAfter (TimeSpan?)
    Console.WriteLine($"{ex.Status} {ex.Code}: {ex.Message}");
}
```

`Message` = `detail` → `title` → `"HTTP <status>"`. Erorile de rețea și timeout-urile devin
`DragonStarException` cu `Status = 0` și `Code = "network_error"` (excepția originală în `InnerException`).
Anularea prin `CancellationToken`-ul vostru aruncă `OperationCanceledException`, ca de obicei.

## Reîncercări

- **429 `rate_limit_exceeded`** — se reîncearcă pentru orice metodă (cererea a fost refuzată,
  nu s-a creat nimic), după `Retry-After` secunde (implicit 1).
- **503 și erori de rețea / timeout** — se reîncearcă **doar** la `GET` și `DELETE`.
  `POST` (AWB, ridicare, borderou) **nu** se reîncearcă automat, ca să nu apară AWB-uri duble.
- **429 `temporarily_banned`** — nu se reîncearcă (blocarea durează minute).
- Așteptarea per reîncercare este plafonată la 60 s; numărul maxim e `MaxRetries` (implicit 2).

## Teste

```bash
dotnet test DragonStar.sln
```

Testele nu apelează API-ul real: folosesc un `HttpMessageHandler` fals cu răspunsuri scriptate.

---

## English

.NET client for the Dragon Star Curier API v1, targeting **netstandard2.0** (.NET Framework 4.6.1+)
and **net8.0**; the only dependency is `System.Text.Json`.

**Install.** The NuGet package `SoftPrim.DragonStar` is coming soon. Until then, build from source
(`dotnet build -c Release` in `dotnet/`) or add a `ProjectReference` to
`dotnet/src/SoftPrim.DragonStar/SoftPrim.DragonStar.csproj`.

**Quick start.**

```csharp
using var client = new DragonStarClient(Environment.GetEnvironmentVariable("DRAGONSTAR_API_KEY")!);
var tracking = await client.TrackAsync(94348148);
Console.WriteLine(ShipmentStatus.Label(tracking.Status?.StatusCode ?? 0));
```

**Methods.** `CreateAwbAsync`, `CreateAwbForBranchAsync`, `QuoteAsync`, `QuoteForBranchAsync`,
`TrackAsync`, `TrackManyAsync` (auto-batches by 100), `GetReturnsAsync`, `DeleteAwbAsync`,
`GetLabelAsync` (PDF bytes; format `"A4"`/`"A6"` only), `GetLabelZplAsync`, `CreatePickupAsync`,
`CreatePickupForBranchAsync`, `CreateManifestAsync` (`ManifestId` is null when there is nothing to close),
`GetManifestPdfAsync`, `GetCountiesAsync`, `GetLocalitiesAsync`, `GetLocalitiesWithKmAsync`, `GetBranchesAsync`.
All are async and accept a `CancellationToken`.

**Errors.** Any HTTP status ≥ 400 throws `DragonStarException` with `Status`, `Code`, `Title`,
`Detail`, `Type`, `Errors` (field → messages on `validation_failed`) and `RetryAfter`. Network errors
and timeouts surface as `DragonStarException` with `Status = 0`, `Code = "network_error"`.

**Retries.** 429 `rate_limit_exceeded` is retried for every method, honouring `Retry-After`
(default 1 s). 503 and network errors are retried for `GET`/`DELETE` only — never for `POST`, so no
duplicate AWBs. `temporarily_banned` is never retried. Each wait is capped at 60 s; `MaxRetries`
defaults to 2.

---

Module gata făcute pentru Shopify, WooCommerce, OpenCart, PrestaShop sau alte platforme, integrări personalizate și actualizări: office@softprim.ro
