<?php

/**
 * Urmărește unul sau mai multe AWB-uri.
 * Tracks one or more AWBs.
 *
 *   DRAGONSTAR_API_KEY=cheia-ta php php/examples/track.php 94348148 [94348149 ...]
 */

// Cu Composer: require 'vendor/autoload.php';
require __DIR__ . '/../dragonstar.php';

use SoftPrim\DragonStar\Client;
use SoftPrim\DragonStar\DragonStarError;
use SoftPrim\DragonStar\Status;

$apiKey = getenv('DRAGONSTAR_API_KEY');
if (!$apiKey) {
    fwrite(STDERR, "Setează variabila de mediu DRAGONSTAR_API_KEY.\n");
    exit(1);
}
$awbs = array_slice($argv, 1);
if (count($awbs) === 0) {
    fwrite(STDERR, "Utilizare: php track.php <awb> [<awb> ...]\n");
    exit(1);
}

$client = new Client($apiKey);

try {
    if (count($awbs) === 1) {
        $result = $client->track($awbs[0]);
        $code = (int) $result['status']['statusCode'];
        printf("AWB %s: %s%s\n", $awbs[0], Status::label($code) ?: $result['status']['description'], Status::isFinal($code) ? ' (final)' : '');
        foreach (isset($result['events']) ? $result['events'] : [] as $event) {
            printf("  %s  %s\n", $event['timestamp'], $event['description']);
        }
    } else {
        $result = $client->trackMany($awbs); // peste 100 AWB-uri: loturi automate
        foreach ($result['data'] as $row) {
            $code = (int) $row['status']['statusCode'];
            printf("AWB %s: %s\n", $row['awb'], Status::label($code) ?: $row['status']['description']);
        }
        foreach ($result['notFound'] as $awb) {
            printf("AWB %s: negăsit\n", $awb);
        }
    }
} catch (DragonStarError $e) {
    fwrite(STDERR, sprintf("Eroare %d (%s): %s\n", $e->getStatus(), $e->getErrorCode(), $e->getMessage()));
    exit(1);
}
