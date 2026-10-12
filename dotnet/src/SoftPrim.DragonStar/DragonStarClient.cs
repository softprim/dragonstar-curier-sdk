using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace SoftPrim.DragonStar
{
    /// <summary>
    /// Client pentru API-ul Dragon Star Curier v1. / Client for the Dragon Star Curier API v1.
    /// Thread-safe; creați o singură instanță și refolosiți-o.
    /// </summary>
    public sealed class DragonStarClient : IDisposable
    {
        private static readonly string[] AcceptedMediaTypes = { "application/json", "application/problem+json", "application/pdf", "application/octet-stream" };

        /// <summary>Versiunea SDK-ului.</summary>
        public const string Version = "1.0.0";

        /// <summary>User-Agent implicit.</summary>
        public const string DefaultUserAgent = "softprim-dragonstar-dotnet/" + Version;

        private const int TrackManyBatchSize = 100;
        private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(60);

        private readonly HttpClient _http;
        private readonly bool _ownsHttpClient;
        private readonly string _apiKey;
        private readonly string _baseUrl;
        private readonly TimeSpan _timeout;
        private readonly int _maxRetries;
        private readonly string _userAgent;
        private bool _disposed;

        /// <summary>Creează un client cu propriul <see cref="HttpClient"/>.</summary>
        /// <param name="apiKey">Cheia API (trimisă ca <c>Authorization: Bearer</c>).</param>
        /// <param name="options">Opțiuni; null = valorile implicite.</param>
        public DragonStarClient(string apiKey, DragonStarClientOptions? options = null)
            : this(apiKey, CreateOwnedHttpClient(), options, ownsHttpClient: true)
        {
        }

        /// <summary>
        /// Creează un client peste un <see cref="HttpClient"/> extern (ex. din IHttpClientFactory).
        /// Clientul extern nu este eliberat la <see cref="Dispose"/>.
        /// </summary>
        public DragonStarClient(string apiKey, HttpClient httpClient, DragonStarClientOptions? options = null)
            : this(apiKey, httpClient ?? throw new ArgumentNullException(nameof(httpClient)), options, ownsHttpClient: false)
        {
        }

        private DragonStarClient(string apiKey, HttpClient httpClient, DragonStarClientOptions? options, bool ownsHttpClient)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("API key is required.", nameof(apiKey));

            options ??= new DragonStarClientOptions();
            if (options.MaxRetries < 0)
                throw new ArgumentOutOfRangeException(nameof(options), "MaxRetries must be >= 0.");
            if (options.Timeout <= TimeSpan.Zero && options.Timeout != System.Threading.Timeout.InfiniteTimeSpan)
                throw new ArgumentOutOfRangeException(nameof(options), "Timeout must be positive.");

            _apiKey = apiKey;
            _http = httpClient;
            _ownsHttpClient = ownsHttpClient;
            _baseUrl = (string.IsNullOrWhiteSpace(options.BaseUrl) ? "https://api.curierdragonstar.ro" : options.BaseUrl).TrimEnd('/');
            _timeout = options.Timeout;
            _maxRetries = options.MaxRetries;
            _userAgent = string.IsNullOrWhiteSpace(options.UserAgent) ? DefaultUserAgent : options.UserAgent;
        }

        private static HttpClient CreateOwnedHttpClient()
        {
            // Timeout-ul se aplică per cerere, prin CancellationToken.
            return new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        }

        // =================================================================== AWB

        /// <summary>Creează un AWB. <c>POST /v1/awb</c>. Nu se reîncearcă la 503 (evită AWB-uri duble).</summary>
        public Task<AwbCreated> CreateAwbAsync(AwbCreateRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return SendJsonAsync<AwbCreated>(HttpMethod.Post, "/v1/awb", ToJson(request), cancellationToken);
        }

        /// <summary>Creează un AWB în numele unei sucursale. <c>POST /v1/awb/branch</c>.</summary>
        public Task<AwbCreated> CreateAwbForBranchAsync(long pcId, AwbCreateRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return SendJsonAsync<AwbCreated>(HttpMethod.Post, "/v1/awb/branch", ToJson(request, pcId), cancellationToken);
        }

        /// <summary>Calculează costul estimat. <c>POST /v1/awb/quote</c>.</summary>
        public Task<Quote> QuoteAsync(QuoteRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return SendJsonAsync<Quote>(HttpMethod.Post, "/v1/awb/quote", ToJson(request), cancellationToken);
        }

        /// <summary>Cost estimat pentru o sucursală. <c>POST /v1/awb/quote/branch</c>.</summary>
        public Task<Quote> QuoteForBranchAsync(long pcId, QuoteRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return SendJsonAsync<Quote>(HttpMethod.Post, "/v1/awb/quote/branch", ToJson(request, pcId), cancellationToken);
        }

        /// <summary>Urmărește un AWB. <c>GET /v1/awb/{awb}?events=true|false</c>.</summary>
        public Task<Tracking> TrackAsync(long awb, bool events = true, CancellationToken cancellationToken = default)
        {
            return SendJsonAsync<Tracking>(HttpMethod.Get, "/v1/awb/" + Id(awb) + "?events=" + Bool(events), null, cancellationToken);
        }

        /// <summary>
        /// Urmărește mai multe AWB-uri. <c>POST /v1/awb/tracking</c>.
        /// Peste 100 de AWB-uri, lista se împarte automat în loturi de 100 și rezultatele se unesc.
        /// </summary>
        public async Task<BulkTracking> TrackManyAsync(IEnumerable<long> awbs, bool events = false, CancellationToken cancellationToken = default)
        {
            if (awbs == null) throw new ArgumentNullException(nameof(awbs));
            var all = awbs.ToList();
            var result = new BulkTracking();
            for (int offset = 0; offset < all.Count; offset += TrackManyBatchSize)
            {
                var body = new TrackManyBody { Awbs = all.GetRange(offset, Math.Min(TrackManyBatchSize, all.Count - offset)) };
                var json = JsonSerializer.Serialize(body, DragonStarJson.Options);
                var page = await SendJsonAsync<BulkTracking>(HttpMethod.Post, "/v1/awb/tracking?events=" + Bool(events), json, cancellationToken)
                    .ConfigureAwait(false);
                if (page.Data != null) result.Data.AddRange(page.Data);
                if (page.NotFound != null) result.NotFound.AddRange(page.NotFound);
            }

            result.Count = result.Data.Count;
            return result;
        }

        /// <summary>Retururile legate de un AWB. <c>GET /v1/awb/{awb}/return</c>.</summary>
        public Task<ReturnList> GetReturnsAsync(long awb, CancellationToken cancellationToken = default)
        {
            return SendJsonAsync<ReturnList>(HttpMethod.Get, "/v1/awb/" + Id(awb) + "/return", null, cancellationToken);
        }

        /// <summary>Șterge un AWB. <c>DELETE /v1/awb/{awb}</c>.</summary>
        public async Task DeleteAwbAsync(long awb, CancellationToken cancellationToken = default)
        {
            using (await SendAsync(HttpMethod.Delete, "/v1/awb/" + Id(awb), null, cancellationToken).ConfigureAwait(false))
            {
            }
        }

        /// <summary>
        /// Eticheta PDF. <c>GET /v1/awb/{awb}/label[/{A4|A6}]</c>.
        /// <paramref name="format"/> null = formatul setat pe cont; altfel exact "A4" sau "A6".
        /// </summary>
        public Task<byte[]> GetLabelAsync(long awb, string? format = null, CancellationToken cancellationToken = default)
        {
            if (format != null && format != LabelFormat.A4 && format != LabelFormat.A6)
                throw new ArgumentException("Label format must be exactly \"A4\" or \"A6\".", nameof(format));
            var path = "/v1/awb/" + Id(awb) + "/label" + (format == null ? "" : "/" + format);
            return SendBytesAsync(path, cancellationToken);
        }

        /// <summary>Eticheta ZPL pentru coletul <paramref name="parcel"/> (de la 1). <c>GET /v1/awb/{awb}/label/zpl/{nb}</c>.</summary>
        public async Task<string> GetLabelZplAsync(long awb, int parcel = 1, CancellationToken cancellationToken = default)
        {
            if (parcel < 1) throw new ArgumentOutOfRangeException(nameof(parcel), "Parcel number must be >= 1.");
            using (var response = await SendAsync(HttpMethod.Get, "/v1/awb/" + Id(awb) + "/label/zpl/" + parcel.ToString(CultureInfo.InvariantCulture), null, cancellationToken).ConfigureAwait(false))
            {
                var bytes = await ReadBytesAsync(response, cancellationToken).ConfigureAwait(false);
                return Encoding.UTF8.GetString(bytes);
            }
        }

        // ================================================================ pickups

        /// <summary>Comandă de ridicare. <c>POST /v1/pickups</c>.</summary>
        public Task<PickupCreated> CreatePickupAsync(PickupRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return SendJsonAsync<PickupCreated>(HttpMethod.Post, "/v1/pickups", ToJson(request), cancellationToken);
        }

        /// <summary>
        /// Comandă de ridicare pentru o sucursală. <c>POST /v1/pickups/branch</c>.
        /// Întoarce null când API-ul răspunde fără corp.
        /// </summary>
        public async Task<PickupCreated?> CreatePickupForBranchAsync(long pcId, PickupRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return await SendJsonOrNullAsync<PickupCreated>(HttpMethod.Post, "/v1/pickups/branch", ToJson(request, pcId), cancellationToken)
                .ConfigureAwait(false);
        }

        // ============================================================= manifests

        /// <summary>
        /// Închide borderoul zilei. <c>POST /v1/manifests</c>.
        /// <see cref="ManifestResult.ManifestId"/> este null când nu există expediții de închis.
        /// </summary>
        public async Task<ManifestResult> CreateManifestAsync(CancellationToken cancellationToken = default)
        {
            var result = await SendJsonOrNullAsync<ManifestResult>(HttpMethod.Post, "/v1/manifests", null, cancellationToken)
                .ConfigureAwait(false);
            return result ?? new ManifestResult { ManifestId = null, ShipmentCount = 0 };
        }

        /// <summary>PDF-ul borderoului. <c>GET /v1/manifests/{id}/pdf</c>.</summary>
        public Task<byte[]> GetManifestPdfAsync(long manifestId, CancellationToken cancellationToken = default)
        {
            return SendBytesAsync("/v1/manifests/" + Id(manifestId) + "/pdf", cancellationToken);
        }

        // ========================================================= nomenclatures

        /// <summary>Județele: cod → nume (ex. "CJ" → "Cluj"). <c>GET /v1/counties</c>.</summary>
        public async Task<IReadOnlyDictionary<string, string>> GetCountiesAsync(CancellationToken cancellationToken = default)
        {
            var env = await SendJsonAsync<DataEnvelope<Dictionary<string, string>>>(HttpMethod.Get, "/v1/counties", null, cancellationToken)
                .ConfigureAwait(false);
            return env.Data ?? new Dictionary<string, string>();
        }

        /// <summary>Localitățile unui județ (numele județului, ex. "Cluj"). <c>GET /v1/counties/{county}/localities</c>.</summary>
        public async Task<IReadOnlyList<string>> GetLocalitiesAsync(string county, CancellationToken cancellationToken = default)
        {
            var env = await SendJsonAsync<DataEnvelope<List<string>>>(HttpMethod.Get, "/v1/counties/" + County(county) + "/localities", null, cancellationToken)
                .ConfigureAwait(false);
            return env.Data ?? new List<string>();
        }

        /// <summary>Localitățile cu distanța în km. <c>GET /v1/counties/{county}/localities-km</c>.</summary>
        public async Task<IReadOnlyList<LocalityKm>> GetLocalitiesWithKmAsync(string county, CancellationToken cancellationToken = default)
        {
            var env = await SendJsonAsync<DataEnvelope<List<string>>>(HttpMethod.Get, "/v1/counties/" + County(county) + "/localities-km", null, cancellationToken)
                .ConfigureAwait(false);
            return (env.Data ?? new List<string>()).Where(s => s != null).Select(LocalityKm.Parse).ToList();
        }

        /// <summary>Sucursalele contului. <c>GET /v1/branches</c>.</summary>
        public Task<BranchList> GetBranchesAsync(CancellationToken cancellationToken = default)
        {
            return SendJsonAsync<BranchList>(HttpMethod.Get, "/v1/branches", null, cancellationToken);
        }

        // ============================================================== plumbing

        private static string Id(long value) => value.ToString(CultureInfo.InvariantCulture);

        private static string Bool(bool value) => value ? "true" : "false";

        private static string County(string county)
        {
            if (string.IsNullOrWhiteSpace(county)) throw new ArgumentException("County is required.", nameof(county));
            return Uri.EscapeDataString(county);
        }

        private static string ToJson(object request)
            => JsonSerializer.Serialize(request, request.GetType(), DragonStarJson.Options);

        private static string ToJson(object request, long pcId)
        {
            var node = JsonSerializer.SerializeToNode(request, request.GetType(), DragonStarJson.Options) as JsonObject
                       ?? new JsonObject();
            node["pcId"] = pcId;
            return node.ToJsonString(DragonStarJson.Options);
        }

        private async Task<T> SendJsonAsync<T>(HttpMethod method, string path, string? json, CancellationToken cancellationToken)
            where T : class
        {
            var result = await SendJsonOrNullAsync<T>(method, path, json, cancellationToken).ConfigureAwait(false);
            if (result == null)
                throw new DragonStarException(0, "invalid_response", "Invalid response", "The API returned an empty body.", null);
            return result;
        }

        private async Task<T?> SendJsonOrNullAsync<T>(HttpMethod method, string path, string? json, CancellationToken cancellationToken)
            where T : class
        {
            using (var response = await SendAsync(method, path, json, cancellationToken).ConfigureAwait(false))
            {
                var bytes = await ReadBytesAsync(response, cancellationToken).ConfigureAwait(false);
                if (bytes.Length == 0 || IsWhiteSpace(bytes)) return null;
                try
                {
                    return JsonSerializer.Deserialize<T>(bytes, DragonStarJson.Options);
                }
                catch (JsonException ex)
                {
                    throw new DragonStarException((int)response.StatusCode, "invalid_response", "Invalid response",
                        "The API response could not be parsed: " + ex.Message, null, null, null, ex);
                }
            }
        }

        private async Task<byte[]> SendBytesAsync(string path, CancellationToken cancellationToken)
        {
            using (var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false))
            {
                return await ReadBytesAsync(response, cancellationToken).ConfigureAwait(false);
            }
        }

        private static async Task<byte[]> ReadBytesAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            if (response.Content == null) return new byte[0];
#if NET5_0_OR_GREATER
            return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
#else
            cancellationToken.ThrowIfCancellationRequested();
            return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
#endif
        }

        private static bool IsWhiteSpace(byte[] bytes)
        {
            foreach (var b in bytes)
                if (b != (byte)' ' && b != (byte)'\n' && b != (byte)'\r' && b != (byte)'\t') return false;
            return true;
        }

        private HttpRequestMessage BuildRequest(HttpMethod method, string path, string? json)
        {
            var request = new HttpRequestMessage(method, new Uri(_baseUrl + path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            foreach (var mediaType in AcceptedMediaTypes) request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(mediaType));
            request.Headers.TryAddWithoutValidation("User-Agent", _userAgent);
            if (json != null)
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            return request;
        }

        /// <summary>
        /// Trimite cererea cu reîncercări conform contractului SDK și întoarce răspunsul de succes
        /// (apelantul îl eliberează). Orice răspuns ≥ 400 devine <see cref="DragonStarException"/>.
        /// </summary>
        private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? json, CancellationToken cancellationToken)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DragonStarClient));

            bool idempotent = method == HttpMethod.Get || method == HttpMethod.Delete;
            for (int attempt = 0; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool canRetry = attempt < _maxRetries;
                HttpResponseMessage response;

                using (var request = BuildRequest(method, path, json))
                using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    if (_timeout != System.Threading.Timeout.InfiniteTimeSpan) timeoutCts.CancelAfter(_timeout);
                    try
                    {
                        response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeoutCts.Token)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        // Timeout (nu anulare cerută de apelant).
                        if (idempotent && canRetry)
                        {
                            await Task.Delay(Backoff(attempt), cancellationToken).ConfigureAwait(false);
                            continue;
                        }

                        throw new DragonStarException(0, "network_error", "Network error",
                            "The request timed out after " + _timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture) + " s.",
                            null, null, null, ex);
                    }
                    catch (HttpRequestException ex)
                    {
                        if (idempotent && canRetry)
                        {
                            await Task.Delay(Backoff(attempt), cancellationToken).ConfigureAwait(false);
                            continue;
                        }

                        throw new DragonStarException(0, "network_error", "Network error", ex.Message, null, null, null, ex);
                    }
                }

                int status = (int)response.StatusCode;
                if (status < 400) return response;

                DragonStarException error;
                using (response)
                {
                    error = await BuildErrorAsync(response, cancellationToken).ConfigureAwait(false);
                }

                if (canRetry)
                {
                    if (status == 429 && error.Code != "temporarily_banned")
                    {
                        await Task.Delay(Cap(error.RetryAfter ?? TimeSpan.FromSeconds(1)), cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    if (status == 503 && idempotent)
                    {
                        await Task.Delay(Cap(error.RetryAfter ?? Backoff(attempt)), cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                }

                throw error;
            }
        }

        private static TimeSpan Backoff(int attempt)
            => Cap(TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt)));

        private static TimeSpan Cap(TimeSpan delay)
        {
            if (delay < TimeSpan.Zero) return TimeSpan.Zero;
            return delay > MaxRetryDelay ? MaxRetryDelay : delay;
        }

        private static async Task<DragonStarException> BuildErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            int status = (int)response.StatusCode;
            TimeSpan? retryAfter = ParseRetryAfter(response);

            ProblemDocument? problem = null;
            try
            {
                var bytes = await ReadBytesAsync(response, cancellationToken).ConfigureAwait(false);
                if (bytes.Length > 0 && !IsWhiteSpace(bytes))
                    problem = JsonSerializer.Deserialize<ProblemDocument>(bytes, DragonStarJson.Options);
            }
            catch (JsonException)
            {
                // Corpul nu e problem+json (ex. pagină HTML de la un proxy).
            }

            string? code = string.IsNullOrEmpty(problem?.Code) ? null : problem!.Code;
            return new DragonStarException(
                status,
                code,
                problem?.Title,
                problem?.Detail,
                problem?.Type,
                problem?.Errors,
                retryAfter);
        }

        private static TimeSpan? ParseRetryAfter(HttpResponseMessage response)
        {
            var header = response.Headers.RetryAfter;
            if (header != null)
            {
                if (header.Delta.HasValue) return header.Delta.Value;
                if (header.Date.HasValue)
                {
                    var delta = header.Date.Value - DateTimeOffset.UtcNow;
                    return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
                }
            }

            if (response.Headers.TryGetValues("Retry-After", out var values))
            {
                var raw = values.FirstOrDefault();
                if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0)
                    return TimeSpan.FromSeconds(seconds);
            }

            return null;
        }

        /// <summary>Eliberează <see cref="HttpClient"/>-ul intern (nu și pe cel extern).</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_ownsHttpClient) _http.Dispose();
        }
    }
}
