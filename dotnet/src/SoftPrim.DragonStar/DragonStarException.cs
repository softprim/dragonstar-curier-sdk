using System;
using System.Collections.Generic;

namespace SoftPrim.DragonStar
{
    /// <summary>
    /// Eroare întoarsă de API (orice răspuns HTTP ≥ 400) sau eroare de rețea
    /// (<see cref="Status"/> = 0, <see cref="Code"/> = <c>network_error</c>).
    /// </summary>
    public class DragonStarException : Exception
    {
        private static readonly IReadOnlyDictionary<string, string[]> NoErrors =
            new Dictionary<string, string[]>();

        public DragonStarException(
            int status,
            string? code,
            string? title,
            string? detail,
            string? type,
            IReadOnlyDictionary<string, string[]>? errors = null,
            TimeSpan? retryAfter = null,
            Exception? innerException = null)
            : base(BuildMessage(status, title, detail), innerException)
        {
            Status = status;
            Code = code;
            Title = title;
            Detail = detail;
            Type = type;
            Errors = errors ?? NoErrors;
            RetryAfter = retryAfter;
        }

        /// <summary>Codul HTTP (0 pentru erori de rețea / timeout).</summary>
        public int Status { get; }

        /// <summary>Codul stabil din problem+json, ex. <c>validation_failed</c>; null dacă lipsește.</summary>
        public string? Code { get; }

        public string? Title { get; }

        public string? Detail { get; }

        public string? Type { get; }

        /// <summary>Câmp → mesaje (doar la <c>validation_failed</c>); altfel gol.</summary>
        public IReadOnlyDictionary<string, string[]> Errors { get; }

        /// <summary>Valoarea antetului Retry-After, dacă există.</summary>
        public TimeSpan? RetryAfter { get; }

        private static string BuildMessage(int status, string? title, string? detail)
        {
            if (!string.IsNullOrEmpty(detail)) return detail!;
            if (!string.IsNullOrEmpty(title)) return title!;
            return "HTTP " + status;
        }
    }
}
