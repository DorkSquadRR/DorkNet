# docker/

The Docker Compose stack for advanced mode. See
[docs/advanced-setup.md](../docs/advanced-setup.md) for the full
walkthrough.

```
docker/
├── docker-compose.yml         Postgres + Redis + server + nginx
├── docker-compose.cloudflared.yml  Optional: replace nginx with CF Tunnel
├── Dockerfile.server          Multi-stage: SDK build → admin-ui build → runtime
├── nginx.conf                 Subdomain routing for *.your-domain
└── .env.example               Required environment variables
```

Quick start:

```bash
cp .env.example .env
$EDITOR .env
docker compose up -d
```
