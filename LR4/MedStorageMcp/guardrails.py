"""Guardrails for the MedStorage MCP server (LR4, Task 2).

Every tool argument comes from an LLM, so it is treated as untrusted input:
it is normalized, checked against an allowlist and only then used to build a REST call.
Responses are sanitized too, because data stored in the DB (e.g. a drug name typed by a user)
reaches the model and could carry an indirect prompt injection.
"""

from __future__ import annotations

import os
import re
import time
import unicodedata
import uuid
from collections import deque
from datetime import date, timedelta

from mcp.server.mcpserver.exceptions import ToolError

MAX_QUANTITY = 100
MAX_SHELF_LIFE_YEARS = 10
MAX_TEXT_LEN = 120

# Letters (Latin/Cyrillic), digits, space and a few punctuation marks used in drug names.
# Characters that could break out of a URL, a query or a prompt (& ? # / \ < > { } " ; etc.) are not allowed.
_TRADE_NAME = re.compile(r"^[^\W_][\w \-.,'()+%]{0,79}$")
_ZERO_WIDTH = dict.fromkeys(map(ord, "​‌‍⁠﻿"))
_INJECTION_HINTS = re.compile(
    r"ignore\s+(all\s+)?(previous|prior|above)\s+instructions|system\s+prompt|you\s+are\s+now"
    r"|ігноруй|забудь\s+(всі|попередні)|<\|?im_(start|end)\|?>",
    re.IGNORECASE,
)


def _normalize(value: str) -> str:
    """NFKC folds look-alike characters (full-width letters etc.); zero-width and control chars are dropped."""
    value = unicodedata.normalize("NFKC", value).translate(_ZERO_WIDTH)
    return "".join(ch for ch in value if unicodedata.category(ch) != "Cc").strip()


def sanitize_trade_name(value: str) -> str:
    name = _normalize(value)
    if not _TRADE_NAME.fullmatch(name):
        raise ToolError("Invalid trade_name: use 1-80 letters, digits, spaces or - . , ' ( ) + %.")
    if _INJECTION_HINTS.search(name):
        raise ToolError("trade_name looks like an instruction, not a drug name. Rejected.")
    return name


def _allowed_kits() -> set[uuid.UUID]:
    raw = os.environ.get("MEDSTORAGE_ALLOWED_KITS", "")
    return {uuid.UUID(k.strip()) for k in raw.split(",") if k.strip()}


def validate_kit_id(value: str) -> uuid.UUID:
    """Authorization guardrail: the model may only touch kits listed in MEDSTORAGE_ALLOWED_KITS."""
    try:
        kit_id = uuid.UUID(_normalize(value))
    except ValueError:
        raise ToolError("Invalid kit_id: expected a UUID.") from None
    allowed = _allowed_kits()
    if allowed and kit_id not in allowed:
        raise ToolError("Access to this kit is not allowed for this MCP server.")
    return kit_id


def validate_quantity(value: int) -> int:
    # The JSON schema already limits the range (Field ge/le); this is the server-side double check
    if not 1 <= value <= MAX_QUANTITY:
        raise ToolError(f"Invalid quantity: must be between 1 and {MAX_QUANTITY}.")
    return value


def validate_expiry_date(value: date) -> date:
    if value > date.today() + timedelta(days=365 * MAX_SHELF_LIFE_YEARS):
        raise ToolError(f"expiry_date is more than {MAX_SHELF_LIFE_YEARS} years ahead, probably a typo.")
    return value


def sanitize_output(value: object) -> object:
    """Recursively truncates and cleans strings coming back from the REST API before the model sees them."""
    match value:
        case str():
            text = _normalize(value)
            return text if len(text) <= MAX_TEXT_LEN else text[:MAX_TEXT_LEN] + "..."
        case dict():
            return {k: sanitize_output(v) for k, v in value.items()}
        case list():
            return [sanitize_output(v) for v in value]
        case _:
            return value


class RateLimiter:
    """Sliding-window limit on tool calls, so a looping agent cannot flood the backend."""

    def __init__(self, max_calls: int, per_seconds: float) -> None:
        self._max_calls = max_calls
        self._per_seconds = per_seconds
        self._calls: deque[float] = deque()

    def check(self) -> None:
        now = time.monotonic()
        while self._calls and now - self._calls[0] > self._per_seconds:
            self._calls.popleft()
        if len(self._calls) >= self._max_calls:
            raise ToolError("Rate limit exceeded: too many tool calls, slow down.")
        self._calls.append(now)
