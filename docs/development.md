# Development

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Docker (optional, for container parity tests)

## Run locally

```bash
cd ArrDash
dotnet run --project src/ArrDash/ArrDash.csproj
```

Default URL: `http://localhost:5000` or `https://localhost:5001` (see launch output).

Set service env vars or edit `src/ArrDash/appsettings.json` for upstream URLs. For local dev without real *arr instances, panels will be empty but the app should start.

### Config path

```bash
export ARRDASH_CONFIG_PATH=/tmp/arrdash-config
mkdir -p "$ARRDASH_CONFIG_PATH"
```

## Run tests

```bash
dotnet test tests/ArrDash.Tests/ArrDash.Tests.csproj
```

Without local SDK:

```bash
docker run --rm -v "$PWD:/src" -w /src/tests/ArrDash.Tests \
  mcr.microsoft.com/dotnet/sdk:8.0 dotnet test
```

### Test coverage areas

| Area | Examples |
|------|----------|
| Settings wiring | Every `UserLayoutPreferences` property consumed |
| Theme | Cache keys, colour normalization, CSS variables |
| Filters | Recent window days vs count limits |
| Display helpers | Relative time, byte formatting |
| Behaviour | Startup page, manual refresh, service enable flags |

## Project structure

| Project | Role |
|---------|------|
| `src/ArrDash/ArrDash.csproj` | Web application |
| `tests/ArrDash.Tests/ArrDash.Tests.csproj` | xUnit tests referencing ArrDash |

Key dependencies (see `.csproj`):

- `MudBlazor`
- ASP.NET Core Blazor Server

## Build Docker image

```bash
docker compose build
# or
docker build -t arrdash:latest .
```

Publish output is a framework-dependent deployment on `mcr.microsoft.com/dotnet/aspnet:8.0`.

## Coding conventions

- **Settings** — add help text in `SettingsHelpTexts.cs`; wire preview via `ScheduleLivePreviewNow`
- **New preference** — add to `UserLayoutPreferences`, load/save in `LayoutPreferencesService`, test in `SettingsFormCoverageTests`
- **Upstream client** — extend `ArrClientBase` pattern in `Services/Clients/`
- **UI** — prefer existing shared components; panel accents from `Prefs.GetPanelAccent`

## Debugging tips

| Issue | Where to look |
|-------|----------------|
| Setting ignored | `SettingsConsumptionTests`, component `Parameter` binding |
| Theme not applying | `ThemeBuilder.GetRootStyle`, `MainLayout` theme cache |
| Stale dashboard | `MediaAggregatorService`, SignalR hub connection in `Home.razor` |
| Poster 404 | `PosterProxyService`, upstream *arr image endpoints |

Enable detailed logging in `appsettings.Development.json` if present, or:

```json
"Logging": { "LogLevel": { "Default": "Debug" } }
```

## Contributing

1. **Create an issue** on the [ArrDash project board](github-workflow.md) before coding:

   ```bash
   bash scripts/arrdash-issue-create.sh --title "[ArrDash] ..." --body "..." --label area:backend --status Ready
   ```

2. Branch from `main`: `feature/issue-<id>-short-name`
3. Implement + tests + docs + CHANGELOG
4. PR to `main` with `Closes #<id>`

See [github-workflow.md](github-workflow.md) and [documenting-changes.md](documenting-changes.md).

### Cursor agents

Open this repo root in Cursor. Rules in `.cursor/rules/` enforce issue-first development — see [AGENTS.md](../AGENTS.md).

## Release checklist

- [ ] Version/tag if publishing images
- [ ] Update docs for new settings or services
- [ ] Run full test suite
- [ ] Verify Docker build
- [ ] No secrets in committed files

## Related documentation

- [Architecture](architecture.md)
- [Configuration](configuration.md)
- [Settings reference](settings-reference.md)
