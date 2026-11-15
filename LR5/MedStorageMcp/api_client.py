"""Thin async HTTP client for the existing .NET MedStorage REST APIs (LR1 Catalog, LR3 Inventory/Alerts)."""

from __future__ import annotations

import os
import uuid
from datetime import date
from typing import Any

import httpx
from mcp.server.mcpserver.exceptions import ToolError

CATALOG_URL = os.environ.get("MEDSTORAGE_CATALOG_URL", "http://localhost:5001")
INVENTORY_URL = os.environ.get("MEDSTORAGE_INVENTORY_URL", "http://localhost:5002")
ALERTS_URL = os.environ.get("MEDSTORAGE_ALERTS_URL", "http://localhost:5003")
# Keycloak user token (catalog:read, inventory:read/write, alerts:read): the server acts as that user
ACCESS_TOKEN = os.environ.get("MEDSTORAGE_ACCESS_TOKEN", "")

# Guardrail: short timeouts, no redirects - the server only ever talks to the three configured hosts
_TIMEOUT = httpx.Timeout(10.0, connect=3.0)


class MedStorageApi:
    def __init__(self) -> None:
        headers = {"Authorization": f"Bearer {ACCESS_TOKEN}"} if ACCESS_TOKEN else {}
        self._catalog = httpx.AsyncClient(base_url=CATALOG_URL, timeout=_TIMEOUT, follow_redirects=False, headers=headers)
        self._inventory = httpx.AsyncClient(base_url=INVENTORY_URL, timeout=_TIMEOUT, follow_redirects=False, headers=headers)
        self._alerts = httpx.AsyncClient(base_url=ALERTS_URL, timeout=_TIMEOUT, follow_redirects=False, headers=headers)

    async def aclose(self) -> None:
        for client in (self._catalog, self._inventory, self._alerts):
            await client.aclose()

    async def find_drug(self, trade_name: str) -> dict[str, Any] | None:
        # params= lets httpx encode the value; arguments are never concatenated into the URL
        response = await self._send(self._catalog, "GET", "/api/v1/drugs/search", params={"name": trade_name})
        return None if response.status_code == 404 else _json(response)

    async def get_drug(self, drug_id: uuid.UUID) -> dict[str, Any] | None:
        response = await self._send(self._catalog, "GET", f"/api/v1/drugs/{drug_id}")
        return None if response.status_code == 404 else _json(response)

    async def get_kit_items(self, kit_id: uuid.UUID) -> list[dict[str, Any]]:
        return _json(await self._send(self._inventory, "GET", f"/api/v1/inventory/kits/{kit_id}/items"))

    async def add_medicine(self, kit_id: uuid.UUID, trade_name: str, quantity: int, expiry: date) -> dict[str, Any]:
        response = await self._send(
            self._inventory,
            "POST",
            f"/api/v1/inventory/kits/{kit_id}/items",
            json={"tradeName": trade_name, "quantity": quantity, "expiryDate": expiry.isoformat()},
        )
        if response.status_code in (400, 404):
            raise ToolError(f"Inventory rejected the request: {response.text[:200]}")
        return _json(response)

    async def get_expiring(self, days: int) -> list[dict[str, Any]]:
        return _json(await self._send(self._alerts, "GET", "/api/v1/alerts/expiring", params={"days": days}))

    @staticmethod
    async def _send(client: httpx.AsyncClient, method: str, url: str, **kwargs: Any) -> httpx.Response:
        try:
            return await client.request(method, url, **kwargs)
        except httpx.HTTPError as ex:
            # Internal hostnames and stack traces are not leaked to the model
            raise ToolError(f"MedStorage service is unavailable ({type(ex).__name__}).") from ex


def _json(response: httpx.Response) -> Any:
    if response.status_code >= 400:
        raise ToolError(f"MedStorage service returned HTTP {response.status_code}.")
    return response.json()
