<?php

/**
 * Router script for `php -S`: a fake Dragon Star API used by the tests.
 *
 * State lives in the directory named by the DRAGONSTAR_FAKE_DIR env var:
 *   queue.json    — list of scripted responses, consumed one per request:
 *                   {"status": 200, "headers": {"X": "y"}, "json": {...}}
 *                   or {"status": 200, "body_b64": "...", "headers": {...}}
 *   requests.json — every request received (appended by this script).
 * With an empty queue the server answers 500.
 */

$dir = getenv('DRAGONSTAR_FAKE_DIR');
if (!$dir || !is_dir($dir)) {
    http_response_code(500);
    echo 'DRAGONSTAR_FAKE_DIR not set';
    return true;
}

$headers = [];
foreach (getallheaders() as $name => $value) {
    $headers[strtolower($name)] = $value;
}
$body = file_get_contents('php://input');

$logFile = $dir . '/requests.json';
$log = is_file($logFile) ? json_decode(file_get_contents($logFile), true) : [];
if (!is_array($log)) {
    $log = [];
}
$log[] = [
    'method' => $_SERVER['REQUEST_METHOD'],
    'uri' => $_SERVER['REQUEST_URI'],
    'path' => parse_url($_SERVER['REQUEST_URI'], PHP_URL_PATH),
    'query' => (string) parse_url($_SERVER['REQUEST_URI'], PHP_URL_QUERY),
    'headers' => $headers,
    'body' => $body,
];
file_put_contents($logFile, json_encode($log, JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE));

$queueFile = $dir . '/queue.json';
$queue = is_file($queueFile) ? json_decode(file_get_contents($queueFile), true) : [];
if (!is_array($queue) || count($queue) === 0) {
    http_response_code(500);
    header('Content-Type: text/plain');
    echo 'fake server: no scripted response left';
    return true;
}
$response = array_shift($queue);
file_put_contents($queueFile, json_encode($queue, JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE));

http_response_code(isset($response['status']) ? (int) $response['status'] : 200);
$responseHeaders = isset($response['headers']) ? $response['headers'] : [];
if (array_key_exists('json', $response) && !isset($responseHeaders['Content-Type'])) {
    $responseHeaders['Content-Type'] = ((int) $response['status'] >= 400) ? 'application/problem+json' : 'application/json';
}
foreach ($responseHeaders as $name => $value) {
    header($name . ': ' . $value);
}
if (array_key_exists('json', $response)) {
    echo json_encode($response['json'], JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE);
} elseif (isset($response['body_b64'])) {
    echo base64_decode($response['body_b64']);
} elseif (isset($response['body'])) {
    echo $response['body'];
}
return true;
