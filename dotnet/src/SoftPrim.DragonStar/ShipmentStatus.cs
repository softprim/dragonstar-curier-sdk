namespace SoftPrim.DragonStar
{
    /// <summary>Codurile de status ale unei expediții. / Shipment status codes.</summary>
    public static class ShipmentStatus
    {
        public const int Generated = 1;
        public const int Exception = 2;
        public const int Delivered = 3;
        public const int Notified = 4;
        public const int PickedUp = 5;
        public const int InTransit = 6;
        public const int Returned = 7;
        public const int OutForDelivery = 18;
        public const int Redirected = 19;
        public const int AtDestinationHub = 21;

        /// <summary>Eticheta în română pentru un cod de status (ex. 3 → "Livrat").</summary>
        public static string Label(int code)
        {
            switch (code)
            {
                case Generated: return "Generat";
                case Exception: return "Exceptie";
                case Delivered: return "Livrat";
                case Notified: return "Avizat";
                case PickedUp: return "Preluat";
                case InTransit: return "In tranzit";
                case Returned: return "Returnat";
                case OutForDelivery: return "Spre livrare";
                case Redirected: return "Redirectionat";
                case AtDestinationHub: return "In centru destinatie";
                default: return "Necunoscut (" + code + ")";
            }
        }

        /// <summary>True pentru statusurile finale: 3 (Livrat) și 7 (Returnat).</summary>
        public static bool IsFinal(int code) => code == Delivered || code == Returned;
    }

    /// <summary>Tipul expediției. / Shipment type.</summary>
    public static class ShipmentType
    {
        public const string Envelope = "envelope";
        public const string Parcel = "parcel";
        public const string Pallet = "pallet";
    }

    /// <summary>Modul de încasare a rambursului. / COD payment type.</summary>
    public static class CodPaymentType
    {
        public const string Cash = "cash";
        public const string BankOrder = "bankOrder";
        public const string Cheque = "cheque";
        public const string Account = "account";
    }

    /// <summary>Formatul etichetei PDF (exact cu majusculă).</summary>
    public static class LabelFormat
    {
        public const string A4 = "A4";
        public const string A6 = "A6";
    }
}
