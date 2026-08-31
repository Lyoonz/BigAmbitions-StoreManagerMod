# Phase 0 probe report — fill this in

Run the probe in a loaded city with at least one staffed store. Record results.

## Environment
- Game version: ______   Build type (Mono / IL2CPP): ______
- SDK commit: ______   Unity: 2022.3.62f2

## Workforce code map (from F8 dump + decompile)

| System | Type (namespace.Class) | DLL | public? | Notes |
|--------|------------------------|-----|---------|-------|
| Employee entity | | BigAmbitions.Characters | | |
| Task / station assignment | | | | enum / component / object? |
| Schedule / roster | | | | |
| Scheduler / solver (OrTools) | | | | entry point + inputs |
| Restock / supplier order | | | | |
| Complaints | | | | |
| Reputation | | | | |
| Training (HR path) | | | | |
| Leave requests | | | | |
| Day / week tick event | | | | or polled? |
| Difficulty setting | | | | |
| Mod-save API | | | | ModContext hook / Odin container |
| Store daily revenue | | | | |

## Probe results

| Probe | Result | Needed private-method patching? | Hackiness (1–5) | Notes |
|-------|--------|--------------------------------|-----------------|-------|
| 1 — reassign task | ☐ worked ☐ partial ☐ blocked | | | did the sim honour it? persisted a save? |
| 2 — write shift (direct) | ☐ worked ☐ partial ☐ blocked | | | |
| 2 — write shift (solver) | ☐ worked ☐ partial ☐ blocked | | | |
| 3 — trigger restock | ☐ worked ☐ partial ☐ blocked | | | cash moved? delivery arrived? |

## Side checks (design brief §11 / runbook §05)
- Save round-trip (add dummy field, save, reload, remove mod, reload): ______
- Scheduling stability (Steam "scheduling issue" repro): ______
- Departments concept present in code? ______
- Can a mod read difficulty at runtime? ______
- `ModOptions` scope — global only, or per-store possible? ______

## Decision
- [ ] **GO** — official SDK does all three writes without private-method patching → Phase 1.
- [ ] **CONDITIONAL GO** — needs bundled Harmony (D2). Accepted maintenance cost: ______
- [ ] **NO-GO / RE-SCOPE** — task assignment not drivable → numbers-only manager. Update the brief.

Reasoning (one paragraph): ______
