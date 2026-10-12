# Dragon Star Curier API v1 — Guide

A practical integration guide for the new Dragon Star Curier (Romanian courier) API, written by
SoftPrim from the official documentation. The source of truth is the official reference:
**https://api.curierdragonstar.ro/docs?lang=en** (OpenAPI: [`openapi.v1.en.json`](openapi.v1.en.json),
snapshot taken 2026-10-12).

> Versiunea în română: [ghid-api.md](ghid-api.md)

## Migration timeline

- The legacy API receives **no further changes** and stays available **at least until 31 March 2027**.
- New features ship only in API v1 (`https://api.curierdragonstar.ro`).
- Technical questions for Dragon Star: `api@curierdragonstar.ro`.

## Getting an API key

1. Sign in to the DSC web app (**https://app.dscexpres.ro**) with the company's **master** account.
2. Enable **two-factor authentication** (Settings → Two-factor authentication). API access is only
   available to accounts protected this way.
3. Settings → API access, confirm with your authenticator code, click **Create API key**.
4. Fill in a name, an expiry date (**max 12 months**) and the **public IP addresses** of the servers
   that will call the API.
5. Copy the key — **it is shown only once**.

At most **5 active keys**. Use separate keys for test and production. Requests from an unlisted IP get
**403 `ip_not_allowed`**.

## Authentication

```
Authorization: Bearer <key>      # or
X-Api-Key: <key>
```

If both are sent, `Authorization` wins. Never ship the key to a browser or mobile app.

## Rate limits

Per API key and per route: `GET` 40/s and 1200/min, other methods 20/s and 600/min. Responses carry
`X-RateLimit-Limit` / `X-RateLimit-Remaining`. Exceeding a limit returns **429
`rate_limit_exceeded`** with `Retry-After` (1 or 60 seconds). Public routes are limited to 2/s and
30/min per IP; repeatedly probing non-existent paths leads to a temporary ban
(`temporarily_banned`: 5 min, 10 min, then 1 hour).

## Errors

[RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) problem documents (`application/problem+json`)
with a stable machine-readable `code`; branch on `code`, not on `detail`. `validation_failed`
responses include `errors`, a map of field → messages.

Codes: `unauthorized`, `ip_not_allowed`, `account_not_eligible`, `quotes_not_enabled`,
`validation_failed`, `invalid_json`, `empty_body`, `awb_not_found`, `return_not_found`,
`awb_not_deletable` (409), `manifest_not_found`, `manifest_empty`, `county_not_found`,
`endpoint_not_found`, `method_not_allowed`, `rate_limit_exceeded`, `temporarily_banned`,
`service_unavailable` (503, retryable).

Do not blindly retry `POST /v1/awb` after a 503 or a network error — you may create a duplicate
shipment. The SDKs in this repo never auto-retry POST requests for that reason.

## Conventions

ISO 8601 dates with offset; amounts are JSON numbers in RON; tracking events are newest first and
capped at 100; bulk tracking accepts up to 100 AWBs per request; counties are referenced by **name**
(`Cluj`), not by code (`CJ`), case- and diacritic-insensitive.

## Endpoints

| Method | Path | Purpose |
|---|---|---|
| `POST` | `/v1/awb` | Create a shipment (AWB) |
| `POST` | `/v1/awb/branch` | Create on behalf of branch `pcId` |
| `POST` | `/v1/awb/quote` | Price estimate (no shipment created) |
| `POST` | `/v1/awb/quote/branch` | Price estimate for a branch |
| `GET` | `/v1/awb/{awb}?events=true` | Status + tracking events |
| `POST` | `/v1/awb/tracking?events=false` | Bulk tracking, body `{"awbs": [...]}`, max 100 |
| `GET` | `/v1/awb/{awb}/return` | Return shipments linked to an AWB |
| `DELETE` | `/v1/awb/{awb}` | Delete (only before pickup / manifest / invoicing) |
| `GET` | `/v1/awb/{awb}/label[/A4\|A6]` | PDF label |
| `GET` | `/v1/awb/{awb}/label/zpl/{nb}` | ZPL label (100×150 mm, 203 DPI) for parcel `nb` |
| `POST` | `/v1/pickups` | Book a pickup |
| `POST` | `/v1/pickups/branch` | Book a pickup for a branch |
| `POST` | `/v1/manifests` | Close pending shipments into a manifest |
| `GET` | `/v1/manifests/{id}/pdf` | Manifest PDF |
| `GET` | `/v1/counties` | Counties (code → name) |
| `GET` | `/v1/counties/{county}/localities` | Localities of a county |
| `GET` | `/v1/counties/{county}/localities-km` | Localities with distance (`"Name:km"`) |
| `GET` | `/v1/branches` | Branches of the master account |

Field-level rules for shipments and pickups are listed in the Romanian guide
([§8](ghid-api.md#8-câmpurile-unui-awb), [§9](ghid-api.md#9-ridicări)) and in the OpenAPI file.

## Shipment status codes

1 Generated · 2 Exception · **3 Delivered** (final) · 4 Notified · 5 Picked up · 6 In transit ·
**7 Returned** (final) · 18 Out for delivery · 19 Redirected · 21 At destination hub.

---

Need a ready-made module (Shopify, WooCommerce, OpenCart, PrestaShop, Magento), an ERP/invoicing
integration, or updates? Write to **office@softprim.ro**.
