"""Shipment status codes returned by the Dragon Star Curier API."""

from enum import IntEnum
from typing import Dict, Optional, Union


class Status(IntEnum):
    GENERATED = 1
    EXCEPTION = 2
    DELIVERED = 3
    NOTIFIED = 4
    PICKED_UP = 5
    IN_TRANSIT = 6
    RETURNED = 7
    OUT_FOR_DELIVERY = 18
    REDIRECTED = 19
    AT_DESTINATION_HUB = 21


_LABELS: Dict[int, str] = {
    1: "Generat",
    2: "Exceptie",
    3: "Livrat",
    4: "Avizat",
    5: "Preluat",
    6: "In tranzit",
    7: "Returnat",
    18: "Spre livrare",
    19: "Redirectionat",
    21: "In centru destinatie",
}

_FINAL = frozenset({3, 7})


def label(code: Optional[Union[int, Status]]) -> Optional[str]:
    """Romanian label for a status code, or ``None`` for unknown codes."""
    if code is None:
        return None
    try:
        return _LABELS.get(int(code))
    except (TypeError, ValueError):
        return None


def is_final(code: Optional[Union[int, Status]]) -> bool:
    """True for final statuses: 3 (Livrat) and 7 (Returnat)."""
    if code is None:
        return False
    try:
        return int(code) in _FINAL
    except (TypeError, ValueError):
        return False
