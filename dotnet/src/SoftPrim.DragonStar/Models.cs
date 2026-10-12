using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Serialization;

namespace SoftPrim.DragonStar
{
    // ---------------------------------------------------------------- requests

    /// <summary>
    /// Cererea de cotație (<c>POST /v1/awb/quote</c>). Câmpurile null nu se trimit.
    /// Obligatorii: ShipmentType, RecipientCounty, RecipientCity.
    /// </summary>
    public class QuoteRequest
    {
        /// <summary><c>envelope</c>, <c>parcel</c> sau <c>pallet</c> (vezi clasa <c>ShipmentType</c>).</summary>
        public string ShipmentType { get; set; } = string.Empty;

        /// <summary>Greutatea în kg. Obligatorie pentru colet și palet.</summary>
        public decimal? Weight { get; set; }

        /// <summary>Numele județului (valoarea din <c>GetCountiesAsync</c>, ex. "Cluj"), nu codul.</summary>
        public string RecipientCounty { get; set; } = string.Empty;

        /// <summary>Localitatea destinatarului (din <c>GetLocalitiesAsync</c>).</summary>
        public string RecipientCity { get; set; } = string.Empty;

        /// <summary>Lungime în cm. Dacă trimiți una dintre dimensiuni, toate trei devin obligatorii.</summary>
        public decimal? Length { get; set; }

        /// <summary>Lățime în cm.</summary>
        public decimal? Width { get; set; }

        /// <summary>Înălțime în cm.</summary>
        public decimal? Height { get; set; }

        /// <summary>Suma ramburs.</summary>
        public decimal? CodAmount { get; set; }

        /// <summary>Vezi clasa <c>CodPaymentType</c>. Obligatoriu când CodAmount &gt; 0.</summary>
        public string? CodPaymentType { get; set; }

        /// <summary>Valoarea declarată (asigurare).</summary>
        public decimal? InsuredValue { get; set; }

        public bool? ReturnAwb { get; set; }
        public bool? ReturnDocuments { get; set; }
        public bool? ReturnPackaging { get; set; }
        public bool? ReturnParcel { get; set; }

        /// <summary>Necesită RecipientPhone mobil.</summary>
        public bool? SmsNotification { get; set; }

        public bool? OpenOnDelivery { get; set; }
        public bool? SaturdayDelivery { get; set; }
        public bool? DeliverToBranch { get; set; }
        public bool? RecipientPays { get; set; }
        public bool? ReverseShipment { get; set; }

        /// <summary>Observații tipărite pe etichetă.</summary>
        public string? Notes { get; set; }

        public string? DocumentDetails { get; set; }
    }

    /// <summary>
    /// Crearea unui AWB (<c>POST /v1/awb</c>): toate câmpurile cotației plus datele destinatarului.
    /// Obligatorii: ShipmentType, RecipientName, RecipientAddress, RecipientCounty, RecipientCity.
    /// </summary>
    public class AwbCreateRequest : QuoteRequest
    {
        /// <summary>Numărul de colete (1–99), obligatoriu pentru colet.</summary>
        public int? Pieces { get; set; }

        public string RecipientName { get; set; } = string.Empty;
        public string RecipientAddress { get; set; } = string.Empty;
        public string? RecipientContact { get; set; }

        /// <summary>Obligatoriu (mobil 07XXXXXXXX) doar când SmsNotification este true.</summary>
        public string? RecipientPhone { get; set; }

        public string? RecipientEmail { get; set; }
        public string? RecipientPostalCode { get; set; }
    }

    /// <summary>Comanda de ridicare (<c>POST /v1/pickups</c>).</summary>
    public class PickupRequest
    {
        /// <summary>Data ridicării (trimisă ca <c>yyyy-MM-dd</c>). Nu poate fi în trecut.</summary>
        [JsonConverter(typeof(DateOnlyJsonConverter))]
        public DateTime PickupDate { get; set; }

        /// <summary>Ora de început a intervalului (9–16).</summary>
        public int PickupWindowStart { get; set; }

        /// <summary>Ora de sfârșit a intervalului (10–17).</summary>
        public int PickupWindowEnd { get; set; }

        public string? PickupLocation { get; set; }
        public string? PickupAddress { get; set; }
        public string? PickupCounty { get; set; }
        public string? PickupCity { get; set; }
        public string? PickupContact { get; set; }
        public string? PickupPhone { get; set; }
        public string? PickupEmail { get; set; }

        /// <summary>Obligatoriu dacă Pallets lipsește sau e 0.</summary>
        public int? Parcels { get; set; }

        /// <summary>Obligatoriu dacă Parcels lipsește sau e 0.</summary>
        public int? Pallets { get; set; }

        /// <summary>Greutatea totală în kg (minim 1).</summary>
        public decimal Weight { get; set; }

        /// <summary>Volumul total, în metri cubi.</summary>
        public decimal? Volume { get; set; }

        public string? Notes { get; set; }
    }

    // --------------------------------------------------------------- responses

    /// <summary>Rezultatul creării unui AWB.</summary>
    public class AwbCreated
    {
        public long Awb { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal? Weight { get; set; }
        public int? Pieces { get; set; }
        public decimal? CodAmount { get; set; }

        /// <summary>Tipul de ramburs înregistrat efectiv (poate diferi de cel trimis); null fără ramburs.</summary>
        public string? CodPaymentType { get; set; }
    }

    /// <summary>Cotația (costul estimat).</summary>
    public class Quote
    {
        public decimal BaseRate { get; set; }
        public decimal DistanceCost { get; set; }
        public decimal WeightCost { get; set; }
        public decimal InsuranceCost { get; set; }
        public bool IsListRate { get; set; }
        public decimal NetAmount { get; set; }
        public decimal VatAmount { get; set; }
        public decimal TotalAmount { get; set; }
    }

    /// <summary>Statusul curent al unei expediții.</summary>
    public class TrackingStatus
    {
        /// <summary>Cod stabil, vezi clasa <c>ShipmentStatus</c>.</summary>
        public int? StatusCode { get; set; }

        public string? Description { get; set; }
        public string? DescriptionRo { get; set; }

        [JsonConverter(typeof(LenientDateTimeOffsetConverter))]
        public DateTimeOffset? Timestamp { get; set; }
    }

    /// <summary>Locația unui eveniment.</summary>
    public class TrackingLocation
    {
        public string? Name { get; set; }
        public string? Code { get; set; }
    }

    /// <summary>Un eveniment din istoricul expediției.</summary>
    public class TrackingEvent
    {
        public int? StatusCode { get; set; }
        public string? EventCode { get; set; }
        public string? Description { get; set; }

        /// <summary>Marchează o problemă de livrare.</summary>
        public bool IsException { get; set; }

        [JsonConverter(typeof(LenientDateTimeOffsetConverter))]
        public DateTimeOffset? Timestamp { get; set; }

        public TrackingLocation? Location { get; set; }
    }

    /// <summary>Starea unei expediții.</summary>
    public class Tracking
    {
        public TrackingStatus? Status { get; set; }

        /// <summary>Istoricul, cel mai recent primul. Null când s-a cerut fără evenimente.</summary>
        public List<TrackingEvent>? Events { get; set; }
    }

    /// <summary>O intrare din urmărirea în masă.</summary>
    public class BulkTrackingItem : Tracking
    {
        public long Awb { get; set; }
    }

    /// <summary>Rezultatul urmăririi în masă.</summary>
    public class BulkTracking
    {
        public int Count { get; set; }
        public List<BulkTrackingItem> Data { get; set; } = new List<BulkTrackingItem>();

        /// <summary>AWB-urile care nu au fost găsite.</summary>
        public List<long> NotFound { get; set; } = new List<long>();
    }

    /// <summary>O expediție de retur legată de un AWB.</summary>
    public class ReturnLeg
    {
        public long Awb { get; set; }
        public string? Type { get; set; }
        public int? TypeCode { get; set; }
        public TrackingStatus? Status { get; set; }
        public TrackingEvent? LastEvent { get; set; }
    }

    /// <summary>Lista retururilor unui AWB.</summary>
    public class ReturnList
    {
        public int Count { get; set; }
        public List<ReturnLeg> Data { get; set; } = new List<ReturnLeg>();
    }

    /// <summary>Rezultatul unei comenzi de ridicare.</summary>
    public class PickupCreated
    {
        public long PickupId { get; set; }
    }

    /// <summary>Rezultatul închiderii borderoului.</summary>
    public class ManifestResult
    {
        /// <summary>Null când nu au existat expediții de închis.</summary>
        public long? ManifestId { get; set; }

        public int ShipmentCount { get; set; }
    }

    /// <summary>O sucursală (punct de lucru).</summary>
    public class Branch
    {
        public long BranchId { get; set; }
        public string? Name { get; set; }
        public string? County { get; set; }
        public string? City { get; set; }
        public string? Address { get; set; }
    }

    /// <summary>Lista sucursalelor contului.</summary>
    public class BranchList
    {
        public long MasterId { get; set; }
        public int Count { get; set; }
        public List<Branch> Data { get; set; } = new List<Branch>();
    }

    /// <summary>O localitate cu distanța în km.</summary>
    public sealed class LocalityKm
    {
        public LocalityKm(string name, decimal? km)
        {
            Name = name;
            Km = km;
        }

        public string Name { get; }

        /// <summary>Kilometrii; null dacă valoarea nu a putut fi citită.</summary>
        public decimal? Km { get; }

        /// <summary>Parsează <c>"Nume:km"</c>, separând la ultimul <c>:</c>.</summary>
        public static LocalityKm Parse(string raw)
        {
            if (raw == null) throw new ArgumentNullException(nameof(raw));
            int i = raw.LastIndexOf(':');
            if (i < 0) return new LocalityKm(raw.Trim(), null);
            var name = raw.Substring(0, i).Trim();
            var kmText = raw.Substring(i + 1).Trim();
            decimal? km = decimal.TryParse(kmText, NumberStyles.Number, CultureInfo.InvariantCulture, out var v)
                ? v
                : (decimal?)null;
            return new LocalityKm(name, km);
        }

        public override string ToString() => Name + ":" + (Km.HasValue ? Km.Value.ToString(CultureInfo.InvariantCulture) : "");
    }

    // ------------------------------------------------- internal response wraps

    internal sealed class DataEnvelope<T>
    {
        public int Count { get; set; }
        public T? Data { get; set; }
    }

    internal sealed class TrackManyBody
    {
        public List<long> Awbs { get; set; } = new List<long>();
    }

    internal sealed class ProblemDocument
    {
        public string? Type { get; set; }
        public string? Title { get; set; }
        public int? Status { get; set; }
        public string? Code { get; set; }
        public string? Detail { get; set; }
        public Dictionary<string, string[]>? Errors { get; set; }
    }
}
