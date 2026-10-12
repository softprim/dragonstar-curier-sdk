// Exemplu minim: listează județele și urmărește un AWB.
// Rulare:  set DRAGONSTAR_API_KEY=cheia_ta   (PowerShell: $env:DRAGONSTAR_API_KEY="cheia_ta")
//          dotnet run --project examples/QuickStart -- 94348148
using SoftPrim.DragonStar;

var apiKey = Environment.GetEnvironmentVariable("DRAGONSTAR_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("Setați variabila de mediu DRAGONSTAR_API_KEY.");
    return 1;
}

using var client = new DragonStarClient(apiKey);

try
{
    var counties = await client.GetCountiesAsync();
    Console.WriteLine($"{counties.Count} județe, ex. CJ = {(counties.TryGetValue("CJ", out var cj) ? cj : "?")}");

    if (args.Length > 0 && long.TryParse(args[0], out var awb))
    {
        var tracking = await client.TrackAsync(awb);
        var code = tracking.Status?.StatusCode ?? 0;
        Console.WriteLine($"AWB {awb}: {ShipmentStatus.Label(code)} (final: {ShipmentStatus.IsFinal(code)})");
        foreach (var e in tracking.Events ?? new List<TrackingEvent>())
            Console.WriteLine($"  {e.Timestamp:yyyy-MM-dd HH:mm}  {e.Location?.Name,-12} {e.Description}");
    }

    return 0;
}
catch (DragonStarException ex)
{
    Console.Error.WriteLine($"Eroare API {ex.Status} {ex.Code}: {ex.Message}");
    foreach (var field in ex.Errors)
        Console.Error.WriteLine($"  {field.Key}: {string.Join("; ", field.Value)}");
    return 2;
}
