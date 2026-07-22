#!/bin/bash
# Create a GitHub issue on Unthred/ArrDash and add it to the ArrDash project board.
# Also supports adding existing issues: arrdash-issue-create.sh --add 10
#
# GraphQL thrift: project metadata (id, Status field, option ids) is fetched ONCE per
# run. Bulk --add reuses that cache. Mutations cost ~5 points each; avoid re-running
# --add just to flip Status when a project automation already covers it (e.g. closed → Done).
set -euo pipefail

REPO="Unthred/ArrDash"
PROJECT_OWNER="@me"
PROJECT_TITLE="ArrDash"
GH_BIN="${GH_BIN:-/tmp/gh}"
DEFAULT_STATUS="Todo"
# Abort bulk board work when GraphQL remaining is below this (leave headroom for other tools).
MIN_GRAPHQL_REMAINING="${MIN_GRAPHQL_REMAINING:-80}"

log() { echo "[arrdash-issue-create] $*"; }

usage() {
  cat <<EOF
Usage:
  arrdash-issue-create.sh --title TITLE --body BODY [--label LABEL]... [--status STATUS]
  arrdash-issue-create.sh --add ISSUE_NUM [ISSUE_NUM...] [--status STATUS]

Creates issues on Unthred/ArrDash and adds them to the ArrDash project board.
Default status: Todo. Board statuses (exact spelling): Todo, In Progress, Done.

Examples:
  arrdash-issue-create.sh --title "Fix status bar wrap" --body "..." --label area:ui --label risk:low
  arrdash-issue-create.sh --add 3 --status "In Progress"

GraphQL tip: prefer one --add with many issue numbers over many separate runs.
Do not bulk-set Done after merge if the project has an automation for closed → Done.
EOF
}

ensure_auth() {
  if [[ -z "${GH_TOKEN:-}" ]] && [[ -f /boot/config/scripts/github-ha-project.token ]]; then
    GH_TOKEN=$(tr -d "\r\n" < /boot/config/scripts/github-ha-project.token)
    export GH_TOKEN
  fi
  if [[ -z "${GH_TOKEN:-}" ]]; then
    log "ERROR: Set GH_TOKEN or create /boot/config/scripts/github-ha-project.token"
    exit 1
  fi
}

gh_cmd() { "$GH_BIN" "$@"; }

# Cached once per process
PROJECT_NUM=""
PROJECT_ID=""
STATUS_FIELD_ID=""
declare -A STATUS_OPTION_IDS=()
declare -A ISSUE_ITEM_IDS=()
ITEM_INDEX_LOADED=false

graphql_remaining() {
  gh_cmd api rate_limit --jq ".resources.graphql.remaining" 2>/dev/null || echo ""
}

require_graphql_budget() {
  local need="${1:-1}"
  local rem
  rem=$(graphql_remaining)
  if [[ -z "$rem" || "$rem" == "null" ]]; then
    return 0
  fi
  if (( rem < MIN_GRAPHQL_REMAINING )); then
    log "ERROR: GraphQL budget too low (remaining=$rem, need≥$MIN_GRAPHQL_REMAINING). Wait for the hourly reset, then retry."
    log "        Check: gh api rate_limit --jq '.resources.graphql'"
    exit 1
  fi
  if (( rem < need * 15 )); then
    log "WARN: GraphQL remaining=$rem — bulk of $need items may not finish; will stop early if budget runs out."
  fi
}

load_project_meta() {
  if [[ -n "$PROJECT_NUM" && -n "$PROJECT_ID" && -n "$STATUS_FIELD_ID" ]]; then
    return 0
  fi

  require_graphql_budget 1

  local list_json fields_json
  list_json=$(gh_cmd project list --owner "$PROJECT_OWNER" --format json)
  PROJECT_NUM=$(jq -r --arg t "$PROJECT_TITLE" '.projects[] | select(.title==$t) | .number' <<<"$list_json")
  PROJECT_ID=$(jq -r --arg t "$PROJECT_TITLE" '.projects[] | select(.title==$t) | .id' <<<"$list_json")
  if [[ -z "$PROJECT_NUM" || "$PROJECT_NUM" == "null" || -z "$PROJECT_ID" || "$PROJECT_ID" == "null" ]]; then
    log "ERROR: Project '$PROJECT_TITLE' not found. Run scripts/setup-github-arrdash-project.sh first."
    exit 1
  fi

  fields_json=$(gh_cmd project field-list "$PROJECT_NUM" --owner "$PROJECT_OWNER" --format json)
  STATUS_FIELD_ID=$(jq -r '.fields[] | select(.name=="Status") | .id' <<<"$fields_json")
  while IFS=$'\t' read -r name oid; do
    [[ -n "$name" && -n "$oid" && "$oid" != "null" ]] || continue
    STATUS_OPTION_IDS["$name"]="$oid"
  done < <(jq -r '.fields[] | select(.name=="Status") | .options[] | "\(.name)\t\(.id)"' <<<"$fields_json")

  log "Cached project #$PROJECT_NUM (Status options: ${!STATUS_OPTION_IDS[*]})"
}

load_item_index() {
  if $ITEM_INDEX_LOADED; then
    return 0
  fi
  require_graphql_budget 1
  local items_json
  items_json=$(gh_cmd project item-list "$PROJECT_NUM" --owner "$PROJECT_OWNER" --format json --limit 500)
  while IFS=$'\t' read -r num iid; do
    [[ -n "$num" && -n "$iid" ]] || continue
    ISSUE_ITEM_IDS["$num"]="$iid"
  done < <(jq -r '
    .items[]
    | select(.content.url != null)
    | select(.content.url | test("/issues/[0-9]+$"))
    | [(.content.url | capture("/issues/(?<n>[0-9]+)$").n), .id]
    | @tsv
  ' <<<"$items_json")
  ITEM_INDEX_LOADED=true
  log "Indexed ${#ISSUE_ITEM_IDS[@]} existing board items"
}

resolve_status_option() {
  local status="$1"
  local oid="${STATUS_OPTION_IDS[$status]:-}"
  if [[ -n "$oid" ]]; then
    echo "$oid"
    return 0
  fi
  if [[ "$status" == "In progress" ]]; then
    echo "${STATUS_OPTION_IDS[In Progress]:-}"
    return 0
  fi
  echo ""
}

add_issue_to_board() {
  local issue_num="$1"
  local status="${2:-$DEFAULT_STATUS}"
  local url="https://github.com/$REPO/issues/$issue_num"
  local item_id option_id rem

  rem=$(graphql_remaining)
  if [[ -n "$rem" && "$rem" != "null" ]] && (( rem < MIN_GRAPHQL_REMAINING )); then
    log "ERROR: Stopping before #$issue_num — GraphQL remaining=$rem"
    exit 1
  fi

  load_project_meta

  log "Add #$issue_num to project board"
  item_id="${ISSUE_ITEM_IDS[$issue_num]:-}"

  if [[ -z "$item_id" ]]; then
    if item_id=$(gh_cmd project item-add "$PROJECT_NUM" --owner "$PROJECT_OWNER" --url "$url" --format json 2>/dev/null | jq -r ".id"); then
      ISSUE_ITEM_IDS["$issue_num"]="$item_id"
    else
      load_item_index
      item_id="${ISSUE_ITEM_IDS[$issue_num]:-}"
      if [[ -z "$item_id" || "$item_id" == "null" ]]; then
        log "ERROR: Could not add or find issue #$issue_num on the board"
        exit 1
      fi
    fi
  fi

  option_id=$(resolve_status_option "$status")
  if [[ -z "$option_id" || "$option_id" == "null" ]]; then
    log "WARN: Unknown status '$status' — card present but status not set (valid: ${!STATUS_OPTION_IDS[*]})"
    return 0
  fi

  gh_cmd project item-edit \
    --id "$item_id" \
    --project-id "$PROJECT_ID" \
    --field-id "$STATUS_FIELD_ID" \
    --single-select-option-id "$option_id" >/dev/null
  log "Issue #$issue_num on board (Status: $status) — $url"
}

TITLE=""
BODY=""
STATUS="$DEFAULT_STATUS"
LABELS=()
ADD_MODE=false
ADD_NUMS=()

while [[ $# -gt 0 ]]; do
  case "$1" in
    --title) TITLE="$2"; shift 2 ;;
    --body) BODY="$2"; shift 2 ;;
    --body-file) BODY="$(cat "$2")"; shift 2 ;;
    --label) LABELS+=("$2"); shift 2 ;;
    --status) STATUS="$2"; shift 2 ;;
    --add) ADD_MODE=true; shift; while [[ $# -gt 0 && "$1" != --* ]]; do ADD_NUMS+=("$1"); shift; done ;;
    -h|--help) usage; exit 0 ;;
    *) log "Unknown argument: $1"; usage; exit 1 ;;
  esac
done

ensure_auth

if [[ ! -x "$GH_BIN" ]]; then
  log "ERROR: gh not found at $GH_BIN (run scripts/setup-github-arrdash-project.sh)"
  exit 1
fi

if $ADD_MODE; then
  if [[ ${#ADD_NUMS[@]} -eq 0 ]]; then
    log "ERROR: --add requires at least one issue number"
    exit 1
  fi
  require_graphql_budget "${#ADD_NUMS[@]}"
  load_project_meta
  if (( ${#ADD_NUMS[@]} > 1 )); then
    load_item_index
  fi
  for num in "${ADD_NUMS[@]}"; do
    add_issue_to_board "$num" "$STATUS"
  done
  rem=$(graphql_remaining)
  [[ -n "$rem" && "$rem" != "null" ]] && log "GraphQL remaining after run: $rem"
  exit 0
fi

if [[ -z "$TITLE" || -z "$BODY" ]]; then
  log "ERROR: --title and --body are required (or use --add)"
  usage
  exit 1
fi

CREATE_ARGS=(issue create --repo "$REPO" --title "$TITLE" --body "$BODY")
for label in "${LABELS[@]}"; do
  CREATE_ARGS+=(--label "$label")
done

require_graphql_budget 1
ISSUE_URL=$(gh_cmd "${CREATE_ARGS[@]}")
ISSUE_NUM="${ISSUE_URL##*/}"
log "Created issue #$ISSUE_NUM — $ISSUE_URL"
add_issue_to_board "$ISSUE_NUM" "$STATUS"
