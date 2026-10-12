"""Tests for the Dragon Star Curier Python SDK.

They never hit the real API: a local ThreadingHTTPServer replays scripted
responses and records every request it receives.
"""

import json
import os
import sys
import threading
import unittest
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Any, Callable, Dict, List, Optional, Tuple, Union

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))

from dragonstar import DragonStarClient, DragonStarError, Status, __version__  # noqa: E402
from dragonstar.status import is_final, label  # noqa: E402

Scripted = Tuple[int, Dict[str, str], Union[bytes, str, dict, None]]
Responder = Callable[[Dict[str, Any]], Scripted]


class FakeServer(object):
    def __init__(self) -> None:
        self.requests: List[Dict[str, Any]] = []
        self.queue: List[Union[Scripted, Responder]] = []
        server = self

        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *args: Any) -> None:  # silence
                pass

            def _handle(self) -> None:
                length = int(self.headers.get("Content-Length") or 0)
                body = self.rfile.read(length) if length else b""
                rec = {
                    "method": self.command,
                    "path": self.path,
                    "headers": dict(self.headers),
                    "body": body,
                }
                server.requests.append(rec)
                if server.queue:
                    item = server.queue.pop(0)
                else:
                    item = (500, {}, {"title": "no scripted response", "status": 500})
                status, headers, payload = item(rec) if callable(item) else item
                if isinstance(payload, dict):
                    data = json.dumps(payload).encode("utf-8")
                    ctype = (
                        "application/problem+json" if status >= 400 else "application/json"
                    )
                    headers = dict({"Content-Type": ctype}, **headers)
                elif isinstance(payload, str):
                    data = payload.encode("utf-8")
                elif payload is None:
                    data = b""
                else:
                    data = payload
                self.send_response(status)
                for k, v in headers.items():
                    self.send_header(k, v)
                self.send_header("Content-Length", str(len(data)))
                self.end_headers()
                if data:
                    self.wfile.write(data)

            do_GET = do_POST = do_DELETE = do_PUT = _handle

        self.httpd = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.thread = threading.Thread(
            target=self.httpd.serve_forever, kwargs={"poll_interval": 0.02}, daemon=True
        )
        self.thread.start()

    @property
    def url(self) -> str:
        return "http://127.0.0.1:{}".format(self.httpd.server_address[1])

    def stop(self) -> None:
        self.httpd.shutdown()
        self.httpd.server_close()


class ClientTestCase(unittest.TestCase):
    def setUp(self) -> None:
        self.server = FakeServer()
        self.sleeps: List[float] = []
        self.client = self.make_client()

    def tearDown(self) -> None:
        self.server.stop()

    def make_client(self, **kw: Any) -> DragonStarClient:
        c = DragonStarClient("test-key", base_url=self.server.url + "/", timeout=5, **kw)
        c._sleep = self.sleeps.append  # do not actually wait
        return c

    def reply(self, *items: Union[Scripted, Responder]) -> None:
        self.server.queue.extend(items)

    def last_json(self) -> Any:
        return json.loads(self.server.requests[-1]["body"].decode("utf-8"))

    # 1. Authorization + User-Agent ------------------------------------------
    def test_sends_auth_and_user_agent_headers(self) -> None:
        self.reply((200, {}, {"count": 1, "data": {"CJ": "Cluj"}}))
        self.assertEqual(self.client.get_counties(), {"CJ": "Cluj"})
        req = self.server.requests[0]
        self.assertEqual(req["method"], "GET")
        self.assertEqual(req["path"], "/v1/counties")
        self.assertEqual(req["headers"]["Authorization"], "Bearer test-key")
        self.assertEqual(req["headers"]["User-Agent"], "softprim-dragonstar-python/1.0.0")
        self.assertTrue(req["headers"]["Accept"].startswith("application/json"))
        self.assertEqual(__version__, "1.0.0")

    def test_custom_user_agent(self) -> None:
        client = self.make_client(user_agent="my-shop/2.0")
        self.reply((200, {}, {"masterId": 454, "count": 0, "data": []}))
        self.assertEqual(client.get_branches()["masterId"], 454)
        self.assertEqual(self.server.requests[0]["headers"]["User-Agent"], "my-shop/2.0")

    # 2. create_awb ------------------------------------------------------------
    def test_create_awb_sends_json_and_returns_awb(self) -> None:
        created = {
            "awb": 94348148,
            "totalAmount": 9.83,
            "weight": 0.5,
            "pieces": 1,
            "codAmount": 100,
            "codPaymentType": "account",
        }
        self.reply((201, {}, created))
        data = {
            "shipmentType": "parcel",
            "weight": 5,
            "pieces": 1,
            "recipientName": "Ion Popescu",
            "recipientAddress": "Str. Exemplu 1",
            "recipientCounty": "Cluj",
            "recipientCity": "Cluj-Napoca",
            "codAmount": 100,
        }
        res = self.client.create_awb(data)
        self.assertEqual(res["awb"], 94348148)
        self.assertEqual(res, created)
        req = self.server.requests[0]
        self.assertEqual(req["method"], "POST")
        self.assertEqual(req["path"], "/v1/awb")
        self.assertEqual(req["headers"]["Content-Type"], "application/json")
        self.assertEqual(self.last_json(), data)

    def test_create_awb_for_branch_adds_pc_id(self) -> None:
        self.reply((201, {}, {"awb": 1}))
        self.client.create_awb_for_branch(454, {"shipmentType": "envelope"})
        self.assertEqual(self.server.requests[0]["path"], "/v1/awb/branch")
        self.assertEqual(self.last_json(), {"shipmentType": "envelope", "pcId": 454})

    # 3. 422 -> DragonStarError ------------------------------------------------
    def test_validation_error(self) -> None:
        problem = {
            "type": "https://api.curierdragonstar.ro/errors/validation-failed",
            "title": "Validation failed",
            "status": 422,
            "code": "validation_failed",
            "detail": "The request has invalid fields.",
            "errors": {"weight": ["required if shipmentType = parcel or pallet"]},
        }
        self.reply((422, {}, problem))
        with self.assertRaises(DragonStarError) as ctx:
            self.client.create_awb({"shipmentType": "parcel"})
        err = ctx.exception
        self.assertEqual(err.status, 422)
        self.assertEqual(err.code, "validation_failed")
        self.assertEqual(err.title, "Validation failed")
        self.assertEqual(err.detail, "The request has invalid fields.")
        self.assertEqual(err.type, problem["type"])
        self.assertEqual(err.errors, problem["errors"])
        self.assertIsNone(err.retry_after)
        self.assertEqual(str(err), "The request has invalid fields.")
        self.assertEqual(len(self.server.requests), 1)

    def test_error_message_fallbacks(self) -> None:
        self.reply((404, {}, {"title": "Resource not found", "status": 404}), (500, {}, None))
        with self.assertRaises(DragonStarError) as ctx:
            self.client.track(1)
        self.assertEqual(str(ctx.exception), "Resource not found")
        self.assertIsNone(ctx.exception.code)
        with self.assertRaises(DragonStarError) as ctx:
            self.client.get_branches()
        self.assertEqual(str(ctx.exception), "HTTP 500")
        self.assertEqual(ctx.exception.status, 500)

    # 4. 429 retry --------------------------------------------------------------
    def test_429_is_retried_and_succeeds(self) -> None:
        self.reply(
            (429, {"Retry-After": "0"}, {"status": 429, "code": "rate_limit_exceeded",
                                         "title": "Too many requests"}),
            (201, {}, {"awb": 5}),
        )
        res = self.client.create_awb({"shipmentType": "envelope"})
        self.assertEqual(res["awb"], 5)
        self.assertEqual(len(self.server.requests), 2)
        self.assertEqual(self.sleeps, [0.0])

    def test_429_exhausts_retries_and_reports_retry_after(self) -> None:
        body = {"status": 429, "code": "rate_limit_exceeded", "title": "Too many requests"}
        self.reply(*[(429, {"Retry-After": "120"}, body)] * 3)
        with self.assertRaises(DragonStarError) as ctx:
            self.client.get_branches()
        self.assertEqual(ctx.exception.retry_after, 120)
        self.assertEqual(len(self.server.requests), 3)  # 1 + max_retries(2)
        self.assertEqual(self.sleeps, [60.0, 60.0])  # capped

    def test_temporarily_banned_is_not_retried(self) -> None:
        self.reply((429, {"Retry-After": "300"},
                    {"status": 429, "code": "temporarily_banned", "title": "Banned"}))
        with self.assertRaises(DragonStarError) as ctx:
            self.client.get_counties()
        self.assertEqual(ctx.exception.code, "temporarily_banned")
        self.assertEqual(len(self.server.requests), 1)

    # 5. 503 on POST not retried; GET retried ---------------------------------
    def test_post_503_is_not_retried(self) -> None:
        self.reply((503, {}, {"status": 503, "code": "service_unavailable",
                              "title": "Service unavailable"}))
        with self.assertRaises(DragonStarError) as ctx:
            self.client.create_awb({"shipmentType": "envelope"})
        self.assertEqual(ctx.exception.status, 503)
        self.assertEqual(ctx.exception.code, "service_unavailable")
        self.assertEqual(len(self.server.requests), 1)

    def test_get_503_is_retried(self) -> None:
        self.reply(
            (503, {}, {"status": 503, "code": "service_unavailable", "title": "x"}),
            (200, {}, {"status": {"statusCode": 3}}),
        )
        res = self.client.track(94348148)
        self.assertEqual(res["status"]["statusCode"], 3)
        self.assertEqual(len(self.server.requests), 2)

    def test_max_retries_zero_disables_retries(self) -> None:
        client = self.make_client(max_retries=0)
        self.reply((429, {"Retry-After": "0"}, {"status": 429, "code": "rate_limit_exceeded"}))
        with self.assertRaises(DragonStarError):
            client.get_branches()
        self.assertEqual(len(self.server.requests), 1)

    def test_network_error_wrapped(self) -> None:
        client = DragonStarClient("k", base_url="http://127.0.0.1:1", timeout=2, max_retries=0)
        with self.assertRaises(DragonStarError) as ctx:
            client.get_counties()
        self.assertEqual(ctx.exception.status, 0)
        self.assertEqual(ctx.exception.code, "network_error")

    # 6. track_many batching --------------------------------------------------
    def test_track_many_splits_into_batches(self) -> None:
        def responder(rec: Dict[str, Any]) -> Scripted:
            awbs = json.loads(rec["body"].decode("utf-8"))["awbs"]
            data = [{"awb": a, "status": {"statusCode": 6}} for a in awbs[:-1]]
            return 200, {}, {"count": len(data), "data": data, "notFound": [awbs[-1]]}

        self.reply(responder, responder)
        awbs = list(range(1000, 1150))
        res = self.client.track_many(awbs)
        self.assertEqual(len(self.server.requests), 2)
        first = json.loads(self.server.requests[0]["body"].decode("utf-8"))["awbs"]
        second = json.loads(self.server.requests[1]["body"].decode("utf-8"))["awbs"]
        self.assertEqual(first, awbs[:100])
        self.assertEqual(second, awbs[100:])
        for r in self.server.requests:
            self.assertEqual(r["path"], "/v1/awb/tracking?events=false")
        self.assertEqual(res["notFound"], [1099, 1149])
        self.assertEqual(len(res["data"]), 148)
        self.assertEqual(res["count"], 148)
        self.assertEqual(res["data"][0]["awb"], 1000)
        self.assertEqual(res["data"][-1]["awb"], 1148)

    def test_track_events_param(self) -> None:
        self.reply((200, {}, {"status": {}}), (200, {}, {"status": {}}))
        self.client.track(123)
        self.client.track("123", events=False)
        self.assertEqual(self.server.requests[0]["path"], "/v1/awb/123?events=true")
        self.assertEqual(self.server.requests[1]["path"], "/v1/awb/123?events=false")

    # 7. get_label --------------------------------------------------------------
    def test_get_label_returns_raw_bytes_and_rejects_bad_format(self) -> None:
        pdf = b"%PDF-1.4\n\x00\xff\xfe binary \r\n%%EOF"
        self.reply((200, {"Content-Type": "application/pdf"}, pdf),
                   (200, {"Content-Type": "application/pdf"}, pdf))
        self.assertEqual(self.client.get_label(94348148), pdf)
        self.assertEqual(self.client.get_label(94348148, "A6"), pdf)
        self.assertEqual(self.server.requests[0]["path"], "/v1/awb/94348148/label")
        self.assertEqual(self.server.requests[1]["path"], "/v1/awb/94348148/label/A6")
        with self.assertRaises(DragonStarError):
            self.client.get_label(94348148, "a4")
        self.assertEqual(len(self.server.requests), 2)  # no request for "a4"

    def test_get_label_zpl(self) -> None:
        self.reply((200, {"Content-Type": "application/octet-stream"}, "^XA^FDTest^FS^XZ"))
        self.assertEqual(self.client.get_label_zpl(5, 2), "^XA^FDTest^FS^XZ")
        self.assertEqual(self.server.requests[0]["path"], "/v1/awb/5/label/zpl/2")
        with self.assertRaises(DragonStarError):
            self.client.get_label_zpl(5, 0)
        self.assertEqual(len(self.server.requests), 1)

    # 8. localities with km ---------------------------------------------------
    def test_get_localities_with_km_parses(self) -> None:
        self.reply((200, {}, {"count": 3, "data": ["Cluj-Napoca:450", "Sat: Nou:12.5", "Fara"]}))
        res = self.client.get_localities_with_km("Cluj")
        self.assertEqual(res[0], {"name": "Cluj-Napoca", "km": 450})
        self.assertEqual(res[1], {"name": "Sat: Nou", "km": 12.5})
        self.assertEqual(res[2], {"name": "Fara", "km": None})

    # 9. county URL encoding --------------------------------------------------
    def test_county_is_url_encoded(self) -> None:
        self.reply((200, {}, {"count": 1, "data": ["Brăila"]}),
                   (200, {}, {"count": 0, "data": []}))
        self.assertEqual(self.client.get_localities("Brăila"), ["Brăila"])
        self.client.get_localities_with_km("Satu Mare")
        self.assertEqual(self.server.requests[0]["path"],
                         "/v1/counties/Br%C4%83ila/localities")
        self.assertEqual(self.server.requests[1]["path"],
                         "/v1/counties/Satu%20Mare/localities-km")

    # misc ----------------------------------------------------------------------
    def test_delete_and_manifest(self) -> None:
        self.reply((204, {}, None), (200, {}, None), (201, {}, {"manifestId": 77, "shipmentCount": 12}),
                   (200, {"Content-Type": "application/pdf"}, b"%PDF"))
        self.assertIsNone(self.client.delete_awb(94348148))
        self.assertEqual(self.server.requests[0]["method"], "DELETE")
        self.assertEqual(self.client.create_manifest(), {"manifestId": None, "shipmentCount": 0})
        self.assertEqual(self.server.requests[1]["body"], b"")
        self.assertEqual(self.client.create_manifest(), {"manifestId": 77, "shipmentCount": 12})
        self.assertEqual(self.client.get_manifest_pdf(77), b"%PDF")
        self.assertEqual(self.server.requests[3]["path"], "/v1/manifests/77/pdf")

    def test_pickups_and_quotes(self) -> None:
        self.reply((201, {}, {"pickupId": 12345}), (201, {}, None),
                   (200, {}, {"totalAmount": 25.41}), (200, {}, {"totalAmount": 1}),
                   (200, {}, {"count": 0, "data": []}))
        self.assertEqual(self.client.create_pickup({"weight": 1})["pickupId"], 12345)
        self.assertIsNone(self.client.create_pickup_for_branch(454, {"weight": 1}))
        self.assertEqual(self.last_json(), {"weight": 1, "pcId": 454})
        self.assertEqual(self.client.quote({"shipmentType": "parcel"})["totalAmount"], 25.41)
        self.client.quote_for_branch(9, {"shipmentType": "parcel"})
        self.assertEqual(self.server.requests[3]["path"], "/v1/awb/quote/branch")
        self.assertEqual(self.last_json()["pcId"], 9)
        self.assertEqual(self.client.get_returns(5)["count"], 0)
        self.assertEqual(self.server.requests[4]["path"], "/v1/awb/5/return")


class StatusTestCase(unittest.TestCase):
    def test_status_constants(self) -> None:
        self.assertEqual(Status.GENERATED, 1)
        self.assertEqual(Status.AT_DESTINATION_HUB, 21)
        self.assertEqual(label(3), "Livrat")
        self.assertEqual(label(Status.OUT_FOR_DELIVERY), "Spre livrare")
        self.assertIsNone(label(99))
        self.assertTrue(is_final(3))
        self.assertTrue(is_final(Status.RETURNED))
        self.assertFalse(is_final(6))
        self.assertFalse(is_final(None))


if __name__ == "__main__":
    unittest.main()
