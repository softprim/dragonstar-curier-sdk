"""HTTP client for the Dragon Star Curier API v1 (stdlib only)."""

import http.client
import json
import time
import urllib.error
import urllib.parse
import urllib.request
from typing import (
    Any,
    Callable,
    Dict,
    Iterable,
    List,
    Literal,
    Mapping,
    Optional,
    TypedDict,
    Union,
)

from .errors import DragonStarError

VERSION = "1.0.0"
DEFAULT_BASE_URL = "https://api.curierdragonstar.ro"
DEFAULT_USER_AGENT = "softprim-dragonstar-python/" + VERSION
MAX_RETRY_WAIT = 60.0
TRACKING_BATCH_SIZE = 100

AwbNumber = Union[int, str]


# --------------------------------------------------------------------------
# Typed shapes (informative; plain dicts are accepted and returned)
# --------------------------------------------------------------------------

ShipmentType = Literal["envelope", "parcel", "pallet"]
CodPaymentType = Literal["cash", "bankOrder", "cheque", "account"]


class _AwbQuoteRequired(TypedDict):
    shipmentType: ShipmentType
    recipientCounty: str
    recipientCity: str


class AwbQuoteRequest(_AwbQuoteRequired, total=False):
    weight: float
    length: float
    width: float
    height: float
    codAmount: float
    codPaymentType: CodPaymentType
    insuredValue: float
    returnAwb: bool
    returnDocuments: bool
    returnPackaging: bool
    returnParcel: bool
    smsNotification: bool
    openOnDelivery: bool
    saturdayDelivery: bool
    deliverToBranch: bool
    recipientPays: bool
    reverseShipment: bool
    notes: str
    documentDetails: str


class AwbCreate(AwbQuoteRequest, total=False):
    recipientName: str
    recipientAddress: str
    recipientContact: str
    recipientPhone: str
    recipientEmail: str
    recipientPostalCode: str
    pieces: int


class AwbCreated(TypedDict, total=False):
    awb: int
    totalAmount: float
    weight: Optional[float]
    pieces: Optional[int]
    codAmount: Optional[float]
    codPaymentType: Optional[str]


class Quote(TypedDict, total=False):
    baseRate: float
    distanceCost: float
    weightCost: float
    insuranceCost: float
    isListRate: bool
    netAmount: float
    vatAmount: float
    totalAmount: float


class TrackingStatus(TypedDict, total=False):
    statusCode: Optional[int]
    description: str
    descriptionRo: str
    timestamp: Optional[str]


class TrackingEvent(TypedDict, total=False):
    statusCode: Optional[int]
    eventCode: Optional[str]
    description: str
    isException: bool
    timestamp: Optional[str]
    location: Dict[str, Optional[str]]


class Tracking(TypedDict, total=False):
    status: TrackingStatus
    events: List[TrackingEvent]


class TrackingBatch(TypedDict):
    count: int
    data: List[Dict[str, Any]]
    notFound: List[int]


class ReturnsResult(TypedDict, total=False):
    count: int
    data: List[Dict[str, Any]]


class _PickupRequired(TypedDict):
    pickupDate: str
    pickupWindowStart: int
    pickupWindowEnd: int
    weight: float


class PickupCreate(_PickupRequired, total=False):
    pickupLocation: str
    pickupAddress: str
    pickupCounty: str
    pickupCity: str
    pickupContact: str
    pickupPhone: str
    pickupEmail: str
    parcels: int
    pallets: int
    volume: float
    notes: str


class PickupCreated(TypedDict, total=False):
    pickupId: int


class ManifestCreated(TypedDict):
    manifestId: Optional[int]
    shipmentCount: int


class LocalityKm(TypedDict):
    name: str
    km: Optional[float]


class Branch(TypedDict, total=False):
    branchId: int
    name: str
    county: str
    city: str
    address: str


class BranchesResult(TypedDict, total=False):
    masterId: int
    count: int
    data: List[Branch]


# --------------------------------------------------------------------------
# Client
# --------------------------------------------------------------------------


class _Response(object):
    __slots__ = ("status", "headers", "body")

    def __init__(self, status: int, headers: Mapping[str, str], body: bytes) -> None:
        self.status = status
        self.headers = headers
        self.body = body


class DragonStarClient(object):
    """Client for the Dragon Star Curier API v1.

    Example::

        from dragonstar import DragonStarClient
        client = DragonStarClient("my-api-key")
        result = client.create_awb({...})
    """

    def __init__(
        self,
        api_key: str,
        base_url: str = DEFAULT_BASE_URL,
        timeout: float = 30,
        max_retries: int = 2,
        user_agent: Optional[str] = None,
    ) -> None:
        if not api_key:
            raise ValueError("api_key is required")
        self.api_key = api_key
        self.base_url = (base_url or DEFAULT_BASE_URL).rstrip("/")
        self.timeout = timeout
        self.max_retries = max(0, int(max_retries))
        self.user_agent = user_agent or DEFAULT_USER_AGENT
        # Injectable for tests.
        self._sleep: Callable[[float], None] = time.sleep

    # -- context manager (no persistent resources, provided for convenience)

    def __enter__(self) -> "DragonStarClient":
        return self

    def __exit__(self, *exc: Any) -> None:
        self.close()

    def close(self) -> None:
        """No-op; kept for symmetry with ``with`` usage."""

    # ------------------------------------------------------------------ AWB

    def create_awb(self, data: Mapping[str, Any]) -> AwbCreated:
        """Create a shipment (``POST /v1/awb``)."""
        return self._json("POST", "/v1/awb", body=dict(data))

    def create_awb_for_branch(self, pc_id: int, data: Mapping[str, Any]) -> AwbCreated:
        """Create a shipment for a branch (``POST /v1/awb/branch``)."""
        body = dict(data)
        body["pcId"] = pc_id
        return self._json("POST", "/v1/awb/branch", body=body)

    def quote(self, data: Mapping[str, Any]) -> Quote:
        """Price a shipment without creating it (``POST /v1/awb/quote``)."""
        return self._json("POST", "/v1/awb/quote", body=dict(data))

    def quote_for_branch(self, pc_id: int, data: Mapping[str, Any]) -> Quote:
        """Price a shipment for a branch (``POST /v1/awb/quote/branch``)."""
        body = dict(data)
        body["pcId"] = pc_id
        return self._json("POST", "/v1/awb/quote/branch", body=body)

    def track(self, awb: AwbNumber, events: bool = True) -> Tracking:
        """Track one AWB (``GET /v1/awb/{awb}``)."""
        return self._json(
            "GET", "/v1/awb/" + _seg(awb), query={"events": _bool(events)}
        )

    def track_many(self, awbs: Iterable[AwbNumber], events: bool = False) -> TrackingBatch:
        """Track many AWBs (``POST /v1/awb/tracking``).

        More than 100 AWBs are split automatically into batches of 100 and the
        results are merged.
        """
        items = [_awb_value(a) for a in awbs]
        data: List[Dict[str, Any]] = []
        not_found: List[int] = []
        for i in range(0, len(items), TRACKING_BATCH_SIZE):
            chunk = items[i : i + TRACKING_BATCH_SIZE]
            res = self._json(
                "POST",
                "/v1/awb/tracking",
                body={"awbs": chunk},
                query={"events": _bool(events)},
            ) or {}
            data.extend(res.get("data") or [])
            not_found.extend(res.get("notFound") or [])
        return {"count": len(data), "data": data, "notFound": not_found}

    def get_returns(self, awb: AwbNumber) -> ReturnsResult:
        """Return shipments linked to an AWB (``GET /v1/awb/{awb}/return``)."""
        return self._json("GET", "/v1/awb/" + _seg(awb) + "/return")

    def delete_awb(self, awb: AwbNumber) -> None:
        """Delete an AWB (``DELETE /v1/awb/{awb}``)."""
        self._request("DELETE", "/v1/awb/" + _seg(awb))

    def get_label(self, awb: AwbNumber, format: Optional[str] = None) -> bytes:
        """PDF label (``GET /v1/awb/{awb}/label[/A4|A6]``). Returns raw bytes."""
        path = "/v1/awb/" + _seg(awb) + "/label"
        if format is not None:
            if format not in ("A4", "A6"):
                raise _invalid("format must be exactly 'A4' or 'A6', got {!r}".format(format))
            path += "/" + format
        return self._request("GET", path, accept="application/pdf").body

    def get_label_zpl(self, awb: AwbNumber, parcel: int = 1) -> str:
        """ZPL label for one parcel (``GET /v1/awb/{awb}/label/zpl/{nb}``)."""
        if isinstance(parcel, bool) or not isinstance(parcel, int) or parcel < 1:
            raise _invalid("parcel must be an integer >= 1, got {!r}".format(parcel))
        res = self._request(
            "GET",
            "/v1/awb/" + _seg(awb) + "/label/zpl/" + str(parcel),
            accept="application/octet-stream",
        )
        return res.body.decode("utf-8", errors="replace")

    # -------------------------------------------------------------- pickups

    def create_pickup(self, data: Mapping[str, Any]) -> PickupCreated:
        """Order a courier pickup (``POST /v1/pickups``)."""
        return self._json("POST", "/v1/pickups", body=dict(data))

    def create_pickup_for_branch(
        self, pc_id: int, data: Mapping[str, Any]
    ) -> Optional[PickupCreated]:
        """Order a pickup for a branch (``POST /v1/pickups/branch``).

        Returns ``{"pickupId": ...}`` or ``None`` when the API sends no body.
        """
        body = dict(data)
        body["pcId"] = pc_id
        res = self._json("POST", "/v1/pickups/branch", body=body)
        return res or None

    # ------------------------------------------------------------ manifests

    def create_manifest(self) -> ManifestCreated:
        """Close the open shipments into a manifest (``POST /v1/manifests``).

        ``manifestId`` is ``None`` when there was nothing to close.
        """
        res = self._json("POST", "/v1/manifests") or {}
        return {
            "manifestId": res.get("manifestId"),
            "shipmentCount": int(res.get("shipmentCount") or 0),
        }

    def get_manifest_pdf(self, manifest_id: Union[int, str]) -> bytes:
        """Manifest PDF (``GET /v1/manifests/{id}/pdf``). Returns raw bytes."""
        return self._request(
            "GET", "/v1/manifests/" + _seg(manifest_id) + "/pdf", accept="application/pdf"
        ).body

    # ---------------------------------------------------------- nomenclature

    def get_counties(self) -> Dict[str, str]:
        """Counties as ``{code: name}``, e.g. ``{"CJ": "Cluj"}``."""
        res = self._json("GET", "/v1/counties") or {}
        return dict(res.get("data") or {})

    def get_localities(self, county: str) -> List[str]:
        """Locality names of a county (use the county *name*, e.g. ``Cluj``)."""
        res = self._json("GET", "/v1/counties/" + _seg(county) + "/localities") or {}
        return list(res.get("data") or [])

    def get_localities_with_km(self, county: str) -> List[LocalityKm]:
        """Localities with distance, parsed from ``"Name:km"`` strings."""
        res = self._json("GET", "/v1/counties/" + _seg(county) + "/localities-km") or {}
        return [_parse_locality_km(s) for s in (res.get("data") or [])]

    def get_branches(self) -> BranchesResult:
        """Branches of the account (``GET /v1/branches``)."""
        return self._json("GET", "/v1/branches")

    # ------------------------------------------------------------- internals

    def _json(
        self,
        method: str,
        path: str,
        body: Optional[Any] = None,
        query: Optional[Mapping[str, str]] = None,
    ) -> Any:
        res = self._request(method, path, body=body, query=query)
        if not res.body or not res.body.strip():
            return {}
        try:
            return json.loads(res.body.decode("utf-8"))
        except ValueError:
            raise DragonStarError(
                "Invalid JSON in response",
                status=res.status,
                code="invalid_response",
            )

    def _request(
        self,
        method: str,
        path: str,
        body: Optional[Any] = None,
        query: Optional[Mapping[str, str]] = None,
        accept: str = "application/json",
    ) -> _Response:
        url = self.base_url + path
        if query:
            url += "?" + urllib.parse.urlencode(query)
        headers = {
            "Authorization": "Bearer " + self.api_key,
            "Accept": accept + ", application/problem+json",
            "User-Agent": self.user_agent,
        }
        data: Optional[bytes] = None
        if body is not None:
            data = json.dumps(body).encode("utf-8")
            headers["Content-Type"] = "application/json"
        idempotent = method in ("GET", "DELETE")

        attempt = 0
        while True:
            req = urllib.request.Request(url, data=data, headers=headers, method=method)
            try:
                with urllib.request.urlopen(req, timeout=self.timeout) as resp:
                    return _Response(resp.status, resp.headers, resp.read())
            except urllib.error.HTTPError as e:
                try:
                    raw = e.read()
                except Exception:  # pragma: no cover
                    raw = b""
                err = _build_error(e.code, e.headers, raw)
                if attempt < self.max_retries and _should_retry(err, idempotent):
                    attempt += 1
                    self._sleep(_wait_for(err.retry_after, attempt, err.status == 429))
                    continue
                raise err
            except (urllib.error.URLError, http.client.HTTPException, OSError) as e:
                if attempt < self.max_retries and idempotent:
                    attempt += 1
                    self._sleep(_wait_for(None, attempt, False))
                    continue
                reason = getattr(e, "reason", None) or e
                err = DragonStarError(
                    "Network error: {}".format(reason), status=0, code="network_error"
                )
                raise err from e


# --------------------------------------------------------------------------
# Helpers
# --------------------------------------------------------------------------


def _seg(value: Any) -> str:
    return urllib.parse.quote(str(value), safe="")


def _bool(value: bool) -> str:
    return "true" if value else "false"


def _awb_value(awb: AwbNumber) -> Any:
    if isinstance(awb, str) and awb.strip().isdigit():
        return int(awb.strip())
    return awb


def _invalid(message: str) -> DragonStarError:
    return DragonStarError(message, status=0, code="invalid_argument")


def _parse_locality_km(item: str) -> LocalityKm:
    text = str(item)
    idx = text.rfind(":")
    if idx < 0:
        return {"name": text, "km": None}
    name, km_raw = text[:idx], text[idx + 1 :].strip()
    km: Optional[float]
    try:
        km_f = float(km_raw)
        km = int(km_f) if km_f.is_integer() else km_f
    except ValueError:
        km = None
    return {"name": name, "km": km}


def _parse_retry_after(headers: Optional[Mapping[str, str]]) -> Optional[float]:
    if headers is None:
        return None
    value = headers.get("Retry-After")
    if value is None:
        return None
    try:
        seconds = float(str(value).strip())
    except ValueError:
        return None
    return max(0.0, seconds)


def _build_error(status: int, headers: Optional[Mapping[str, str]], raw: bytes) -> DragonStarError:
    problem: Dict[str, Any] = {}
    if raw:
        try:
            parsed = json.loads(raw.decode("utf-8"))
            if isinstance(parsed, dict):
                problem = parsed
        except (ValueError, UnicodeDecodeError):
            pass
    code = problem.get("code") or None
    title = problem.get("title")
    detail = problem.get("detail")
    errors = problem.get("errors") if isinstance(problem.get("errors"), dict) else None
    message = detail or title or "HTTP {}".format(status)
    return DragonStarError(
        message,
        status=status,
        code=code,
        title=title,
        detail=detail,
        type=problem.get("type"),
        errors=errors,
        retry_after=_parse_retry_after(headers),
    )


def _should_retry(err: DragonStarError, idempotent: bool) -> bool:
    if err.status == 429:
        return err.code != "temporarily_banned"
    if err.status == 503:
        return idempotent
    return False


def _wait_for(retry_after: Optional[float], attempt: int, is_rate_limit: bool) -> float:
    if retry_after is not None:
        wait = retry_after
    elif is_rate_limit:
        wait = 1.0
    else:
        wait = 0.5 * (2 ** (attempt - 1))
    return min(MAX_RETRY_WAIT, max(0.0, wait))
