# v2 static review — findings & fixes (2026-09-02)

A multi-agent static review of the just-written v2 code against the fresh Build 3672 decompile +
the in-game reflection dump. 35 raw findings → 7 fully adversarially verified before the run hit a
usage limit; the rest are `confirmed-in-source` with cited decompile line numbers. All the
material ones are fixed below.

## Fixed — save-damage class

| # | Finding | Fix |
|---|---------|-----|
| 1 | `onNewDay` / `onSaveGame` handlers unguarded — the game invokes them with a bare `?.Invoke()`, so a throw aborts the daily tick or **wedges the save subsystem** (`_saveProcessesRunning` stuck at 2 → `SavingGameInProgress` never clears → quit hangs, all later saves fail) | `Core/StoreManagerMod.cs`: every subscribed lambda now runs through `Guard(name, body)` (try/catch/log). `ManagerDirectory.OnNewDay/OnNewHour/RunWeeklyPlanning/Reconcile` each have their own try/catch too. |
| 2 | `Serialization.Serialize` had no try/catch (unlike Deserialize) and its result is an *argument* to `SaveModData`, so `SaveModData`'s own catch can't protect it | `Serialize` now never throws — returns the last-known-good JSON (cached on every success) or null. `SaveModData` skips the write when the payload is null/empty. |
| 3 | Failed `Deserialize` returned an empty list → next `Save()` overwrites the still-intact blob with `{"Plans":[]}`, destroying every plan **and every `OriginalContract` snapshot** | `Serialization.Load()` now returns `{Ok, Absent, Corrupt}`. On `Corrupt` the directory enters **read-only mode**: no writes, a warning toast, the blob is left untouched. |
| 4 | Persistence file scoped only by `charactersData[0].name` → two saves of the same-named character share it; loading a save whose `modData` lacks the key imports another timeline's plans and `Reconcile` rewrites *this* save's real delivery contracts | File scope key is now `characterId + "_" + SaveGameName` (both confirmed `GameInstance` fields). The file is consulted **only when `GameInstance.modData` is genuinely unavailable** (null dict), never merely because the key is absent. |
| 5 | `Reconcile`'s "manager gone → delete plan + restore/disable contracts" ran from `Load()` with no guard that the employee dictionary is populated — a load-order change would tear down every plan | Destructive teardown is gated on `GameApi.EmployeeSubsystemReady()` (`GetEmployeeInstances().Count > 0`) and never runs from `Load()` — only from the first tick that passes the gate. Until then a plan with a missing manager just goes dormant. |
| 6 | Contract restore is silently skipped during the Sun 20:00→Mon 08:00 delivery lock, but the assignment + `OriginalContract` snapshot were deleted anyway → the player's real contract left in the mod's tuned state **forever** | `DeliveryContracts.Restore/Disable` now return `bool` (applied?). When blocked, `RestoreAndRemove` pushes a `PendingRestore` (persisted on the plan) that `DrainPendingRestores` retries every tick until the lock lifts. `UnassignStore`'s toast no longer lies. |

## Fixed — wrong-behaviour class

| # | Finding | Fix |
|---|---------|-----|
| 7 | **Geometric compounding.** `DeliveryHelper.GetOrderAmount` just echoes `it.amount` (only clamps down during a ProductShortage) — it is not a demand estimate. `ComputeTarget` did `round(GetOrderAmount * TargetDaysOfStock/7)` and wrote it back, so every Saturday multiplied the line by ~1.43 off its own previous value → runaway to the budget cap in ~5–8 weeks, or decay to zero for `TargetDays < 7` | `ComputeTarget` rewritten: baseline = `it.amountOrderedLastWeek` (**real units delivered last week**, set by the game, never the mod's output). Target = `weeklyDemand + min(gap, weeklyDemand)` where `gap = targetOnHand - CountTotalResourcesInStock(...)`. Converges to steady-state `weeklyDemand`; the buffer fills over a few weeks; no compounding. Never reads `it.amount` as a multiplicand. |
| 8 | `PlanAndApply` set `c.enabled = true` without `c.UpdateNextDeliveryDay()` — a previously-disabled contract keeps a past `nextDeliveryDay`, and `HandleWholesaleDeliveries` needs exact `nextDeliveryDay == CurrentDay`, so it's enabled + tuned but **never delivers** | Capture `wasEnabled`; call `c.UpdateNextDeliveryDay()` (reflection, `Type.EmptyTypes`) when `!wasEnabled`. Same in `Restore`. |
| 9 | Budget scale-down floored every line with `Math.Floor(amount * scale)` → small lines → 0; if all lines → 0 the game skips the whole delivery while `enabled` stays true, and a re-seed later homogenises the player's hand-tuned contract | Scale from the **pre-tune original** amounts, not the inflated targets. If every line would be 0: revert to the originals, leave `enabled` as it was, raise an attention item — never ship an all-zero enabled contract. |
| 10 | "Weekly" budget cap only counted *increases* (`SpentThisWeek` bumped only when `OrdersAdjusted > 0`) — a contract already at target bills its full `TotalPricePerDelivery` every Monday with the cap noticing nothing | `PlanAndApply` sets `a.SpentThisWeek = TotalPricePerDelivery` (the standing weekly charge) every pass. The digest's `RestockSpend` adds that, not just deltas. Cap enforced against the full post-tune cost. |
| 11 | Transient ProductShortage baked permanently into the contract amount (via the `GetOrderAmount` path) | Fixed by #7 — `GetOrderAmount` is no longer used. |
| 12 | `SafeTotal` reflected `TotalPricePerDelivery` (it's public — reflection was pointless) and returned **0 on any throw** → `projected(0) > budget` is always false → budget cap disabled for that store, order treated as free | `Price(c)` calls the property directly and returns `float?`. On failure `PlanAndApply` reverts amounts, leaves `enabled` unchanged, and raises "couldn't price this wholesaler — left unchanged". Never proceeds with an unpriced order. |
| 13 | `Address.ToString()` (`"ba:street_x 57"`) ↔ `BuildingHelper.ParseAddressString` (expects number-first + a help abbreviation) are incompatible — every round-trip parse throws `FormatException`, killing `SelfTest` and forcing `MaxStores` / address attribution down dead fallbacks | `GameApi` never calls `ParseAddressString`. Buildings resolved by linear `Address.ToString() == key` scan. `SelfTest` sets `assignedAddress` to the **live HQ `Address` object** (`GameApi.HqAddressObject`, reflection-set). |
| 14 | `_onJob` wired to `GlobalEvents.onJobChange`, which fires **only when the player accepts a side job** — never on employee hire/fire/unschedule. "Reconcile when the manager is fired" (a v1 requirement) actually only happened on the daily tick, up to a game-day late; and accepting a pizza job fired an unrelated reconcile | Dropped `onJobChange`. Added `onNewHour` (throttled to ~every 3 in-game hours) running the light reconcile — catches a fired/unscheduled manager within hours, no false triggers. |
| 15 | `MaxStores` reflected `LogisticsManagerPlan.CalculateMaxDestinations`, which casts the registration to `Warehouse` (HQ isn't one → `InvalidCastException`) and reads `ba:skill_logisticsmanager` — guaranteed dead code | Deleted the reflection path. `MaxStores` = `StoreManagerPlan.MaxStoresForSkill(skill)` (the mod's own `1 + floor(skill/25)` curve). No vanilla helper computes this for a purchasing agent. |
| 16 | `BuildingRegistration.Address` can be null (`BuildingCached == null`) → `b.Address.ToString()` NRE in unguarded LINQ projections | `PlayerBuildings` filters `b.Address != null`; all `.ToString()` go through a try/catch `A(b)` helper; empty results dropped. |
| 17 | `IsBoundToVanillaPlan` didn't check the **Purchasing Agent** plan — and the review found `ba:skill_purchasingagent` **does** have an HQ tab (`PurchasingAgentsPlanList`, drives `ImportPartnership` not `DeliveryContract`, so no direct collision, but a player could double-bind the same employee) | Added `PurchasingAgentHelper.GetAssignedPlanForPurchasingAgent` to the guard. |
| 18 | `plan.Week` (a *persisted* field) was `Reset()` at the **end** of `RunWeeklyPlanning` — a crash mid-pass leaves a stale tally that next week adds to; one bad plan starved the rest and skipped `Save()` | `plan.Week.Reset()` + `SpentThisWeek = 0` now run at the **start** of each plan's turn; each plan's body is in its own try/catch. |
| 19 | `IsScheduledAtHq` returned `false` on any exception → a transient failure on the in-game **Saturday** (the only planning day) marks the plan dormant and loses the whole week's restock | Now returns `bool?`; `null` = "couldn't tell" and `Reconcile` keeps the previous `Dormant` state rather than forcing dormant. |

## Minor / cleanup done
- Deleted `GameApi.ChangeMoney` — it was never called *and* would have double-charged (the delivery contract bills automatically on Monday).
- `GetSkillValue` no longer called without a `HasSkill` guard → no more `"Skill ba:skill_purchasingagent not found"` log spam for every non-manager employee.
- Removed dead `priceBefore`, the unreachable `budgetLeft <= 0 ? 0` ternary, and `ContractSnapshot.WholesaleAddress` (captured, never restored).
- `Serialization` now passes an explicit pinned `SerializationPolicies.Everything` context (the comment claimed a custom policy; the code used Odin's default).

## Not changed (verified NOT bugs by the review)
- `TimeHelper.GetDayOfWeek().ToString()` **does** yield `"Monday".."Sunday"` — every D12 string check is correct.
- `DeliveryHelper` constants match D12 (Monday delivery, Sun 20:00→Mon 08:00 lock); `CanModifyContract(nextDeliveryDay)` is the right call; Saturday planning is never in the lock window.
- `onSaveGame` fires **synchronously before** serialization → D13 write-on-save lands in the file.
- The whole game-API signature surface (Notifications, Contact/TextMessage, ChangeMoneySafe, ModOptions, OdinSerializer, event fields, `EmployeeHelper`, `BuildingRegistration`, `DeliveryContract`) — all confirmed matching.

## Still open — needs the one in-game test
- Does an HQ have an assignable desk workstation whose `suitableSkills` includes `ba:skill_purchasingagent`? (The v2 probe dumps this.)
- The convergent `ComputeTarget` math against a real multi-week playthrough (does the buffer settle where expected?).
- `GameInstance.modData` round-trip of a multi-KB string through both save paths (CosaNostra proves the mechanism for a small value).
