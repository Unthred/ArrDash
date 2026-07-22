# GitHub workflow

Repository: [Unthred/ArrDash](https://github.com/Unthred/ArrDash)

## Rule: no work without an issue

Every change — features, bugfixes, refactors, infra, docs that describe behaviour — starts as a **GitHub issue** on the **ArrDash** project board **before** code is written.

Exceptions (user must say so explicitly):

- Single-line typo in docs with no behaviour impact
- Direct user instruction to skip tracking for a one-off chore

## Project board: ArrDash

| Status | Meaning |
|--------|---------|
| **Todo** | Scoped issue; ready to start |
| **In Progress** | Active development / open PR |
| **Done** | Merged, verified, issue closed |

Board: https://github.com/users/Unthred/projects/6 (**ArrDash**)

### Rules

1. **No code without an issue** on the board.
2. **Always** create issues with `scripts/arrdash-issue-create.sh` — not bare `gh issue create`.
3. PR description includes `Closes #N`.
4. Update **CHANGELOG.md** and **docs/** as part of the change ([documenting-changes.md](documenting-changes.md)).
5. Run unit tests before PR (`dotnet test`).

## Issue template

Use **ArrDash change** when creating issues in the GitHub UI (`.github/ISSUE_TEMPLATE/arrdash_change.yml`).

## Create issues (CLI)

From the repo root:

```bash
bash scripts/arrdash-issue-create.sh \
  --title "[ArrDash] Short title" \
  --body "$(cat <<'EOF'
## What
...

## Why
...

## Test plan
- [ ] dotnet test
- [ ] docker compose build && up -d
EOF
)" \
  --label area:ui --label risk:low --label needs-docker-test \
  --status Todo
```

Add existing issues to the board (one run, many numbers — cheaper on GraphQL):

```bash
bash scripts/arrdash-issue-create.sh --add 5 6 7 --status "In Progress"
```

### Project board + GraphQL rate limits

Board APIs use GitHub **GraphQL** (5 000 points/hour). The create script now caches project metadata once per run; still:

1. Prefer **one** `--add` with many issue numbers over many separate invocations.
2. Do **not** bulk `--add … --status Done` after merge if you can use a project automation instead: **when issue is closed → set Status to Done** (Project → ⋯ → Workflows). Then `Closes #N` on merge is enough.
3. Move to **In Progress** when you start / open the PR — not on every micro-commit.
4. If the script exits with “GraphQL budget too low”, wait for the hourly reset (`gh api rate_limit --jq '.resources.graphql'`).

## Typical flow

1. **Create issue** → card on board → **Todo**
2. **Branch** `feature/issue-5-short-name` from `main`
3. **Implement** + tests + docs + CHANGELOG → move to **In Progress** (once)
4. **PR** to `main` with `Closes #N`
5. **Merge** → issue closes → board **Done** via automation (or one manual drag)

## Labels

| Label | Use |
|-------|-----|
| `area:ui` | Blazor components, CSS |
| `area:backend` | Services, clients, API |
| `area:settings` | Settings page, preferences |
| `area:tests` | Unit tests |
| `area:docs` | Documentation only |
| `area:infra` | Docker, compose, Unraid template |
| `risk:low` / `risk:high` | Blast radius |
| `needs-docker-test` | Verify in container after merge |

## Branches

| Branch | Purpose |
|--------|---------|
| `main` | Default; deploy from here |
| `feature/issue-<id>-*` | Feature work (PR → `main`) |

## Commits

When tied to an issue:

```
#12: Show refresh age in seconds on hero strip
```

## PR checklist

See [.github/pull_request_template.md](../.github/pull_request_template.md).

## One-time project setup

See [github-project-setup.md](github-project-setup.md).

## Cursor / agent rules

Agents read `.cursor/rules/` in this repo:

| Rule | Purpose |
|------|---------|
| `work-tracking.mdc` | Issues, branches, commits |
| `arrdash-development.mdc` | Code conventions, tests |
| `documentation.mdc` | Doc update expectations |
| `agent-workflow.mdc` | When to create issues |

Open the **ArrDash** folder as the Cursor workspace (or ensure rules are synced) so agents load these rules.

## Related

- [Development](development.md)
- [Documenting changes](documenting-changes.md)
