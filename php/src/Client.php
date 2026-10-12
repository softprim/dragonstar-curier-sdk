<?php

/**
 * Dragon Star Curier API v1 — PHP SDK.
 *
 * @license MIT
 * @copyright SoftPrim Technology SRL (office@softprim.ro)
 */

namespace SoftPrim\DragonStar;

/**
 * Client for the Dragon Star Curier API v1.
 *
 * Requires PHP >= 7.4 with ext-curl and ext-json. Every method returns
 * associative arrays decoded from JSON (or raw strings for PDF / ZPL) and
 * throws DragonStarError for any HTTP status >= 400 or network error.
 */
class Client
{
    const VERSION = '1.0.0';
    const DEFAULT_BASE_URL = 'https://api.curierdragonstar.ro';
    const TRACKING_BATCH_SIZE = 100;
    const MAX_RETRY_WAIT = 60;

    /** @var string */
    private $apiKey;
    /** @var string */
    private $baseUrl;
    /** @var float */
    private $timeout;
    /** @var int */
    private $maxRetries;
    /** @var string */
    private $userAgent;

    /**
     * @param string $apiKey  API key, sent as "Authorization: Bearer <key>".
     * @param array  $options baseUrl (string), timeout (seconds, default 30),
     *                        maxRetries (int, default 2, 0 disables), userAgent (string).
     */
    public function __construct(string $apiKey, array $options = [])
    {
        if (trim($apiKey) === '') {
            throw new \InvalidArgumentException('Dragon Star: apiKey is required.');
        }
        if (!function_exists('curl_init')) {
            throw new \RuntimeException('Dragon Star: the PHP curl extension (ext-curl) is required.');
        }
        $this->apiKey = $apiKey;
        $this->baseUrl = rtrim(isset($options['baseUrl']) ? (string) $options['baseUrl'] : self::DEFAULT_BASE_URL, '/');
        $this->timeout = isset($options['timeout']) ? (float) $options['timeout'] : 30.0;
        $this->maxRetries = isset($options['maxRetries']) ? max(0, (int) $options['maxRetries']) : 2;
        $this->userAgent = isset($options['userAgent']) && $options['userAgent'] !== ''
            ? (string) $options['userAgent']
            : 'softprim-dragonstar-php/' . self::VERSION;
    }

    // ------------------------------------------------------------------
    // Shipments (AWB)
    // ------------------------------------------------------------------

    /**
     * POST /v1/awb — creates a shipment.
     *
     * @return array {awb, totalAmount, weight, pieces, codAmount, codPaymentType}
     */
    public function createAwb(array $data): array
    {
        return $this->json('POST', '/v1/awb', null, $data);
    }

    /**
     * POST /v1/awb/branch — creates a shipment on behalf of a branch.
     *
     * @return array {awb, totalAmount, weight, pieces, codAmount, codPaymentType}
     */
    public function createAwbForBranch(int $pcId, array $data): array
    {
        $data['pcId'] = $pcId;
        return $this->json('POST', '/v1/awb/branch', null, $data);
    }

    /**
     * POST /v1/awb/quote — price estimate, nothing is created.
     *
     * @return array {baseRate, distanceCost, weightCost, insuranceCost, isListRate, netAmount, vatAmount, totalAmount}
     */
    public function quote(array $data): array
    {
        return $this->json('POST', '/v1/awb/quote', null, $data);
    }

    /**
     * POST /v1/awb/quote/branch — price estimate for a branch.
     */
    public function quoteForBranch(int $pcId, array $data): array
    {
        $data['pcId'] = $pcId;
        return $this->json('POST', '/v1/awb/quote/branch', null, $data);
    }

    /**
     * GET /v1/awb/{awb} — status (and the checkpoint trail when $events is true).
     *
     * @param int|string $awb
     * @return array {status, events?}
     */
    public function track($awb, bool $events = true): array
    {
        return $this->json('GET', '/v1/awb/' . $this->segment($awb), ['events' => $events ? 'true' : 'false']);
    }

    /**
     * POST /v1/awb/tracking — tracks several AWBs. More than 100 AWBs are split
     * automatically into batches of 100 and the results merged.
     *
     * @param array<int|string> $awbs
     * @return array {count, data[], notFound[]}
     */
    public function trackMany(array $awbs, bool $events = false): array
    {
        $result = ['count' => 0, 'data' => [], 'notFound' => []];
        $awbs = array_values($awbs);
        if (count($awbs) === 0) {
            return $result;
        }
        $query = ['events' => $events ? 'true' : 'false'];
        foreach (array_chunk($awbs, self::TRACKING_BATCH_SIZE) as $batch) {
            $batch = array_map(function ($awb) {
                return (is_string($awb) && ctype_digit($awb)) ? (int) $awb : $awb;
            }, $batch);
            $page = $this->json('POST', '/v1/awb/tracking', $query, ['awbs' => $batch]);
            if (isset($page['data']) && is_array($page['data'])) {
                foreach ($page['data'] as $row) {
                    $result['data'][] = $row;
                }
            }
            if (isset($page['notFound']) && is_array($page['notFound'])) {
                foreach ($page['notFound'] as $awb) {
                    $result['notFound'][] = $awb;
                }
            }
        }
        $result['count'] = count($result['data']);
        return $result;
    }

    /**
     * GET /v1/awb/{awb}/return — the return leg(s) of a shipment.
     *
     * @param int|string $awb
     * @return array {count, data[]}
     */
    public function getReturns($awb): array
    {
        return $this->json('GET', '/v1/awb/' . $this->segment($awb) . '/return');
    }

    /**
     * DELETE /v1/awb/{awb} — deletes a shipment (204, nothing returned).
     *
     * @param int|string $awb
     */
    public function deleteAwb($awb): void
    {
        $this->request('DELETE', '/v1/awb/' . $this->segment($awb));
    }

    /**
     * GET /v1/awb/{awb}/label[/{A4|A6}] — the label as raw PDF bytes.
     *
     * @param int|string  $awb
     * @param string|null $format null (account default), "A4" or "A6" (exact case).
     */
    public function getLabel($awb, ?string $format = null): string
    {
        $path = '/v1/awb/' . $this->segment($awb) . '/label';
        if ($format !== null) {
            if ($format !== 'A4' && $format !== 'A6') {
                throw new \InvalidArgumentException('Dragon Star: label format must be "A4" or "A6", got "' . $format . '".');
            }
            $path .= '/' . $format;
        }
        return $this->request('GET', $path)['body'];
    }

    /**
     * GET /v1/awb/{awb}/label/zpl/{parcel} — the label of one parcel as ZPL text.
     *
     * @param int|string $awb
     */
    public function getLabelZpl($awb, int $parcel = 1): string
    {
        if ($parcel < 1) {
            throw new \InvalidArgumentException('Dragon Star: parcel must be >= 1.');
        }
        return $this->request('GET', '/v1/awb/' . $this->segment($awb) . '/label/zpl/' . $parcel)['body'];
    }

    // ------------------------------------------------------------------
    // Pickups and manifests
    // ------------------------------------------------------------------

    /**
     * POST /v1/pickups — orders a courier pickup.
     *
     * @return array {pickupId}
     */
    public function createPickup(array $data): array
    {
        return $this->json('POST', '/v1/pickups', null, $data);
    }

    /**
     * POST /v1/pickups/branch — orders a pickup for a branch.
     *
     * @return array {pickupId} or an empty array
     */
    public function createPickupForBranch(int $pcId, array $data): array
    {
        $data['pcId'] = $pcId;
        return $this->json('POST', '/v1/pickups/branch', null, $data);
    }

    /**
     * POST /v1/manifests — closes the un-manifested shipments into a manifest.
     *
     * @return array {manifestId, shipmentCount}; manifestId is null when there was nothing to close.
     */
    public function createManifest(): array
    {
        $result = $this->json('POST', '/v1/manifests');
        if (!array_key_exists('manifestId', $result)) {
            $result['manifestId'] = null;
        }
        if (!array_key_exists('shipmentCount', $result)) {
            $result['shipmentCount'] = 0;
        }
        return $result;
    }

    /**
     * GET /v1/manifests/{id}/pdf — the manifest as raw PDF bytes.
     *
     * @param int|string $manifestId
     */
    public function getManifestPdf($manifestId): string
    {
        return $this->request('GET', '/v1/manifests/' . $this->segment($manifestId) . '/pdf')['body'];
    }

    // ------------------------------------------------------------------
    // Nomenclature
    // ------------------------------------------------------------------

    /**
     * GET /v1/counties — code => name (e.g. "CJ" => "Cluj").
     *
     * @return array<string, string>
     */
    public function getCounties(): array
    {
        $result = $this->json('GET', '/v1/counties');
        return isset($result['data']) && is_array($result['data']) ? $result['data'] : [];
    }

    /**
     * GET /v1/counties/{county}/localities — the locality names of a county.
     *
     * @param string $county county name (a value from getCounties(), e.g. "Cluj")
     * @return string[]
     */
    public function getLocalities(string $county): array
    {
        $result = $this->json('GET', '/v1/counties/' . rawurlencode($county) . '/localities');
        return isset($result['data']) && is_array($result['data']) ? array_values($result['data']) : [];
    }

    /**
     * GET /v1/counties/{county}/localities-km — localities with distance in km,
     * parsed from "Name:km" (split at the last ":").
     *
     * @return array<int, array{name: string, km: int|float|null}>
     */
    public function getLocalitiesWithKm(string $county): array
    {
        $result = $this->json('GET', '/v1/counties/' . rawurlencode($county) . '/localities-km');
        $rows = isset($result['data']) && is_array($result['data']) ? $result['data'] : [];
        $out = [];
        foreach ($rows as $row) {
            $out[] = self::parseLocalityKm((string) $row);
        }
        return $out;
    }

    /**
     * GET /v1/branches — the branches of the master account.
     *
     * @return array {masterId, count, data[]}
     */
    public function getBranches(): array
    {
        return $this->json('GET', '/v1/branches');
    }

    /**
     * Parses "Cluj-Napoca:450" into ['name' => 'Cluj-Napoca', 'km' => 450].
     *
     * @return array{name: string, km: int|float|null}
     */
    public static function parseLocalityKm(string $value): array
    {
        $pos = strrpos($value, ':');
        if ($pos === false) {
            return ['name' => trim($value), 'km' => null];
        }
        $name = trim(substr($value, 0, $pos));
        $km = trim(substr($value, $pos + 1));
        if ($km === '' || !is_numeric($km)) {
            return ['name' => $name, 'km' => null];
        }
        return ['name' => $name, 'km' => (strpos($km, '.') === false && strpos($km, 'e') === false && strpos($km, 'E') === false) ? (int) $km : (float) $km];
    }

    // ------------------------------------------------------------------
    // Transport
    // ------------------------------------------------------------------

    /** @param int|string $value */
    private function segment($value): string
    {
        $value = (string) $value;
        if ($value === '') {
            throw new \InvalidArgumentException('Dragon Star: path parameter must not be empty.');
        }
        return rawurlencode($value);
    }

    /**
     * Performs a request and decodes the JSON response (an empty body gives []).
     */
    private function json(string $method, string $path, ?array $query = null, ?array $body = null): array
    {
        $response = $this->request($method, $path, $query, $body);
        $raw = trim($response['body']);
        if ($raw === '') {
            return [];
        }
        $decoded = json_decode($raw, true);
        if (!is_array($decoded)) {
            throw new DragonStarError(
                'Dragon Star: invalid JSON in response (HTTP ' . $response['status'] . ').',
                $response['status'],
                'invalid_response'
            );
        }
        return $decoded;
    }

    /**
     * Sends a request with the retry policy of the SDK contract.
     *
     * @return array{status: int, headers: array<string, string>, body: string}
     */
    private function request(string $method, string $path, ?array $query = null, ?array $body = null): array
    {
        $url = $this->baseUrl . $path;
        if ($query) {
            $url .= '?' . http_build_query($query, '', '&', PHP_QUERY_RFC3986);
        }
        $idempotent = ($method === 'GET' || $method === 'DELETE');
        $attempt = 0;

        while (true) {
            try {
                $response = $this->send($method, $url, $body);
            } catch (DragonStarError $e) {
                // network error / timeout: retry only idempotent requests
                if ($idempotent && $attempt < $this->maxRetries) {
                    $attempt++;
                    $this->pause($this->backoff($attempt));
                    continue;
                }
                throw $e;
            }

            $status = $response['status'];
            if ($status < 400) {
                return $response;
            }

            $retryAfter = self::parseRetryAfter(isset($response['headers']['retry-after']) ? $response['headers']['retry-after'] : null);
            $error = DragonStarError::fromResponse($status, $response['body'], $retryAfter);

            if ($attempt < $this->maxRetries) {
                $retry = false;
                if ($status === 429 && $error->getErrorCode() !== 'temporarily_banned') {
                    $retry = true; // the request was refused, nothing was created
                } elseif ($status === 503 && $idempotent) {
                    $retry = true;
                }
                if ($retry) {
                    $attempt++;
                    $this->pause($retryAfter !== null ? $retryAfter : 1);
                    continue;
                }
            }
            throw $error;
        }
    }

    /**
     * One HTTP exchange through curl.
     *
     * @return array{status: int, headers: array<string, string>, body: string}
     */
    private function send(string $method, string $url, ?array $body): array
    {
        $headers = [
            'Authorization: Bearer ' . $this->apiKey,
            'Accept: application/json, application/problem+json, application/pdf, application/octet-stream',
            'User-Agent: ' . $this->userAgent,
            'Expect:',
        ];

        $ch = curl_init();
        $options = [
            CURLOPT_URL => $url,
            CURLOPT_CUSTOMREQUEST => $method,
            CURLOPT_RETURNTRANSFER => true,
            CURLOPT_FOLLOWLOCATION => false,
            CURLOPT_TIMEOUT_MS => (int) round($this->timeout * 1000),
            CURLOPT_CONNECTTIMEOUT_MS => (int) round(min($this->timeout, 10.0) * 1000),
            CURLOPT_NOSIGNAL => true,
        ];

        if ($body !== null) {
            $encoded = json_encode(
                count($body) === 0 ? new \stdClass() : $body,
                JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES | JSON_PRESERVE_ZERO_FRACTION
            );
            if ($encoded === false) {
                curl_close($ch);
                throw new \InvalidArgumentException('Dragon Star: request body cannot be encoded as JSON: ' . json_last_error_msg());
            }
            $headers[] = 'Content-Type: application/json';
            $options[CURLOPT_POSTFIELDS] = $encoded;
        } elseif ($method === 'POST') {
            $options[CURLOPT_POSTFIELDS] = '';
        }

        $responseHeaders = [];
        $options[CURLOPT_HTTPHEADER] = $headers;
        $options[CURLOPT_HEADERFUNCTION] = function ($handle, $line) use (&$responseHeaders) {
            $length = strlen($line);
            $line = trim($line);
            if (stripos($line, 'HTTP/') === 0) {
                $responseHeaders = []; // a new response (e.g. after 100 Continue)
            } elseif (($pos = strpos($line, ':')) !== false) {
                $responseHeaders[strtolower(trim(substr($line, 0, $pos)))] = trim(substr($line, $pos + 1));
            }
            return $length;
        };
        curl_setopt_array($ch, $options);

        $result = curl_exec($ch);
        if ($result === false) {
            $message = curl_error($ch);
            $errno = curl_errno($ch);
            curl_close($ch);
            throw new DragonStarError(
                'Dragon Star: network error: ' . $message,
                0,
                'network_error',
                null,
                $message . ' (curl ' . $errno . ')'
            );
        }
        $status = (int) curl_getinfo($ch, CURLINFO_RESPONSE_CODE);
        curl_close($ch);

        return ['status' => $status, 'headers' => $responseHeaders, 'body' => (string) $result];
    }

    /** Retry-After in seconds (numeric or HTTP-date); null when absent/invalid. */
    private static function parseRetryAfter(?string $value): ?int
    {
        if ($value === null || trim($value) === '') {
            return null;
        }
        $value = trim($value);
        if (ctype_digit($value)) {
            return (int) $value;
        }
        $time = strtotime($value);
        if ($time === false) {
            return null;
        }
        return max(0, $time - time());
    }

    private function backoff(int $attempt): float
    {
        return min(self::MAX_RETRY_WAIT, 0.5 * pow(2, $attempt - 1));
    }

    /** @param int|float $seconds */
    private function pause($seconds): void
    {
        $seconds = min(self::MAX_RETRY_WAIT, max(0, $seconds));
        if ($seconds > 0) {
            usleep((int) round($seconds * 1000000));
        }
    }
}
