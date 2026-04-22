# Authentication & Authorization

The API validates Clerk-issued JWTs server-side via `AddJwtBearer`. This is
defense-in-depth alongside the Next.js middleware: the API is correct on its
own terms and does not rely on a trusted caller to assert identity.

## Required configuration

Add to `appsettings.json` or environment variables:

```json
{
  "Clerk": {
    "Authority": "https://<your-instance>.clerk.accounts.dev",
    "AdminOrgId": "<same value as ADMIN_ORG_ID in the Next.js app>"
  }
}
```

- **`Authority`** — the Clerk instance URL that issues and signs tokens. The
  JWT handler hits `{Authority}/.well-known/openid-configuration` to discover
  the JWKS endpoint, and caches keys with automatic rotation.
- **`AdminOrgId`** — Clerk organization id whose members are treated as
  admins. Must match the value the Next.js middleware uses for its admin
  check, so the two layers agree on who is an admin.

If `Clerk:Authority` is missing, the app fails fast at startup rather than
silently running unauthenticated.

## Clerk-side setup

The JWT must include an `org_id` claim for the admin policy to work. Clerk's
default session template includes this when the user has an active organization
context. If your users don't always have an active org, create a custom JWT
template in the Clerk dashboard that emits:

```
{
  "org_id": "{{org.id}}",
  "email": "{{user.primary_email_address}}",
  "email_verified": "{{user.primary_email_address_verified}}"
}
```

and configure the Next.js app to request tokens from that template via
`getToken({ template: "<name>" })`.

## Policies

Two authorization policies, both defined in `Features/Auth/AuthPolicies.cs`:

| Policy     | Requirement                                                            | Applied to                                                                             |
| ---------- | ---------------------------------------------------------------------- | -------------------------------------------------------------------------------------- |
| `Customer` | Authenticated Clerk user                                                | `/customers/*`, `/reservations/*`                                                      |
| `Admin`    | Authenticated + `org_id` claim matches `Clerk:AdminOrgId`               | Inventory mutation, site content mutation, all AI features, image mutation             |

A fallback policy (`RequireAuthenticatedUser`) is registered globally, so any
endpoint without an explicit `[AllowAnonymous]` still requires a valid JWT.

## Anonymous endpoints

Three categories of endpoint are explicitly `[AllowAnonymous]`:

- `GET /api/health` — health probe
- `POST /square/webhook` — Square calls the API directly with no bearer token;
  request authenticity is established by HMAC signature verification inside
  the handler (tracked separately as Severe #3)
- Public storefront reads:
  - `GET /items`, `GET /items/{id}`, `GET /items/sku/{sku}`
  - `GET /items/{id}/similar`
  - `GET /items/{id}/images`
  - `GET /site/config`, `GET /site/config/{key}`, `GET /site/hero`, `GET /site/media`

## Reading identity in controllers

`ClaimsPrincipalExtensions` provides the accessors:

```csharp
var clerkUserId     = User.GetClerkUserId();     // "sub" claim
var email           = User.GetEmail();           // from JWT, trustworthy
var isEmailVerified = User.GetEmailVerified();   // from JWT, trustworthy
var orgId           = User.GetOrgId();           // for org-scoped features
```

The `X-Clerk-User-Id` header is no longer read. The Next.js frontend already
sends `Authorization: Bearer <token>` (seen in `apiCall` in `account/page.tsx`),
so no frontend change is required for the switch.

## Notable change: `/customers/sync`

Previously accepted `{ clerkUserId, email, isEmailVerified, ... }` in the
request body and trusted all of those values. The handler now derives
`clerkUserId`, `email`, and `isEmailVerified` from validated JWT claims; the
body only carries editable fields (`firstName`, `lastName`, `phone`). This
closes the vector where a caller could mark their own email verified and
immediately create a reservation.
