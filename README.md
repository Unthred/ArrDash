# ArrDash

Blazor Server dashboard for recent *arr downloads, audiobooks, music, and Plex/Emby now playing.

## Features

- Recent TV, movies, audiobooks, and music from Sonarr, Radarr, Chaptarr, Lidarr, and AudioBookShelf
- Merged audiobook view (Chaptarr history + ABS library)
- Now playing from Plex and Emby
- Configurable layout, themes, panel colours, and kiosk mode
- Server CPU, memory, and disk metrics (Unraid host)
- Live refresh via SignalR with configurable poll interval
- Settings UI with live preview before save

## Stack

- .NET 8 / Blazor Server
- MudBlazor
- Docker

## Quick start

1. Copy the example compose file and set your service URLs and API keys:

   ```bash
   cp docker-compose.example.yml docker-compose.yml
   # edit docker-compose.yml — keys can also be saved in the Settings UI after first run
   ```

2. Build and run:

   ```bash
   docker compose build
   docker compose up -d
   ```

3. Open `http://localhost:7979`

Layout preferences and API keys are persisted under `/config` (`user-layout.json`, `service-secrets.json`).

## Development

```bash
dotnet run --project src/ArrDash/ArrDash.csproj
dotnet test tests/ArrDash.Tests/ArrDash.Tests.csproj
```

Or run tests in Docker:

```bash
docker run --rm -v "$PWD:/src" -w /src/tests/ArrDash.Tests mcr.microsoft.com/dotnet/sdk:8.0 dotnet test
```

## Unraid

See `unraid/my-arrdash.xml` for a Community Applications template stub. Point the image at your built `arrdash:latest` or a registry tag you publish.

## Configuration

| Source | Purpose |
|--------|---------|
| Environment variables | Initial service URLs and API keys |
| `/config/user-layout.json` | Layout, theme, and behaviour preferences |
| `/config/service-secrets.json` | API keys saved from the Settings UI |

Service URLs should be reachable from the container (HTTPS FQDNs or internal hostnames that resolve inside Docker).

## License

Private / personal project unless otherwise noted.
