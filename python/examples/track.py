"""Urmărește unul sau mai multe AWB-uri.

Rulare:
    DRAGONSTAR_API_KEY=cheia-ta python examples/track.py 94348148 94348149
"""

import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))

from dragonstar import DragonStarClient, DragonStarError  # noqa: E402
from dragonstar.status import is_final, label  # noqa: E402


def main(argv: list) -> int:
    api_key = os.environ.get("DRAGONSTAR_API_KEY")
    if not api_key:
        print("Setează variabila de mediu DRAGONSTAR_API_KEY.", file=sys.stderr)
        return 1
    if not argv:
        print("Utilizare: python examples/track.py AWB [AWB ...]", file=sys.stderr)
        return 1

    client = DragonStarClient(api_key)
    try:
        if len(argv) == 1:
            result = client.track(argv[0], events=True)
            status = result.get("status") or {}
            code = status.get("statusCode")
            print("AWB {}: {} (final: {})".format(argv[0], label(code), is_final(code)))
            for ev in result.get("events") or []:
                loc = (ev.get("location") or {}).get("name") or ""
                print("  {}  {}  {}".format(ev.get("timestamp"), ev.get("description"), loc))
        else:
            batch = client.track_many(argv)
            for item in batch["data"]:
                code = (item.get("status") or {}).get("statusCode")
                print("AWB {}: {}".format(item.get("awb"), label(code)))
            if batch["notFound"]:
                print("Negăsite:", ", ".join(str(a) for a in batch["notFound"]))
    except DragonStarError as e:
        print("Eroare {} ({}): {}".format(e.status, e.code, e), file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
