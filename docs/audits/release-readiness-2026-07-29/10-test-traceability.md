# 10 — Test traceability

## Suites run 2026-07-29 (safe)

| Repo | Command | Result | Notes |
|------|---------|--------|-------|
| detector | `pytest tests` (skip smoke video) | **116 passed** | tip `47366e2` |
| detector golden | SHA256 keypoint / expected | `1B6B8B6D…F119F165` / `8AB7B766…DABE668` | **not regenerated** |
| platform | `pytest backend/tests` | **42 passed** | on pre-audit tip then main branch for docs; tests exist on main+feature |
| monitor | verify gate1, gate2, whep, contracts, office-nav | **PASS** | static/contract |
| CC xUnit | not re-run this freeze | UNKNOWN today | ~108 on #34 tree historically |
| browser E2E full | — | NOT run as suite | Gate2 puppeteer evidence earlier |

## Traceability (selected)

| Funkce | Unit | Integration | E2E | Manual | Gap |
|--------|------|-------------|-----|--------|-----|
| Fall algorithm | golden+unit | smoke video opt | — | office | on-site Zahrádky |
| Event Core CRUD/ack | pytest | — | — | simulate_system | WS reconnect |
| Monitor shell | static verify | — | partial shots | nav | real Playwright suite |
| Multi-cam WHEP | static gate2 | internal-lan-whep | shots | soak pending | 30min |
| Recording | — | gate1 doc | — | office | automated segment assert |
| CC START ALL | xUnit partial | — | — | office | click matrix |
| Snapshot/clip | — | — | — | — | none |
| Model Lab | — | — | — | — | none |

## Warning

Static `verify:*` greps are **contract tests**, not functional GUI proof. Do not treat them as E2E PASS for release.
