# 01 — Git and PR state

Generated: 2026-07-29. Verified with `git` + `gh` (not prior chat claims).

## Summary table

| Repo | Branch / tip | HEAD | PR | Base | Draft | Clean | CI checks | Stacking |
|------|--------------|------|----|------|-------|-------|-----------|----------|
| detector | detached @ spike tip | `47366e2` | [#7](https://github.com/DanielJanda/cableguard-detector/pull/7) | main | yes | clean | none reported | #6 ⊂ #7; #5 **not** ⊂ #7 |
| detector | `perf/latest-frame…` | `8e42950` | [#5](https://github.com/DanielJanda/cableguard-detector/pull/5) | main | yes | n/a | none | parallel to #7 |
| detector | `perf/lightweight…` | `91e6363` | [#6](https://github.com/DanielJanda/cableguard-detector/pull/6) | main | yes | n/a | none | ancestor of #7 |
| detector | `origin/main` | `a355599` | — | — | — | — | — | baseline without full PyAV spike |
| platform | `feat/multi-camera…` | `b4988de` | [#35](https://github.com/DanielJanda/cableguard-platform/pull/35) | main | yes | dirty: untracked `*.bak` | none | **not** stacked on #34 |
| platform | `feat/control-center…` | `afed13e` | [#34](https://github.com/DanielJanda/cableguard-platform/pull/34) | main | yes | n/a | none | **#32 ⊂ #33 ⊂ #34** |
| platform | `feat/incident…` | `8c6889b` | [#33](https://github.com/DanielJanda/cableguard-platform/pull/33) | main | yes | n/a | none | contains #32 |
| platform | `feat/pyav-detector…` | `c19bfdf` | [#32](https://github.com/DanielJanda/cableguard-platform/pull/32) | main | yes | n/a | none | base of stack |
| platform | `feature/camera-stream…` | `21ac196` | [#26](https://github.com/DanielJanda/cableguard-platform/pull/26) | main | **no** | n/a | none | independent |
| platform | `origin/main` | `e07adc0` | — | — | — | — | — | Merge #30 |
| monitor | `feat/operator-navigation…` | `f9b4af8` | [#13](https://github.com/DanielJanda/cableguard-monitor/pull/13) | main | yes | clean | none | Gate 1+2 on one PR |
| monitor | `origin/main` | `d1a5b19` | — | — | — | — | — | pre–Gate 1 shell |

## Detector details

- Working tree: **detached HEAD** at `47366e2` (= PR #7 tip), clean.
- Stashes present: `diag/frame-lineage…`, `feat/production-monitor…` (historical WIP).
- PR #7 additions ~4303 — large; includes PyAV + audits + display fixes.
- PR #5 latest-frame is **not** an ancestor of #7 → risk of divergent latency semantics if both merge independently.
- PR #6 overlay **is** ancestor of #7.

## Platform details

- Current local branch before audit docs: `feat/multi-camera-runtime-foundation` (PR #35), ahead 0/0 vs origin, **untracked bak files**.
- Stack proof: `git merge-base --is-ancestor` → 32→33→34 = yes; 34→35 = **no**.
- Implication: merging #35 alone ships layout API **without** CC ops UX / recording scripts from #34.
- PR descriptions broadly match diffs; #33/#34 **include** earlier stack files (expected for stacked PRs, but GitHub shows each as base=`main` so review is confusing).
- No secrets found in tracked PR diffs during this audit; **local bak** is the acute risk.

## Monitor details

- PR #13 draft, mergeable, includes Gate 1 shell + Gate 2 multi-cam + evidence screenshots (office TEST imagery — no credentials in screenshots reviewed).
- `main` does not yet contain Gate 1/2.

## Audit branch

- `audit/release-readiness-2026-07-29` cut from `origin/main` (`e07adc0`).
- Docs-only; must not stage `deploy/mediamtx/*.bak*`.
