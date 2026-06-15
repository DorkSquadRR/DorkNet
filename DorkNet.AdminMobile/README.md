# DorkNet Admin Mobile

Native Android/iOS admin app for DorkNet, built with .NET MAUI.

In the microservices deployment, point the app at the public admin host
for the stack, usually `https://admin.<domain>`. Cloudflare and
`DorkNet.Gateway` keep the public URL stable. Requests made through the
admin host are handled by the `web` service's same-origin admin route;
the `/api/admin/*` path family is also exposed by the moderation slice
for non-admin-host routing.

The browser admin page, route map, and troubleshooting flow are
documented in [`../docs/admin.md`](../docs/admin.md).

## Authentication

The app stores sensitive values in platform secure storage:

- DorkNet admin JWT from `POST /api/admin/v1/login`
- Cloudflare Access service-token client id
- Cloudflare Access service-token client secret
- Optional Cloudflare Access JWT assertion

Every API request includes:

```http
Authorization: Bearer <dorknet-admin-jwt>
CF-Access-Client-Id: <service-token-client-id>
CF-Access-Client-Secret: <service-token-client-secret>
CF-Access-Jwt-Assertion: <optional-jwt>
```

The Cloudflare headers are optional, but required when the admin host is behind Cloudflare Access.

## Build

Install the .NET MAUI workloads, then build per platform:

```powershell
dotnet workload install maui
dotnet build DorkNet.AdminMobile\DorkNet.AdminMobile.csproj -f net9.0-android36.0
dotnet build DorkNet.AdminMobile\DorkNet.AdminMobile.csproj -f net9.0-ios26.0
```

The project targets Android 16 / API 36 and iOS 26 SDK. iOS device/archive builds still require Xcode 26 on macOS.
