# server/

The ASP.NET Core backend.

Built two ways:
- **Standalone Postgres-backed** for advanced mode (Docker), via
  `dotnet run` or the `dorknet-server` container.
- **In-process SQLite-backed** for the Easy desktop app, hosted as a
  library inside `easy-app/`.

## Code layout (TBC)

```
server/
├── Controllers/    HTTP endpoints, sliced by subdomain → service
├── Services/       Business logic (presence, notifications, levels, ...)
├── Hubs/           SignalR NotifyHub
├── Data/           DbContext + EntityFramework entity classes
├── Auth/           JWT issuance, Photon CustomAuth handler
├── Middleware/     SubdomainRouter, request logging, error handling
└── Program.cs      Boot: migrations, seeds, canonical overrides, DI
```

## Building

```bash
dotnet build -c Release
dotnet run                                  # localhost:5000
```

## Provider switch

The DbContext is provider-agnostic. `appsettings` picks one:

```jsonc
{
  "ConnectionStrings": {
    // Postgres for advanced mode
    "Default": "Host=localhost;Database=dorknet;Username=dorknet;Password=..."
    // OR SQLite for easy mode
    // "Default": "Data Source=dorknet.db"
  },
  "Database": {
    "Provider": "Postgres"  // or "Sqlite"
  }
}
```

Easy app boots with `Provider=Sqlite` baked in; standalone defaults to
`Provider=Postgres`.

## See also

- [docs/architecture.md](../docs/architecture.md) — request flow,
  subdomain routing, Photon relationship
- [docs/advanced-setup.md](../docs/advanced-setup.md) — production setup
- [CONTRIBUTING.md](../CONTRIBUTING.md) — style + PR rules
