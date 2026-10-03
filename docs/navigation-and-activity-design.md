# Navigation and activity design

This document records the discovery baseline for #88, #89/#90, #91/#92, and #93. It is a
migration plan, not a claim that a new information architecture is already deployed.

## Operator tasks

| Question | Primary destination | Context preserved on return |
| --- | --- | --- |
| Is anything currently broken or blocked? | Dashboard service status, then Infrastructure warnings | selected service and warning filter |
| What is happening now? | Dashboard activity card / Watch activity overview | time range and source filter |
| What has a particular person watched? | Watch activity → Users → user detail | range, source, selected user, and return route |
| Why is a service unhealthy? | Service status detail | selected service and dashboard scroll position |
| What needs action on the host? | Server metrics detail / Infrastructure warnings | selected detail and warning category |

## Chosen interaction model

1. Keep the Dashboard as the operational landing page.
2. Use a stable primary navigation group: Dashboard, Activity, Infrastructure, Library tools,
   Settings. Existing deep routes (`/activity`, `/activity/users`, `/warnings`, `/cleanup`) remain
   valid during migration.
3. Treat activity range and source as route/query state, so a user drill-down can return to the
   same overview rather than an implicit default.
4. Make the activity card a concise linkable summary of active work, recent failure, freshness,
   and one primary drill-down; charts and long history stay on the Activity page.
5. Make Infrastructure warnings use the same page shell, loading/empty/error language, freshness
   label, severity chips, and back navigation as other operational pages.

## Migration and safeguards

- Add route/query-state helpers before changing links; preserve old routes and keyboard access.
- Move one primary destination at a time, with an explicit fallback link to the current route.
- Reuse existing `ActivityDetailDrawer` for short inspection, while the full user workflow owns a
  deep-linkable page state.
- Do not poll every card independently. Activity and warning data retain their current bounded
  cache/freshness model; #84 supplies dashboard-level coalescing.
- Validate desktop, tablet, and phone navigation after implementation. This overnight run has no
  browser automation endpoint, so visual acceptance is pending manual review.

## Acceptance checks for the follow-up implementation

- Keyboard users can reach every primary destination and return with context intact.
- A copied activity/user URL restores its range, source, and selected user.
- Empty, loading, stale, error, and authorization states are explicit.
- Warning severity/remediation remains visible without duplicating service-detail data.
