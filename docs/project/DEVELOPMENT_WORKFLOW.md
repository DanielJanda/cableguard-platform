# DEVELOPMENT_WORKFLOW — Git a vývojový proces

Last verified: 2026-07-27

Verified against:

- cableguard-platform `main` commit `5400cb3`
- cableguard-monitor `main` commit `f085ef0`
- cableguard-detector `main` commit `c628a2f`

---

## Standardní workflow (všechny tři repozitáře)

```
main
  → feature branch (feature/... | fix/... | docs/...)
    → implementace
      → lokální test (pytest / verify skripty / golden master)
        → commit
          → push
            → draft PR do main
              → acceptance (testy + reálné ověření, u UI/video vizuální potvrzení)
                → merge (běžný merge commit, žádný force push)
```

Zásady:

- **Push je žádoucí** — hotové commity nepatří jen na lokální disk; push na feature branch nic nerozbíjí a slouží jako záloha i review podklad.
- **Merge vyžaduje ověření** — testy zelené + akceptační kritérium splněné (např. „video potvrzeno z druhého PC v LAN“ u internal-lan runtime PR #8/#7).
- Draft PR se otvírá brzy; merguje se až po acceptance. Odložené experimenty se značí `[DEFERRED]` v titulku a nemergují se.
- `main` = jediný deploy zdroj. Žádné dlouhodobé odchylky pracovního stromu od main mimo feature větve.
- Secrets nikdy do commitů — gitignored `.env*`, `mediamtx.local.yml`, `runtime/`.

Aktuální příklady: merged platform #8 / monitor #7 (internal LAN runtime), otevřené draft platform #10 + monitor #9 (druhá kamera), deferred platform #6 + monitor #5 (public HTTPS).

## Lovable

**Lovable není runtime deployment pro současný internal-LAN režim.** Provoz běží z lokálního checkoutu `main` na `10.6.1.40`, ne z lovable.app.

Lovable se používá pro:

- UI development (komponenty, layout, styling),
- vizuální změny monitoru,
- synchronizaci frontendového kódu přes GitHub.

Vztahy:

```
Lovable ⇄ GitHub (cableguard-monitor)     # obousměrná synchronizace kódu
GitHub main → internal deployment          # git pull na 10.6.1.40 + restart monitoru
```

Důsledky:

- Po změně UI **není potřeba** Lovable „Publish → Update“ — pro interní runtime stačí, aby změna byla na `main`, a na `10.6.1.40` se provede `git pull` + restart monitoru.
- Lovable preview funguje díky mock režimu (`VITE_USE_MOCKS=true` default) zcela bez backendu; živé video v Lovable hostingu neběží (HTTPS→HTTP LAN blokace) a běžet nemusí.
- Konflikt mezi Lovable komity a lokálními feature větvemi se řeší standardně přes Git (Lovable komituje do main/své větve — před vlastní prací vždy `git pull`).

## Nasazení změny do interního provozu

```powershell
# na 10.6.1.40, po merge PR do main:
cd C:\Users\mega\Documents\cableguard-monitor   # nebo -platform
git checkout main
git pull origin main
# restart dotčené služby dle OPERATIONS.md
```

## Konvence commitů

Prefixy podle obsahu: `feat:`, `fix:`, `docs:`, `test:`, `chore:`. Zprávy popisují „proč“, ne jen „co“.
