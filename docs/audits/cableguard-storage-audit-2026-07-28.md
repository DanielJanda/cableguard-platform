# CableGuard storage audit — 2026-07-28

Read-only size audit. Paths are **repo-relative** (no user home, no credentials).
Nothing was deleted.

## Totals (working trees)

| Tree | Total MB | `.git` MB | `node_modules` MB | `runtime` MB | `bin/obj/publish` MB |
|------|----------|-----------|-------------------|--------------|----------------------|
| cableguard-platform | 119 | 2 | 0 | 33 | 18 |
| cableguard-detector | 6001 | 164 | 0 | 0 | 8 |
| cableguard-monitor | 274 | 0.4 | 271 | 0 | 0 |
| cableguard-monitor-wt-test-lab | 263 | 0* | 261 | 0 | 0 |

\* Worktree shares objects with main monitor `.git` via git worktree link.

**Combined ~6.7 GB** dominated by detector `.venv` (PyTorch CUDA).

## Top directories (detector)

| Rel path | MB | Class |
|----------|----|-------|
| `.venv` | 5549 | REGENERATABLE |
| `.git` | 164 | KEEP |
| `models/` | 163 | KEEP (weights; some duplicates) |
| `zahradky_safety/` | 125 | KEEP / ARCHIVE candidates (legacy copies) |

## Top directories (others)

| Repo | Rel path | MB | Class |
|------|----------|----|-------|
| monitor | `node_modules/` | 271 | REGENERATABLE |
| monitor worktree | `node_modules/` | 261 | REGENERATABLE |
| platform | `runtime/` | 33 | REGENERATABLE / ARCHIVE after review |
| platform | `tools/**/bin|obj` | ~18 | REGENERATABLE |

## Largest files (>10 MB) — pattern summary

Almost all of the top 25 files are under `cableguard-detector/.venv/Lib/site-packages/torch/lib/*.dll` (100–1177 MB each).

Model weights (KEEP; note duplicates):

| Rel path | MB |
|----------|----|
| `zahradky_safety/yolo26m.pt` | 42.2 |
| `models/zahradky/horni_stanice/pose_yolo26m.pt` | 42.2 |
| `models/zahradky/horni_stanice/barrier_best_m.pt` | 42 |
| `zahradky_safety/best_m.pt` | 42 |
| `models/shared/yolo11m-pose.pt` | 40.5 |
| `models/zahradky/dolni_stanice/barrier_best_m.pt` | 38.6 |
| `zahradky_safety/safety_bar_spodni_kamera/best_m.pt` | 38.6 |

SHA-256 duplicates among files >10 MB: torch DLLs are unique; **pose/barrier `.pt` pairs above are size-matched copies** (confirm with hash before deleting any copy). Treat as ARCHIVE/SAFE TO DELETE AFTER CONFIRMATION only after verifying identical SHA and updating configs to a single canonical `models/` path.

## Git LFS

Not measured as a separate store on these trees (no significant `.git/lfs` in the large-file scan). Model weights appear as normal tracked/untracked files.

## Worktrees

| Path (logical) | Branch | Note |
|----------------|--------|------|
| cableguard-monitor | `feature/zahradky-camera-comparison` | main checkout |
| cableguard-monitor-wt-test-lab | `feature/office-e2e-fall-test` | active test-lab / office-fall |
| cableguard-platform-wt-usb4761 | `fix/usb4761-readonly-diagnostics` | USB diagnostics |
| cableguard-platform-wt-docs-audit | `docs/storage-latency-audits-2026-07-28` | this audit |

Old unused worktrees: review with `git worktree list` periodically; removing a worktree directory does not delete the branch.

## Classification guide

| Class | Examples |
|-------|----------|
| KEEP | source, `models/` canonical weights, `.git`, configs without secrets |
| REGENERATABLE | `.venv`, `node_modules`, `bin/`, `obj/`, `publish/`, most `runtime/logs` |
| ARCHIVE | duplicate legacy weights under `zahradky_safety/` after hash confirm |
| SAFE TO DELETE AFTER CONFIRMATION | confirmed duplicate `.pt`, stale `runtime/test-results`, orphaned worktrees |
| UNKNOWN | anything under `runtime/` with unclear provenance |

## Cleanup plan (estimate, no deletes performed)

| Action | Est. freed |
|--------|------------|
| Recreate detector `.venv` only when needed / keep one venv | up to **~5.5 GB** (REGENERATABLE) |
| Deduplicate confirmed identical `.pt` copies | **~80–120 MB** |
| Drop monitor `node_modules` in unused worktrees (`npm ci` later) | **~250 MB** each |
| Prune platform `runtime/test-results` older than retention | **~tens of MB** |
| Remove obsolete git worktrees after merge | small + clarity |

**Do not** delete `.git` history or canonical production weights without explicit confirmation.

## Must remain

- Canonical model files referenced by site YAML
- Event Core / MediaMTX configs without secrets
- Golden-master fixtures / testdata
- Protected fall algorithm sources

## Method notes

- Directory sizes aggregated with recursive `Measure-Object` (safe rollup).
- Files under `node_modules` / `.git/objects` were skipped for per-file SHA listing; `.venv` torch libs dominate the >10 MB list as expected.
- Raw machine-local JSON under `runtime/test-results/storage-audit-*` is **not** committed (may contain absolute paths).
