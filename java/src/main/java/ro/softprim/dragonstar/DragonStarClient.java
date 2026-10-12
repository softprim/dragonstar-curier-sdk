package ro.softprim.dragonstar;

import java.io.IOException;
import java.net.URI;
import java.net.URLEncoder;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.time.ZonedDateTime;
import java.time.format.DateTimeFormatter;
import java.util.ArrayList;
import java.util.Collection;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Objects;

/**
 * Client for the Dragon Star Curier API v1 ({@code https://api.curierdragonstar.ro}).
 *
 * <p>Thread-safe; create it once and reuse it.
 *
 * <pre>{@code
 * DragonStarClient client = DragonStarClient.builder("cheia-ta-api").build();
 * Map<String, Object> awb = client.createAwb(Map.of(
 *         "shipmentType", "parcel", "weight", 2, "pieces", 1,
 *         "recipientName", "Ion Popescu", "recipientPhone", "0722000000",
 *         "recipientAddress", "Str. Exemplu 1", "recipientCounty", "Cluj",
 *         "recipientCity", "Cluj-Napoca"));
 * }</pre>
 *
 * <h2>Data model</h2>
 * Request bodies are plain {@code Map<String, Object>} values (nested maps, lists, strings,
 * numbers, booleans and {@code null} are supported). Responses come back as
 * {@code Map<String, Object>} / {@code List<Object>} trees, where JSON objects are
 * {@code LinkedHashMap}, arrays are {@code ArrayList}, integers are {@code Long},
 * decimals are {@code Double}, plus {@code String}, {@code Boolean} and {@code null}.
 * Field names are exactly those of the API (see {@code docs/openapi.v1.en.json}).
 *
 * <h2>Errors and retries</h2>
 * Every HTTP status &ge; 400 throws {@link DragonStarException}; network errors and timeouts
 * throw it with status {@code 0} and code {@code network_error}. 429 is retried for every
 * method (except {@code temporarily_banned}); 503 and network errors only for GET and DELETE,
 * so a POST never creates duplicate AWBs. Wait = {@code Retry-After} (default 1 s, max 60 s).
 */
public final class DragonStarClient {

    /** SDK version. */
    public static final String VERSION = "1.0.0";
    /** Default API base URL. */
    public static final String DEFAULT_BASE_URL = "https://api.curierdragonstar.ro";
    /** Default {@code User-Agent}. */
    public static final String DEFAULT_USER_AGENT = "softprim-dragonstar-java/" + VERSION;
    /** Maximum AWBs per tracking request; larger lists are split automatically. */
    public static final int TRACKING_BATCH_SIZE = 100;

    private static final int MAX_RETRY_WAIT_SECONDS = 60;
    private static final int DEFAULT_RETRY_WAIT_SECONDS = 1;

    private final String apiKey;
    private final String baseUrl;
    private final Duration timeout;
    private final int maxRetries;
    private final String userAgent;
    private final HttpClient http;

    private DragonStarClient(Builder b) {
        this.apiKey = b.apiKey;
        this.baseUrl = b.baseUrl;
        this.timeout = b.timeout;
        this.maxRetries = b.maxRetries;
        this.userAgent = b.userAgent;
        this.http = b.httpClient != null
                ? b.httpClient
                : HttpClient.newBuilder().connectTimeout(b.timeout).build();
    }

    /** Starts a builder. {@code apiKey} is required. */
    public static Builder builder(String apiKey) {
        return new Builder(apiKey);
    }

    /** Shortcut for {@code builder(apiKey).build()}. */
    public static DragonStarClient create(String apiKey) {
        return builder(apiKey).build();
    }

    // ================================================================ shipments

    /** {@code POST /v1/awb} &rarr; {@code {awb, totalAmount, weight, pieces, codAmount, codPaymentType}}. */
    public Map<String, Object> createAwb(Map<String, ?> data) {
        return requestMap("POST", "/v1/awb", requireData(data));
    }

    /** {@code POST /v1/awb/branch}: like {@link #createAwb} on behalf of branch {@code pcId}. */
    public Map<String, Object> createAwbForBranch(long pcId, Map<String, ?> data) {
        return requestMap("POST", "/v1/awb/branch", withPcId(pcId, data));
    }

    /**
     * {@code POST /v1/awb/quote} &rarr; {@code {baseRate, distanceCost, weightCost, insuranceCost,
     * isListRate, netAmount, vatAmount, totalAmount}}.
     */
    public Map<String, Object> quote(Map<String, ?> data) {
        return requestMap("POST", "/v1/awb/quote", requireData(data));
    }

    /** {@code POST /v1/awb/quote/branch}: like {@link #quote} for branch {@code pcId}. */
    public Map<String, Object> quoteForBranch(long pcId, Map<String, ?> data) {
        return requestMap("POST", "/v1/awb/quote/branch", withPcId(pcId, data));
    }

    /** {@code GET /v1/awb/{awb}?events=true} &rarr; {@code {status, events}}. */
    public Map<String, Object> track(String awb) {
        return track(awb, true);
    }

    /** {@code GET /v1/awb/{awb}?events=true|false} &rarr; {@code {status, events?}}. */
    public Map<String, Object> track(String awb, boolean events) {
        return requestMap("GET", "/v1/awb/" + segment(awb) + "?events=" + events, null);
    }

    public Map<String, Object> track(long awb) {
        return track(Long.toString(awb), true);
    }

    public Map<String, Object> track(long awb, boolean events) {
        return track(Long.toString(awb), events);
    }

    /** {@link #trackMany(Collection, boolean)} without the event trail. */
    public Map<String, Object> trackMany(Collection<? extends Number> awbs) {
        return trackMany(awbs, false);
    }

    /**
     * {@code POST /v1/awb/tracking?events=…} &rarr; {@code {count, data, notFound}}.
     * More than 100 AWBs are split into batches of 100 and the results merged
     * ({@code data} and {@code notFound} concatenated in order, {@code count} = total of {@code data}).
     */
    public Map<String, Object> trackMany(Collection<? extends Number> awbs, boolean events) {
        Objects.requireNonNull(awbs, "awbs");
        List<Long> all = new ArrayList<>(awbs.size());
        for (Number n : awbs) {
            all.add(Objects.requireNonNull(n, "awb").longValue());
        }
        List<Object> data = new ArrayList<>();
        List<Object> notFound = new ArrayList<>();
        for (int i = 0; i < all.size(); i += TRACKING_BATCH_SIZE) {
            List<Long> batch = all.subList(i, Math.min(i + TRACKING_BATCH_SIZE, all.size()));
            Map<String, Object> body = new LinkedHashMap<>();
            body.put("awbs", new ArrayList<>(batch));
            Map<String, Object> res = requestMap("POST", "/v1/awb/tracking?events=" + events, body);
            data.addAll(asList(res.get("data")));
            notFound.addAll(asList(res.get("notFound")));
        }
        Map<String, Object> merged = new LinkedHashMap<>();
        merged.put("count", (long) data.size());
        merged.put("data", data);
        merged.put("notFound", notFound);
        return merged;
    }

    /** {@code GET /v1/awb/{awb}/return} &rarr; {@code {count, data}}. */
    public Map<String, Object> getReturns(String awb) {
        return requestMap("GET", "/v1/awb/" + segment(awb) + "/return", null);
    }

    public Map<String, Object> getReturns(long awb) {
        return getReturns(Long.toString(awb));
    }

    /** {@code DELETE /v1/awb/{awb}} (204, no content). */
    public void deleteAwb(String awb) {
        send("DELETE", "/v1/awb/" + segment(awb), null);
    }

    public void deleteAwb(long awb) {
        deleteAwb(Long.toString(awb));
    }

    /** {@code GET /v1/awb/{awb}/label} &rarr; PDF bytes in the account's default size. */
    public byte[] getLabel(String awb) {
        return getLabel(awb, null);
    }

    /**
     * {@code GET /v1/awb/{awb}/label/{format}} &rarr; PDF bytes.
     *
     * @param format {@code "A4"}, {@code "A6"} or {@code null} for the default size
     * @throws IllegalArgumentException for any other format (before any request is made)
     */
    public byte[] getLabel(String awb, String format) {
        if (format != null && !"A4".equals(format) && !"A6".equals(format)) {
            throw new IllegalArgumentException("Label format must be \"A4\" or \"A6\", got \"" + format + "\"");
        }
        String path = "/v1/awb/" + segment(awb) + "/label" + (format == null ? "" : "/" + format);
        return send("GET", path, null).body();
    }

    public byte[] getLabel(long awb) {
        return getLabel(Long.toString(awb), null);
    }

    public byte[] getLabel(long awb, String format) {
        return getLabel(Long.toString(awb), format);
    }

    /** ZPL label of the first parcel. */
    public String getLabelZpl(String awb) {
        return getLabelZpl(awb, 1);
    }

    /**
     * {@code GET /v1/awb/{awb}/label/zpl/{parcel}} &rarr; ZPL text.
     *
     * @throws IllegalArgumentException if {@code parcel < 1}
     */
    public String getLabelZpl(String awb, int parcel) {
        if (parcel < 1) {
            throw new IllegalArgumentException("parcel must be >= 1, got " + parcel);
        }
        byte[] body = send("GET", "/v1/awb/" + segment(awb) + "/label/zpl/" + parcel, null).body();
        return new String(body, StandardCharsets.UTF_8);
    }

    public String getLabelZpl(long awb) {
        return getLabelZpl(Long.toString(awb), 1);
    }

    public String getLabelZpl(long awb, int parcel) {
        return getLabelZpl(Long.toString(awb), parcel);
    }

    // ================================================================== pickups

    /** {@code POST /v1/pickups} &rarr; {@code {pickupId}}. */
    public Map<String, Object> createPickup(Map<String, ?> data) {
        return requestMap("POST", "/v1/pickups", requireData(data));
    }

    /** {@code POST /v1/pickups/branch} &rarr; {@code {pickupId}} or an empty map. */
    public Map<String, Object> createPickupForBranch(long pcId, Map<String, ?> data) {
        return requestMap("POST", "/v1/pickups/branch", withPcId(pcId, data));
    }

    // ================================================================ manifests

    /**
     * {@code POST /v1/manifests} (no body) &rarr; {@code {manifestId, shipmentCount}};
     * {@code manifestId} is {@code null} when there was nothing to close.
     */
    public Map<String, Object> createManifest() {
        Map<String, Object> res = requestMap("POST", "/v1/manifests", null);
        if (!res.containsKey("manifestId")) {
            res.put("manifestId", null);
        }
        if (!res.containsKey("shipmentCount")) {
            res.put("shipmentCount", 0L);
        }
        return res;
    }

    /** {@code GET /v1/manifests/{id}/pdf} &rarr; PDF bytes. */
    public byte[] getManifestPdf(String manifestId) {
        return send("GET", "/v1/manifests/" + segment(manifestId) + "/pdf", null).body();
    }

    public byte[] getManifestPdf(long manifestId) {
        return getManifestPdf(Long.toString(manifestId));
    }

    // ============================================================= nomenclature

    /** {@code GET /v1/counties} &rarr; code &rarr; name (e.g. {@code CJ → Cluj}), in API order. */
    public Map<String, String> getCounties() {
        Object data = requestMap("GET", "/v1/counties", null).get("data");
        Map<String, String> out = new LinkedHashMap<>();
        if (data instanceof Map) {
            for (Map.Entry<?, ?> e : ((Map<?, ?>) data).entrySet()) {
                out.put(String.valueOf(e.getKey()), e.getValue() == null ? null : String.valueOf(e.getValue()));
            }
        }
        return out;
    }

    /** {@code GET /v1/counties/{county}/localities} &rarr; locality names. {@code county} is the name, e.g. {@code "Cluj"}. */
    public List<String> getLocalities(String county) {
        return stringList(requestMap("GET", "/v1/counties/" + segment(county) + "/localities", null).get("data"));
    }

    /** {@code GET /v1/counties/{county}/localities-km} &rarr; localities with distance, parsed from {@code "Name:km"}. */
    public List<LocalityKm> getLocalitiesWithKm(String county) {
        List<String> raw = stringList(
                requestMap("GET", "/v1/counties/" + segment(county) + "/localities-km", null).get("data"));
        List<LocalityKm> out = new ArrayList<>(raw.size());
        for (String s : raw) {
            if (s != null) {
                out.add(LocalityKm.parse(s));
            }
        }
        return out;
    }

    /** {@code GET /v1/branches} &rarr; {@code {masterId, count, data}}. */
    public Map<String, Object> getBranches() {
        return requestMap("GET", "/v1/branches", null);
    }

    // ================================================================ accessors

    public String getBaseUrl() {
        return baseUrl;
    }

    public Duration getTimeout() {
        return timeout;
    }

    public int getMaxRetries() {
        return maxRetries;
    }

    public String getUserAgent() {
        return userAgent;
    }

    // ================================================================ internals

    private Map<String, Object> requestMap(String method, String path, Object body) {
        HttpResponse<byte[]> res = send(method, path, body);
        byte[] bytes = res.body();
        if (bytes == null || bytes.length == 0) {
            return new LinkedHashMap<>();
        }
        String text = new String(bytes, StandardCharsets.UTF_8).trim();
        if (text.isEmpty()) {
            return new LinkedHashMap<>();
        }
        Object parsed;
        try {
            parsed = Json.parse(text);
        } catch (IllegalArgumentException e) {
            throw new DragonStarException(res.statusCode(), "invalid_response", "Invalid response",
                    "The API returned a body that is not valid JSON", null, null, null, e);
        }
        if (parsed == null) {
            return new LinkedHashMap<>();
        }
        if (!(parsed instanceof Map)) {
            throw new DragonStarException(res.statusCode(), "invalid_response", "Invalid response",
                    "Expected a JSON object from the API", null, null, null, null);
        }
        @SuppressWarnings("unchecked")
        Map<String, Object> map = (Map<String, Object>) parsed;
        return map;
    }

    private HttpResponse<byte[]> send(String method, String path, Object jsonBody) {
        boolean idempotent = "GET".equals(method) || "DELETE".equals(method);
        String payload = jsonBody == null ? null : Json.stringify(jsonBody);
        int attempt = 0;
        while (true) {
            HttpRequest.Builder rb = HttpRequest.newBuilder(URI.create(baseUrl + path))
                    .timeout(timeout)
                    .header("Authorization", "Bearer " + apiKey)
                    .header("Accept", "application/json, application/problem+json, application/pdf, application/octet-stream")
                    .header("User-Agent", userAgent);
            if (payload != null) {
                rb.header("Content-Type", "application/json");
                rb.method(method, HttpRequest.BodyPublishers.ofString(payload, StandardCharsets.UTF_8));
            } else {
                rb.method(method, HttpRequest.BodyPublishers.noBody());
            }

            HttpResponse<byte[]> res;
            try {
                res = http.send(rb.build(), HttpResponse.BodyHandlers.ofByteArray());
            } catch (IOException e) {
                if (idempotent && attempt < maxRetries) {
                    attempt++;
                    pause(DEFAULT_RETRY_WAIT_SECONDS);
                    continue;
                }
                throw DragonStarException.network(e);
            } catch (InterruptedException e) {
                Thread.currentThread().interrupt();
                throw DragonStarException.network(e);
            }

            if (res.statusCode() < 400) {
                return res;
            }
            DragonStarException error = toError(res);
            if (attempt < maxRetries && shouldRetry(error, idempotent)) {
                attempt++;
                pause(error.getRetryAfter() == null ? DEFAULT_RETRY_WAIT_SECONDS : error.getRetryAfter());
                continue;
            }
            throw error;
        }
    }

    private static boolean shouldRetry(DragonStarException e, boolean idempotent) {
        if (e.getStatus() == 429) {
            return !"temporarily_banned".equals(e.getCode());
        }
        return e.getStatus() == 503 && idempotent;
    }

    private static void pause(int seconds) {
        long s = Math.max(0, Math.min(seconds, MAX_RETRY_WAIT_SECONDS));
        if (s == 0) {
            return;
        }
        try {
            Thread.sleep(s * 1000L);
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            throw DragonStarException.network(e);
        }
    }

    private static DragonStarException toError(HttpResponse<byte[]> res) {
        int status = res.statusCode();
        Integer retryAfter = parseRetryAfter(res.headers().firstValue("Retry-After").orElse(null));
        String code = null;
        String title = null;
        String detail = null;
        String type = null;
        Map<String, List<String>> errors = null;
        byte[] bytes = res.body();
        if (bytes != null && bytes.length > 0) {
            try {
                Object parsed = Json.parse(new String(bytes, StandardCharsets.UTF_8).trim());
                if (parsed instanceof Map) {
                    Map<?, ?> p = (Map<?, ?>) parsed;
                    code = str(p.get("code"));
                    title = str(p.get("title"));
                    detail = str(p.get("detail"));
                    type = str(p.get("type"));
                    errors = parseErrors(p.get("errors"));
                }
            } catch (IllegalArgumentException ignored) {
                // Not problem+json (e.g. a proxy HTML page): keep status only.
            }
        }
        return new DragonStarException(status, code, title, detail, type, errors, retryAfter, null);
    }

    private static Map<String, List<String>> parseErrors(Object raw) {
        if (!(raw instanceof Map)) {
            return null;
        }
        Map<String, List<String>> out = new LinkedHashMap<>();
        for (Map.Entry<?, ?> e : ((Map<?, ?>) raw).entrySet()) {
            List<String> messages = new ArrayList<>();
            if (e.getValue() instanceof List) {
                for (Object m : (List<?>) e.getValue()) {
                    messages.add(String.valueOf(m));
                }
            } else if (e.getValue() != null) {
                messages.add(String.valueOf(e.getValue()));
            }
            out.put(String.valueOf(e.getKey()), Collections.unmodifiableList(messages));
        }
        return out;
    }

    static Integer parseRetryAfter(String value) {
        if (value == null || value.trim().isEmpty()) {
            return null;
        }
        String v = value.trim();
        try {
            return Math.max(0, Integer.parseInt(v));
        } catch (NumberFormatException notSeconds) {
            try {
                ZonedDateTime when = ZonedDateTime.parse(v, DateTimeFormatter.RFC_1123_DATE_TIME);
                long secs = Duration.between(ZonedDateTime.now(when.getZone()), when).getSeconds();
                return (int) Math.max(0, Math.min(secs, Integer.MAX_VALUE));
            } catch (RuntimeException notDate) {
                return null;
            }
        }
    }

    private static String str(Object o) {
        return o == null ? null : String.valueOf(o);
    }

    private static List<Object> asList(Object o) {
        if (o instanceof List) {
            @SuppressWarnings("unchecked")
            List<Object> l = (List<Object>) o;
            return l;
        }
        return Collections.emptyList();
    }

    private static List<String> stringList(Object o) {
        List<String> out = new ArrayList<>();
        for (Object item : asList(o)) {
            out.add(item == null ? null : String.valueOf(item));
        }
        return out;
    }

    private static Map<String, ?> requireData(Map<String, ?> data) {
        return Objects.requireNonNull(data, "data");
    }

    private static Map<String, Object> withPcId(long pcId, Map<String, ?> data) {
        Map<String, Object> body = new LinkedHashMap<>(requireData(data));
        body.put("pcId", pcId);
        return body;
    }

    /** Percent-encodes one path segment (spaces as %20, diacritics as UTF-8). */
    static String segment(String value) {
        Objects.requireNonNull(value, "path parameter");
        if (value.isEmpty()) {
            throw new IllegalArgumentException("path parameter must not be empty");
        }
        return URLEncoder.encode(value, StandardCharsets.UTF_8)
                .replace("+", "%20")
                .replace("*", "%2A")
                .replace("%7E", "~");
    }

    // ================================================================== builder

    /** Builder for {@link DragonStarClient}. */
    public static final class Builder {
        private final String apiKey;
        private String baseUrl = DEFAULT_BASE_URL;
        private Duration timeout = Duration.ofSeconds(30);
        private int maxRetries = 2;
        private String userAgent = DEFAULT_USER_AGENT;
        private HttpClient httpClient;

        private Builder(String apiKey) {
            if (apiKey == null || apiKey.trim().isEmpty()) {
                throw new IllegalArgumentException("apiKey is required");
            }
            this.apiKey = apiKey;
        }

        /** API base URL, default {@value DragonStarClient#DEFAULT_BASE_URL}. Trailing slashes are removed. */
        public Builder baseUrl(String baseUrl) {
            if (baseUrl == null || baseUrl.trim().isEmpty()) {
                throw new IllegalArgumentException("baseUrl must not be empty");
            }
            String b = baseUrl.trim();
            while (b.endsWith("/")) {
                b = b.substring(0, b.length() - 1);
            }
            this.baseUrl = b;
            return this;
        }

        /** Per-request timeout, default 30 seconds. */
        public Builder timeout(Duration timeout) {
            if (timeout == null || timeout.isZero() || timeout.isNegative()) {
                throw new IllegalArgumentException("timeout must be positive");
            }
            this.timeout = timeout;
            return this;
        }

        /** Automatic retries, default 2; {@code 0} disables them. */
        public Builder maxRetries(int maxRetries) {
            if (maxRetries < 0) {
                throw new IllegalArgumentException("maxRetries must be >= 0");
            }
            this.maxRetries = maxRetries;
            return this;
        }

        /** {@code User-Agent} header, default {@value DragonStarClient#DEFAULT_USER_AGENT}. */
        public Builder userAgent(String userAgent) {
            if (userAgent == null || userAgent.trim().isEmpty()) {
                throw new IllegalArgumentException("userAgent must not be empty");
            }
            this.userAgent = userAgent;
            return this;
        }

        /** Optional custom {@link HttpClient} (proxy, SSL context, executor...). */
        public Builder httpClient(HttpClient httpClient) {
            this.httpClient = httpClient;
            return this;
        }

        public DragonStarClient build() {
            return new DragonStarClient(this);
        }
    }
}
