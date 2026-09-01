# Research — v2 pivot (Store Manager as a real meta-role)

Generated 2026-09-01 by a multi-agent research + design workflow, then verified in-game.

| File | What it is |
|------|-----------|
| `04_HR_Manager_meta_role_*.json` | Full trace of `HrManagerPlan` — storage (`GameInstance.hrManagerPlans`), creation, employee link, `TrainEmployees()` driver, UI classes |
| `03_Logistics_Manager_*.json` | `LogisticsManagerPlan` + `PricingManagerPlan` — multi-store supervision, `GetSupervisedStores`, `CalculateMaxDestinations`, snapshot/restore. **The closest analogue.** |
| `02_Headquarters_*.json` | The HQ "hire/manage a manager" UI — BizMan app tabs, `HrManagersPlanList`, candidate flow |
| `01_Mod_addable_UI_surfaces_*.json` | What UI a mod can add: phone app (no), native window (partial), ModOptions (yes), notifications/messages (yes) |
| `05_Mod_capability_boundary_*.json` | ModAPI vs Harmony vs asset-bundle vs infeasible, per capability. Custom skill = infeasible/fragile. |
| `06_DESIGN.json` | The synthesised architecture + phased plan. **Read this second.** |
| `07_CRITIQUE-over_engineering.json` | Adversarial review 1: cut ~60%, the free wins from a real skill aren't cashed in |
| `08_CRITIQUE-stability.json` | Adversarial review 2: delivery is a weekly Monday cycle not daily; dual-binding hazard; decompile is obfuscated — verify in-game |
| `reflection-dump-2026-09-01.txt` | In-game reflection dump of the SHIPPING assembly — the real de-obfuscated signatures. **The source of truth for API names.** |

**Start with `../DESIGN-v2.md`** — the distilled, decision-applied version. These raw files are the backing detail.
