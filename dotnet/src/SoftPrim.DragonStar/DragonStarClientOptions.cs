using System;

namespace SoftPrim.DragonStar
{
    /// <summary>Opțiunile clientului. / Client options.</summary>
    public sealed class DragonStarClientOptions
    {
        /// <summary>Adresa de bază a API-ului, fără slash final.</summary>
        public string BaseUrl { get; set; } = "https://api.curierdragonstar.ro";

        /// <summary>Timeout per cerere (implicit 30 s).</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>Numărul maxim de reîncercări automate (implicit 2; 0 le dezactivează).</summary>
        public int MaxRetries { get; set; } = 2;

        /// <summary>Antetul User-Agent.</summary>
        public string UserAgent { get; set; } = DragonStarClient.DefaultUserAgent;
    }
}
