# Deployment

## Docker Compose (recommended)

1. Clone the repository:

   ```bash
   git clone https://github.com/Unthred/arrdash-blazor.git
   cd arrdash-blazor
   ```

2. Create your compose file from the example:

   ```bash
   cp docker-compose.example.yml docker-compose.yml
   ```

3. Edit `docker-compose.yml`:
   - Set each `*_URL` to your service base URL
   - Set API keys / Plex token
   - Map a persistent config volume
   - Optionally mount host paths for disk metrics

4. Build and start:

   ```bash
   docker compose build
   docker compose up -d
   ```

5. Verify:

   ```bash
   curl -s http://127.0.0.1:7979/health
   curl -s http://127.0.0.1:7979/api/dashboard | jq '.updatedAt, (.services | length)'
   ```

Default host port is **7979** → container **8080**.

### Updating

```bash
git pull
docker compose build && docker compose up -d
```

Config in the mounted volume is preserved across rebuilds.

## Unraid

1. Build the image on the host (or push to a registry you pull from):

   ```bash
   cd /path/to/arrdash-blazor
   docker compose build
   ```

2. Copy or symlink `unraid/my-arrdash.xml` into your Unraid docker templates folder.

3. Add the container via **Docker → Add Container** from the template.

4. Set:
   - **Config** path → `/mnt/user/appdata/arrdash` (or your preference)
   - Service URLs and API keys in template variables
   - Port **7979** (or your choice)

5. First run: open the WebUI, finish any remaining keys in **Settings → API keys**, Save.

The template uses `arrdash:latest` as a local image name — build on the Unraid host or change `Repository` to your registry tag.

## Reverse proxy

ArrDash listens on HTTP inside the container. Terminate TLS at HAProxy, nginx, Traefik, or Caddy.

Example concerns:

| Topic | Recommendation |
|-------|----------------|
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
- **Disk** — config JSON only unless you mount large paths for metrics

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
| Blank panels | Settings → Services — is the source enabled? API keys tab → Test |
| Posters missing | Poster proxy logs; upstream *arr reachable from container |
| "Last refresh" stuck | Manual refresh only; or hub disconnected — hard refresh browser |
| Chaptarr errors | Chaptarr API/database health (ArrDash only displays upstream errors) |
| CPU graph flat | Wait for second metrics sample (~2s); graph fills over window minutes |

Logs:

```bash
docker logs arrdash -f
```
