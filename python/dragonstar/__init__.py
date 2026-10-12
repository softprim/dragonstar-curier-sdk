"""Python SDK for the Dragon Star Curier API v1.

Copyright (c) SoftPrim Technology SRL. MIT License.
"""

__version__ = "1.0.0"

from .client import DragonStarClient  # noqa: E402
from .errors import DragonStarError  # noqa: E402
from .status import Status, is_final, label  # noqa: E402

__all__ = [
    "DragonStarClient",
    "DragonStarError",
    "Status",
    "label",
    "is_final",
    "__version__",
]
