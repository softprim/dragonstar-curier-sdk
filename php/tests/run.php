<?php

/**
 * Test runner for the Dragon Star PHP SDK — no PHPUnit needed.
 *
 *   php php/tests/run.php
 *
 * Starts `php -S` with tests/fake-server.php on a free local port, runs every
 * test, prints PASS/FAIL per test and exits non-zero when any test fails.
 */

require __DIR__ . '/../dragonstar.php';

use SoftPrim\DragonStar\Client;
use SoftPrim\DragonStar\DragonStarError;
use SoftPrim\DragonStar\Status;

error_reporting(E_ALL);
set_error_handler(function ($severity, $message, $file, $line) {
    if (!(error_reporting() & $severity)) {
        return false; // silenced with @
    }
    throw new \ErrorException($message, 0, $severity, $file, $line);
});

// ---------------------------------------------------------------------------
// Fake server
// ---------------------------------------------------------------------------

final class FakeServer
{
    /** @var string */
    public $dir;
    /** @var string */
    public $baseUrl;
    /** @var resource */
    private $process;

    public function start(): void
    {
        $this->dir = sys_get_temp_dir() . DIRECTORY_SEPARATOR . 'dragonstar-php-test-' . getmypid() . '-' . bin2hex(random_bytes(4));
        mkdir($this->dir, 0777, true);
        $this->reset([]);

        $probe = stream_socket_server('tcp://127.0.0.1:0', $errno, $errstr);
        if ($probe === false) {
            throw new \RuntimeException("cannot find a free port: $errstr");
        }
        $name = stream_socket_get_name($probe, false);
        fclose($probe);
        $port = (int) substr($name, strrpos($name, ':') + 1);
        $this->baseUrl = 'http://127.0.0.1:' . $port;

        $env = getenv();
        $env['DRAGONSTAR_FAKE_DIR'] = $this->dir;
        $log = $this->dir . DIRECTORY_SEPARATOR . 'server.log';
        $this->process = proc_open(
            [PHP_BINARY, '-S', '127.0.0.1:' . $port, __DIR__ . DIRECTORY_SEPARATOR . 'fake-server.php'],
            [0 => ['pipe', 'r'], 1 => ['file', $log, 'a'], 2 => ['file', $log, 'a']],
            $pipes,
            __DIR__,
            $env
        );
        if (!is_resource($this->process)) {
            throw new \RuntimeException('cannot start php -S');
        }
        register_shutdown_function([$this, 'stop']); // never leave php -S running


        $deadline = microtime(true) + 10;
        while (microtime(true) < $deadline) {
            $socket = @fsockopen('127.0.0.1', $port, $errno, $errstr, 0.2);
            if ($socket) {
                fclose($socket);
                return;
            }
            usleep(50000);
        }
        $this->stop();
        throw new \RuntimeException('php -S did not start: ' . @file_get_contents($log));
    }

    public function stop(): void
    {
        if (is_resource($this->process)) {
            proc_terminate($this->process);
            proc_close($this->process);
        }
        if ($this->dir && is_dir($this->dir)) {
            foreach (glob($this->dir . DIRECTORY_SEPARATOR . '*') as $file) {
                @unlink($file);
            }
            @rmdir($this->dir);
        }
    }

    /** Scripts the next responses and clears the request log. */
    public function reset(array $responses): void
    {
        file_put_contents($this->dir . '/queue.json', json_encode($responses, JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE));
        file_put_contents($this->dir . '/requests.json', '[]');
    }

    /** @return array[] */
    public function requests(): array
    {
        $log = json_decode(file_get_contents($this->dir . '/requests.json'), true);
        return is_array($log) ? $log : [];
    }

    public function client(array $options = []): Client
    {
        return new Client('test-key-123', $options + ['baseUrl' => $this->baseUrl, 'timeout' => 10]);
    }
}

// ---------------------------------------------------------------------------
// Tiny assertion helpers
// ---------------------------------------------------------------------------

function check($condition, string $message): void
{
    if (!$condition) {
        throw new \RuntimeException('assertion failed: ' . $message);
    }
}

function same($expected, $actual, string $message): void
{
    if ($expected !== $actual) {
        throw new \RuntimeException(
            'assertion failed: ' . $message . "\n      expected: " . var_export($expected, true) . "\n      actual:   " . var_export($actual, true)
        );
    }
}

/** @return \Throwable */
function expectThrows(string $class, callable $fn)
{
    try {
        $fn();
    } catch (\Throwable $e) {
        if (!($e instanceof $class)) {
            throw new \RuntimeException('expected ' . $class . ', got ' . get_class($e) . ': ' . $e->getMessage());
        }
        return $e;
    }
    throw new \RuntimeException('expected ' . $class . ' to be thrown');
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

$tests = [];

$tests['1. sends Authorization: Bearer and User-Agent'] = function (FakeServer $s) {
    $s->reset([['status' => 200, 'json' => ['masterId' => 454, 'count' => 0, 'data' => []]]]);
    $result = $s->client()->getBranches();
    same(454, $result['masterId'], 'masterId');
    $req = $s->requests();
    same(1, count($req), 'one request');
    same('GET', $req[0]['method'], 'method');
    same('/v1/branches', $req[0]['path'], 'path');
    same('Bearer test-key-123', $req[0]['headers']['authorization'], 'Authorization header');
    same('softprim-dragonstar-php/' . Client::VERSION, $req[0]['headers']['user-agent'], 'User-Agent header');
    same('softprim-dragonstar-php/1.0.0', $req[0]['headers']['user-agent'], 'User-Agent version');
    check(stripos($req[0]['headers']['accept'], 'application/json') === 0, 'Accept header starts with application/json');

    $s->reset([['status' => 200, 'json' => ['masterId' => 1, 'count' => 0, 'data' => []]]]);
    $s->client(['userAgent' => 'my-shop/2.0'])->getBranches();
    same('my-shop/2.0', $s->requests()[0]['headers']['user-agent'], 'custom User-Agent');
};

$tests['2. createAwb sends the JSON body and returns the AWB'] = function (FakeServer $s) {
    $created = ['awb' => 94348148, 'totalAmount' => 9.83, 'weight' => 0.5, 'pieces' => 1, 'codAmount' => 100, 'codPaymentType' => 'account'];
    $s->reset([['status' => 201, 'json' => $created]]);
    $data = [
        'shipmentType' => 'parcel',
        'weight' => 5,
        'recipientName' => 'Ion Popescu',
        'recipientAddress' => 'Str. Memorandumului 1',
        'recipientCounty' => 'Cluj',
        'recipientCity' => 'Cluj-Napoca',
        'recipientPhone' => '0722123456',
        'codAmount' => 100,
        'notes' => 'Fragil — țărână',
    ];
    $result = $s->client()->createAwb($data);
    same(94348148, $result['awb'], 'awb');
    same(9.83, $result['totalAmount'], 'totalAmount');
    $req = $s->requests()[0];
    same('POST', $req['method'], 'method');
    same('/v1/awb', $req['path'], 'path');
    check(stripos($req['headers']['content-type'], 'application/json') === 0, 'Content-Type is application/json');
    same($data, json_decode($req['body'], true), 'request body');

    // branch variant adds pcId
    $s->reset([['status' => 201, 'json' => $created]]);
    $s->client()->createAwbForBranch(454, $data);
    $req = $s->requests()[0];
    same('/v1/awb/branch', $req['path'], 'branch path');
    same(454, json_decode($req['body'], true)['pcId'], 'pcId in body');
};

$tests['3. a 422 becomes DragonStarError with validation_failed and errors'] = function (FakeServer $s) {
    $s->reset([['status' => 422, 'json' => [
        'type' => 'https://api.curierdragonstar.ro/errors/validation-failed',
        'title' => 'Validation failed',
        'status' => 422,
        'code' => 'validation_failed',
        'detail' => 'The request contains invalid fields.',
        'errors' => ['weight' => ['required if shipmentType = parcel or pallet']],
    ]]]);
    $e = expectThrows(DragonStarError::class, function () use ($s) {
        $s->client()->createAwb(['shipmentType' => 'parcel']);
    });
    same(422, $e->getStatus(), 'status');
    same(422, $e->getCode(), 'getCode() is the HTTP status');
    same('validation_failed', $e->getErrorCode(), 'error code');
    same('Validation failed', $e->getTitle(), 'title');
    same('The request contains invalid fields.', $e->getDetail(), 'detail');
    same('The request contains invalid fields.', $e->getMessage(), 'message = detail');
    same('https://api.curierdragonstar.ro/errors/validation-failed', $e->getType(), 'type');
    same(['weight' => ['required if shipmentType = parcel or pallet']], $e->getErrors(), 'errors');
    same(null, $e->getRetryAfter(), 'no Retry-After');
    same(1, count($s->requests()), '422 is not retried');

    // message falls back to title, then to "HTTP <status>"
    $s->reset([['status' => 404, 'json' => ['title' => 'Resource not found', 'status' => 404, 'code' => 'awb_not_found']]]);
    $e = expectThrows(DragonStarError::class, function () use ($s) {
        $s->client(['maxRetries' => 0])->track(1);
    });
    same('Resource not found', $e->getMessage(), 'message = title');
    $s->reset([['status' => 500, 'body' => 'oops']]);
    $e = expectThrows(DragonStarError::class, function () use ($s) {
        $s->client(['maxRetries' => 0])->track(1);
    });
    same('HTTP 500', $e->getMessage(), 'message = HTTP status');
    same(null, $e->getErrorCode(), 'no code');
};

$tests['4. 429 with Retry-After: 0 is retried and succeeds'] = function (FakeServer $s) {
    $tooMany = ['status' => 429, 'headers' => ['Retry-After' => '0'], 'json' => [
        'type' => 'https://api.curierdragonstar.ro/errors/rate-limit-exceeded',
        'title' => 'Too many requests', 'status' => 429, 'code' => 'rate_limit_exceeded',
    ]];
    $s->reset([$tooMany, ['status' => 201, 'json' => ['awb' => 1001]]]);
    $result = $s->client()->createAwb(['shipmentType' => 'parcel']);
    same(1001, $result['awb'], 'awb after retry');
    same(2, count($s->requests()), 'two requests (POST retried on 429)');

    // temporarily_banned is not retried
    $banned = $tooMany;
    $banned['json']['code'] = 'temporarily_banned';
    $s->reset([$banned, ['status' => 200, 'json' => []]]);
    $e = expectThrows(DragonStarError::class, function () use ($s) {
        $s->client()->getBranches();
    });
    same('temporarily_banned', $e->getErrorCode(), 'banned code');
    same(0, $e->getRetryAfter(), 'retryAfter parsed');
    same(1, count($s->requests()), 'temporarily_banned not retried');

    // maxRetries is respected
    $s->reset([$tooMany, $tooMany, $tooMany, ['status' => 200, 'json' => []]]);
    expectThrows(DragonStarError::class, function () use ($s) {
        $s->client(['maxRetries' => 2])->getBranches();
    });
    same(3, count($s->requests()), '1 attempt + 2 retries');
};

$tests['5. a POST that receives 503 is NOT retried (GET is)'] = function (FakeServer $s) {
    $unavailable = ['status' => 503, 'headers' => ['Retry-After' => '0'], 'json' => [
        'title' => 'Service unavailable', 'status' => 503, 'code' => 'service_unavailable',
    ]];
    $s->reset([$unavailable, ['status' => 201, 'json' => ['awb' => 1]]]);
    $e = expectThrows(DragonStarError::class, function () use ($s) {
        $s->client()->createAwb(['shipmentType' => 'parcel']);
    });
    same(503, $e->getStatus(), 'status');
    same('service_unavailable', $e->getErrorCode(), 'code');
    same(1, count($s->requests()), 'POST not retried');

    $s->reset([$unavailable, ['status' => 200, 'json' => ['status' => ['statusCode' => 3]]]]);
    $result = $s->client()->track(94348148);
    same(3, $result['status']['statusCode'], 'GET retried after 503');
    same(2, count($s->requests()), 'GET made two requests');

    $s->reset([$unavailable, ['status' => 204]]);
    $s->client()->deleteAwb(94348148);
    same(2, count($s->requests()), 'DELETE retried after 503');
    same('DELETE', $s->requests()[1]['method'], 'DELETE method');
};

$tests['6. trackMany with 150 AWBs makes 2 requests and merges data + notFound'] = function (FakeServer $s) {
    $awbs = range(90000001, 90000150);
    $first = array_slice($awbs, 0, 100);
    $second = array_slice($awbs, 100);
    $page1 = ['count' => 99, 'data' => array_map(function ($a) {
        return ['awb' => $a, 'status' => ['statusCode' => 6]];
    }, array_slice($first, 0, 99)), 'notFound' => [$first[99]]];
    $page2 = ['count' => 48, 'data' => array_map(function ($a) {
        return ['awb' => $a, 'status' => ['statusCode' => 3]];
    }, array_slice($second, 0, 48)), 'notFound' => [$second[48], $second[49]]];
    $s->reset([['status' => 200, 'json' => $page1], ['status' => 200, 'json' => $page2]]);

    $result = $s->client()->trackMany($awbs);
    $req = $s->requests();
    same(2, count($req), 'two requests');
    same('/v1/awb/tracking', $req[0]['path'], 'path');
    same('events=false', $req[0]['query'], 'events=false by default');
    same(['awbs' => $first], json_decode($req[0]['body'], true), 'first batch = 100 AWBs');
    same(['awbs' => $second], json_decode($req[1]['body'], true), 'second batch = 50 AWBs');
    same(147, $result['count'], 'merged count');
    same(147, count($result['data']), 'merged data');
    same([$first[99], $second[48], $second[49]], $result['notFound'], 'merged notFound');
    same(90000001, $result['data'][0]['awb'], 'order kept');

    $s->reset([['status' => 200, 'json' => ['count' => 0, 'data' => [], 'notFound' => [1]]]]);
    $s->client()->trackMany([1], true);
    same('events=true', $s->requests()[0]['query'], 'events=true');
};

$tests['7. getLabel returns unchanged bytes and refuses format "a4"'] = function (FakeServer $s) {
    $pdf = "%PDF-1.4\n\x00\x01\x02\xff\xfe\r\n binary \x80\x81 %%EOF";
    $s->reset([
        ['status' => 200, 'headers' => ['Content-Type' => 'application/pdf'], 'body_b64' => base64_encode($pdf)],
        ['status' => 200, 'headers' => ['Content-Type' => 'application/pdf'], 'body_b64' => base64_encode($pdf)],
    ]);
    $client = $s->client();
    same($pdf, $client->getLabel(94348148), 'default label bytes');
    same($pdf, $client->getLabel(94348148, 'A6'), 'A6 label bytes');
    $req = $s->requests();
    same('/v1/awb/94348148/label', $req[0]['path'], 'default label path');
    same('/v1/awb/94348148/label/A6', $req[1]['path'], 'A6 label path');

    $s->reset([]);
    expectThrows(\InvalidArgumentException::class, function () use ($client) {
        $client->getLabel(94348148, 'a4');
    });
    expectThrows(\InvalidArgumentException::class, function () use ($client) {
        $client->getLabel(94348148, 'A5');
    });
    expectThrows(\InvalidArgumentException::class, function () use ($client) {
        $client->getLabelZpl(94348148, 0);
    });
    same(0, count($s->requests()), 'no request for an invalid format / parcel');

    $s->reset([['status' => 200, 'headers' => ['Content-Type' => 'text/plain'], 'body' => "^XA^FDtest^FS^XZ"]]);
    same("^XA^FDtest^FS^XZ", $client->getLabelZpl(94348148, 2), 'ZPL text');
    same('/v1/awb/94348148/label/zpl/2', $s->requests()[0]['path'], 'ZPL path');
};

$tests['8. getLocalitiesWithKm parses "Cluj-Napoca:450"'] = function (FakeServer $s) {
    $s->reset([['status' => 200, 'json' => ['count' => 3, 'data' => ['Cluj-Napoca:450', 'Sat: Vale:12', 'Fara km']]]]);
    $result = $s->client()->getLocalitiesWithKm('Cluj');
    same([
        ['name' => 'Cluj-Napoca', 'km' => 450],
        ['name' => 'Sat: Vale', 'km' => 12],
        ['name' => 'Fara km', 'km' => null],
    ], $result, 'parsed localities');
    same('/v1/counties/Cluj/localities-km', $s->requests()[0]['path'], 'path');
};

$tests['9. a county with diacritics / spaces is URL-encoded'] = function (FakeServer $s) {
    $s->reset([
        ['status' => 200, 'json' => ['count' => 1, 'data' => ['Bistrița']]],
        ['status' => 200, 'json' => ['count' => 1, 'data' => ['Satu Mare:600']]],
    ]);
    $client = $s->client();
    same(['Bistrița'], $client->getLocalities('Bistrița-Năsăud'), 'localities');
    $client->getLocalitiesWithKm('Satu Mare');
    $req = $s->requests();
    same('/v1/counties/Bistri%C8%9Ba-N%C4%83s%C4%83ud/localities', $req[0]['uri'], 'encoded diacritics');
    same('/v1/counties/Satu%20Mare/localities-km', $req[1]['uri'], 'encoded space');
};

$tests['extra: other endpoints, manifests, counties, status helpers'] = function (FakeServer $s) {
    $client = $s->client();
    $s->reset([
        ['status' => 200, 'json' => ['count' => 2, 'data' => ['B' => 'Bucuresti', 'CJ' => 'Cluj']]],
        ['status' => 201, 'json' => ['manifestId' => 77, 'shipmentCount' => 12]],
        ['status' => 200],
        ['status' => 200, 'headers' => ['Content-Type' => 'application/pdf'], 'body' => '%PDF-manifest'],
        ['status' => 201, 'json' => ['pickupId' => 5]],
        ['status' => 201],
        ['status' => 200, 'json' => ['totalAmount' => 12.5]],
        ['status' => 200, 'json' => ['count' => 0, 'data' => []]],
    ]);
    same(['B' => 'Bucuresti', 'CJ' => 'Cluj'], $client->getCounties(), 'counties map');
    same(['manifestId' => 77, 'shipmentCount' => 12], $client->createManifest(), 'manifest');
    same(['manifestId' => null, 'shipmentCount' => 0], $client->createManifest(), 'empty manifest');
    same('%PDF-manifest', $client->getManifestPdf(77), 'manifest pdf');
    same(['pickupId' => 5], $client->createPickup(['date' => '2026-10-13']), 'pickup');
    same([], $client->createPickupForBranch(454, ['date' => '2026-10-13']), 'branch pickup empty');
    same(12.5, $client->quoteForBranch(454, ['shipmentType' => 'parcel'])['totalAmount'], 'quote');
    same(0, $client->getReturns(94348148)['count'], 'returns');
    $req = $s->requests();
    same('/v1/counties', $req[0]['path'], 'counties path');
    same('POST', $req[1]['method'], 'manifest method');
    same('', $req[1]['body'], 'manifest has no body');
    check(!isset($req[1]['headers']['content-type']) || $req[1]['headers']['content-type'] === '' || stripos($req[1]['headers']['content-type'], 'json') === false, 'manifest has no JSON content type');
    same('/v1/manifests/77/pdf', $req[3]['path'], 'manifest pdf path');
    same('/v1/pickups/branch', $req[5]['path'], 'branch pickup path');
    same(454, json_decode($req[5]['body'], true)['pcId'], 'pcId');
    same('/v1/awb/quote/branch', $req[6]['path'], 'quote branch path');
    same('/v1/awb/94348148/return', $req[7]['path'], 'returns path');

    same('Livrat', Status::label(Status::DELIVERED), 'label 3');
    same('In centru destinatie', Status::label(21), 'label 21');
    same(null, Status::label(99), 'unknown label');
    check(Status::isFinal(3) && Status::isFinal(7), 'final statuses');
    check(!Status::isFinal(6) && !Status::isFinal(18), 'non-final statuses');

    // network error -> DragonStarError status 0, code network_error
    $dead = new Client('k', ['baseUrl' => 'http://127.0.0.1:1', 'timeout' => 2, 'maxRetries' => 0]);
    $e = expectThrows(DragonStarError::class, function () use ($dead) {
        $dead->getBranches();
    });
    same(0, $e->getStatus(), 'network status');
    same('network_error', $e->getErrorCode(), 'network code');
};

// ---------------------------------------------------------------------------
// Run
// ---------------------------------------------------------------------------

$server = new FakeServer();
$server->start();
$failed = 0;
try {
    foreach ($tests as $name => $test) {
        try {
            $test($server);
            echo "PASS  $name\n";
        } catch (\Throwable $e) {
            $failed++;
            echo "FAIL  $name\n      " . get_class($e) . ': ' . $e->getMessage() . "\n";
        }
    }
} finally {
    $server->stop();
}

$total = count($tests);
echo "\n" . ($total - $failed) . "/$total passed\n";
exit($failed > 0 ? 1 : 0);
