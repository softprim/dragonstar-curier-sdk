"""Creează un AWB, descarcă eticheta PDF și afișează costul.

Rulare:
    DRAGONSTAR_API_KEY=cheia-ta python examples/create_awb.py
"""

import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))

from dragonstar import DragonStarClient, DragonStarError  # noqa: E402


def main() -> int:
    api_key = os.environ.get("DRAGONSTAR_API_KEY")
    if not api_key:
        print("Setează variabila de mediu DRAGONSTAR_API_KEY.", file=sys.stderr)
        return 1

    client = DragonStarClient(api_key)
    shipment = {
        "shipmentType": "parcel",
        "weight": 2,
        "pieces": 1,
        "recipientName": "Ion Popescu",
        "recipientPhone": "0700000000",
        "recipientAddress": "Str. Memorandumului 1",
        "recipientCounty": "Cluj",
        "recipientCity": "Cluj-Napoca",
        "codAmount": 150,
        "codPaymentType": "cash",
        "notes": "Comanda #1001",
    }

    try:
        quote = client.quote(shipment)
        print("Cost estimat: {} RON (cu TVA)".format(quote.get("totalAmount")))

        created = client.create_awb(shipment)
        awb = created["awb"]
        print("AWB creat: {} — total {} RON".format(awb, created.get("totalAmount")))

        pdf = client.get_label(awb, "A6")
        path = "awb-{}.pdf".format(awb)
        with open(path, "wb") as fh:
            fh.write(pdf)
        print("Eticheta salvată în", path)
    except DragonStarError as e:
        print("Eroare {} ({}): {}".format(e.status, e.code, e), file=sys.stderr)
        for field, messages in e.errors.items():
            print("  {}: {}".format(field, "; ".join(messages)), file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
