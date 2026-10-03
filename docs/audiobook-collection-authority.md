# Audiobook collection authority

This records the source-of-truth contract for #98 and #99.

## Source precedence

1. Chaptarr or AudioBookShelf supplies the upstream collection/series definition, membership, and
   completion signal.
2. AudioBookShelf supplies collection identity, item identity, availability/health, and listening
   progress.
3. ArrDash reads the external report and presents its declared state. It does not manufacture a
   required-book count, minimum collection size, or completion rule.

## Current integration boundary

ArrDash currently reads the external `last-apply-report.json`, `collections-report.json`, and
`reconciliation-report.json` files through `ChaptarrSyncStatusService`. It presents aggregate
freshness and counts only. The external hourly wrapper runs the sync and collection script; ArrDash
does not run it and must not add a competing scheduler.

## Gap found during #99 investigation

The current external script includes an explicit-scope path and also a `chaptarr-default`
heuristic path with inferred completion/minimum-size rules. That heuristic cannot be used to mark a
series complete under the clarified #98/#99 contract. The report also has no immutable
first-confirmed completion transition/time, no source-precedence field, and no structured
unavailable/conflict state suitable for a recently-completed row.

## Required canonical change before ArrDash displays #98

The ServerMaintenance implementation must emit an authoritative, versioned report containing:

- upstream authority/source and collection identity;
- declared membership and available/required counts;
- confirmed, ambiguous, unavailable, or unhealthy completion state;
- a stable first-confirmed completion timestamp that is not reset by a refresh or collection
  recreation; and
- dry-run/apply status, single-run coordination, and sanitized failure freshness.

ArrDash can then cache and render those records with an intentional artwork fallback. It must show
ambiguous/unavailable states rather than promote them to completed.

## Rollback

The ArrDash side remains read-only. Removing a later renderer falls back to the existing sync
status panel and does not mutate collections, memberships, files, or listening progress.
