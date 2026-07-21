# Deployment

> **Security:** ArrDash has **no built-in login**. Treat the WebUI like an unauthenticated admin console — trusted LAN, or reverse-proxy authentication in front of it. Never publish port `7979` to the public internet without auth.

Use the **`main`** branch (or `ghcr.io/unthred/arrdash:latest`, which is built from `main`). Do not deploy random feature branches unless you are developing them.

## Docker Compose (recommended)

### Option A — pull the published image (fastest)

```bash
curl -fsSL -o docker-compose.yml \
  https://raw.githubusercontent.com/Unthred/ArrDash/main/docker-compose.example.yml
docker compose up -d
```

Image: `ghcr.io/unthred/arrdash:latest` (linux/amd64 + linux/arm64).

### Option B — clone and build from source

```bash
git clone https://github.com/Unthred/ArrDash.git
cd ArrDash
git checkout main
cp docker-compose.example.yml docker-compose.yml
```

Edit `docker-compose.yml`:

- Prefer leaving URLs/keys empty and using **Settings → API keys** after first start, **or** set `*_URL` / API keys in compose
- Ensure a persistent `./config:/config` volume
- To build locally: replace `image: ghcr.io/unthred/arrdash:latest` with `build: .`

```bash
docker compose up -d --build
```

### Verify

```bash
curl -s http://127.0.0.1:7979/health
curl -s http://127.0.0.1:7979/api/dashboard | jq '.updatedAt, (.services | length)'
```

Default host port is **7979** → container **8080**.

### Updating

**Image install:**

```bash
docker compose pull && docker compose up -d
```

**Source install:**

```bash
git checkout main && git pull
docker compose up -d --build
```

Config in the mounted volume is preserved across rebuilds.

### Compose files in this repo

| File | Use |
|------|-----|
| `docker-compose.example.yml` | Minimal — any Docker host |
| `docker-compose.unraid.example.yml` | Unraid host mounts (array disk metrics, parity/mover, optional docker.sock) |

## Unraid

1. Prefer the published image in the template (`ghcr.io/unthred/arrdash:latest`), **or** build on the host:

   ```bash
   cd /path/to/ArrDash
   git checkout main
   docker compose -f docker-compose.unraid.example.yml build
   ```

2. Copy or symlink `unraid/my-arrdash.xml` into your Unraid docker templates folder.

3. Add the container via **Docker → Add Container** from the template.

4. Set:
   - **Config** path → `/mnt/user/appdata/arrdash` (or your preference)
   - Service URLs and API keys in template variables (optional — Settings UI works too)
   - Port **7979** (or your choice)

5. First run: open the WebUI → welcome card → **Settings → API keys** → Save.

For compose-based Unraid installs, start from `docker-compose.unraid.example.yml`.

## Reverse proxy

ArrDash listens on HTTP inside the container. Terminate TLS at HAProxy, nginx, Traefik, or Caddy.

**Require authentication at the proxy** (basic auth, SSO, etc.) if anyone outside your trusted LAN can reach the hostname.

Example concerns:

| Topic | Recommendation |
|-------|----------------|
| Auth | Mandatory if exposed beyond trusted LAN — ArrDash has none of its own |
| WebSockets | Required for Blazor Server and SignalR — enable upgrade headers |
| SignalR hub | Path `/hubs/dashboard` must proxy WebSockets |
| Timeouts | Blazor long polling / circuits — avoid very short proxy read timeouts |

### nginx snippet

```nginx
location / {
    proxy_pass http://127.0.0.1:7979;
    proxy_http_version 1.1;
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection "upgrade";
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_read_timeout 3600s;
}
```

## Network checklist

From **inside** the running container, test upstream reachability:

```bash
docker exec -it arrdash curl -sI https://sonarr.example.com
```

If this fails but works on the host, use hostnames your Docker network can resolve (custom DNS, `extra_hosts`, or a reverse proxy FQDN).

## Resource usage

Typical footprint:

- **RAM** — ~150–300 MB (.NET + Blazor circuits per connected user)
- **CPU** — spikes during poll cycle (parallel HTTP to *arr apps)
- **Disk** — config JSON + SQLite DB unless you mount large paths for metrics

Poll interval and enabled services directly affect load.

## Health monitoring

| Endpoint | Expected |
|----------|----------|
| `GET /health` | `{"status":"ok","app":"arrdash"}` |
| `GET /api/dashboard` | JSON snapshot with `updatedAt` advancing on each poll |

Use **Manual refresh only** in Settings if you want to eliminate background traffic (wall display with explicit refresh).

## Troubleshooting

| Symptom | Check |
|---------|-------|
| Cannot pull `ghcr.io/unthred/arrdash` | Package may still be private — use Option B (build from source), or ask the maintainer to set the GHCR package to Public |
| Blank panels | Settings → Services — is the source enabled? API keys tab → Test |
| Posters missing | Poster proxy logs; upstream *arr reachable from container |
| "Last refresh" stuck | Manual refresh only; or hub disconnected — hard refresh browser |
| Chaptarr errors | Chaptarr API/database health (ArrDash only displays upstream errors) |
| CPU graph flat | Wait for second metrics sample (~2s); graph fills over window minutes |

Logs:

```bash
docker logs arrdash -f
```
