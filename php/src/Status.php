<?php

/**
 * Dragon Star Curier API v1 — PHP SDK.
 *
 * @license MIT
 * @copyright SoftPrim Technology SRL (office@softprim.ro)
 */

namespace SoftPrim\DragonStar;

/**
 * Shipment status codes (statusCode in tracking responses).
 */
final class Status
{
    const GENERATED = 1;
    const EXCEPTION = 2;
    const DELIVERED = 3;
    const NOTIFIED = 4;
    const PICKED_UP = 5;
    const IN_TRANSIT = 6;
    const RETURNED = 7;
    const OUT_FOR_DELIVERY = 18;
    const REDIRECTED = 19;
    const AT_DESTINATION_HUB = 21;

    /** @var array<int, string> */
    private static $labels = [
        self::GENERATED => 'Generat',
        self::EXCEPTION => 'Exceptie',
        self::DELIVERED => 'Livrat',
        self::NOTIFIED => 'Avizat',
        self::PICKED_UP => 'Preluat',
        self::IN_TRANSIT => 'In tranzit',
        self::RETURNED => 'Returnat',
        self::OUT_FOR_DELIVERY => 'Spre livrare',
        self::REDIRECTED => 'Redirectionat',
        self::AT_DESTINATION_HUB => 'In centru destinatie',
    ];

    private function __construct()
    {
    }

    /** Romanian label for a status code, or null for an unknown code. */
    public static function label(int $code): ?string
    {
        return isset(self::$labels[$code]) ? self::$labels[$code] : null;
    }

    /** True for final statuses: 3 (Livrat) and 7 (Returnat). */
    public static function isFinal(int $code): bool
    {
        return $code === self::DELIVERED || $code === self::RETURNED;
    }

    /**
     * All known codes with their Romanian labels.
     *
     * @return array<int, string>
     */
    public static function all(): array
    {
        return self::$labels;
    }
}
