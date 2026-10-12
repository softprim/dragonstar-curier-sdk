<?php

/**
 * Creează un AWB, descarcă eticheta PDF și afișează costul.
 * Creates an AWB, downloads its PDF label and prints the cost.
 *
 *   DRAGONSTAR_API_KEY=cheia-ta php php/examples/create-awb.php
 */

// Cu Composer: require 'vendor/autoload.php';
require __DIR__ . '/../dragonstar.php';

use SoftPrim\DragonStar\Client;
use SoftPrim\DragonStar\DragonStarError;

$apiKey = getenv('DRAGONSTAR_API_KEY');
if (!$apiKey) {
    fwrite(STDERR, "Setează variabila de mediu DRAGONSTAR_API_KEY.\n");
    exit(1);
}

$client = new Client($apiKey);

$shipment = [
    'shipmentType' => 'parcel',
    'weight' => 1.5,
    'pieces' => 1,
    'recipientName' => 'Ion Popescu',
    'recipientPhone' => '0722123456',
    'recipientAddress' => 'Str. Memorandumului 1',
    'recipientCounty' => 'Cluj',
    'recipientCity' => 'Cluj-Napoca',
    'codAmount' => 149.90,
    'notes' => 'Comanda #1001',
];

try {
    $quote = $client->quote($shipment);
    printf("Cost estimat: %.2f RON (cu TVA)\n", $quote['totalAmount']);

    $awb = $client->createAwb($shipment);
    printf("AWB creat: %s — total %.2f RON\n", $awb['awb'], $awb['totalAmount']);

    $file = __DIR__ . '/awb-' . $awb['awb'] . '.pdf';
    file_put_contents($file, $client->getLabel($awb['awb'], 'A6'));
    echo "Eticheta salvată în $file\n";
} catch (DragonStarError $e) {
    fwrite(STDERR, sprintf("Eroare %d (%s): %s\n", $e->getStatus(), $e->getErrorCode(), $e->getMessage()));
    foreach ($e->getErrors() as $field => $messages) {
        fwrite(STDERR, "  - $field: " . implode('; ', $messages) . "\n");
    }
    exit(1);
}
