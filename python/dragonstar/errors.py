"""Exceptions raised by the Dragon Star Curier SDK."""

from typing import Dict, List, Optional


class DragonStarError(Exception):
    """Raised for any API response with status >= 400, for network errors
    (``status == 0``, ``code == "network_error"``) and for invalid arguments
    rejected locally before a request is sent (``status == 0``,
    ``code == "invalid_argument"``).

    Attributes:
        status: HTTP status code (0 for network / local errors).
        code: stable machine-readable code from the problem+json body
            (e.g. ``validation_failed``), or ``None`` when missing.
        title: ``title`` from problem+json.
        detail: ``detail`` from problem+json.
        type: ``type`` from problem+json.
        errors: field -> list of messages (only for ``validation_failed``).
        retry_after: value of the ``Retry-After`` header in seconds, if any.
    """

    def __init__(
        self,
        message: str,
        status: int = 0,
        code: Optional[str] = None,
        title: Optional[str] = None,
        detail: Optional[str] = None,
        type: Optional[str] = None,
        errors: Optional[Dict[str, List[str]]] = None,
        retry_after: Optional[float] = None,
    ) -> None:
        super().__init__(message)
        self.message = message
        self.status = status
        self.code = code
        self.title = title
        self.detail = detail
        self.type = type
        self.errors: Dict[str, List[str]] = errors or {}
        self.retry_after = retry_after

    def __repr__(self) -> str:
        return "DragonStarError(status={!r}, code={!r}, message={!r})".format(
            self.status, self.code, self.message
        )
