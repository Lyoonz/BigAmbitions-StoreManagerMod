# How to playtest the Store Manager mod

The mod is deployed at
`%USERPROFILE%\AppData\LocalLow\Hovgaard Games\Big Ambitions\ModsLocal\StoreManager\`.
Nothing else needed — start the game and it loads.

To rebuild after a code change: `bash build/deploy-local.sh` (needs .NET SDK), then restart the game.

---

## Quick check (console) — 30 seconds

1. **Enable the debug console.** Steam → Big Ambitions → ⚙ Properties → *Launch Options* → type:
   `-console`
2. Launch, load a save that has an **office (Headquarters)** and at least one **shop with a
   wholesale delivery contract** (your day-125 save: *Foundation Headquarters* + *The Signature Mart*).
3. Press the **`` ` ``** key (backtick, left of `1`) to open the console. Type:
   ```
   StoreManager.SelfTest
   ```
   It injects a temporary Purchasing Agent, assigns a shop, runs one weekly restock pass, prints
   the delivery contract before/after, then undoes everything.
4. **Do not save the game after SelfTest** (a throwaway employee was briefly in memory). Your
   real saves are never written by it.

Other console commands: `StoreManager.Managers`, `.Adopt <n>`, `.Stores`, `.Assign <n>`,
`.SetCap <n> <amount>`, `.Days <n> <days>`, `.Status`, `.PlanWeek`, `.Drop`.

---

## Real playtest — the way it's meant to be used

### 1. Get a Purchasing Agent
Your existing agent *Richard Leary* is already on a vanilla Purchasing Agent plan, so the mod
won't take him (by design — one job each). Either:
- free him up in **BizMan → your HQ → Purchasing Agents** (unassign him), or
- **recruit a new one:** phone → Recruitment Agency → start a campaign for the *Purchasing Agent*
  skill → hire the candidate from **My Employees → Candidates**.

### 2. Put the agent to work at the office
- **My Employees → the agent → assign to your Headquarters.**
- **BizMan → your HQ → Schedule** → give the agent a shift on an office desk
  (desktop computer / laptop — they all accept the Purchasing Agent skill).
  The mod's plan stays **dormant** until the manager actually holds an HQ shift.

### 3. Make them a Store Manager
Open **Esc → Settings → Mods → Store Manager**:
- **Store Manager** dropdown → pick the agent you just scheduled.
- **Supervised stores** → tick the shops you want them to run (up to the skill cap —
  1 store at low skill, up to 5 near skill 100).
- **Per-store limits** → pick a store in *Configure store*, then set:
  - **Weekly restock budget** — hard ceiling on that shop's delivery cost per week.
  - **Target days of stock** — how big a buffer to aim for (10 is a sensible start).
  - **Staffing level** — Lean / Normal / Generous.
- *Defaults for newly assigned stores* at the bottom seed new picks.

Each shop you tick **must already have a wholesale delivery contract** set up in its
**BizMan → Deliveries** tab (the mod tunes an existing contract, it doesn't create one). If it
has none, the panel/console says so.

### 4. Play a few in-game weeks and watch
- Every **Saturday** the manager re-plans next Monday's delivery for each shop: it tops the
  contract up toward your target buffer, capped by your weekly budget.
- You get a **toast** per adjustment and a **weekly report** in Messages from the
  "Store Manager" contact (revenue-relevant summary + anything needing your attention).
- `StoreManager.Status` (console) shows every plan, per-store cap, spend, contract state.
- Fire or unschedule the manager → the plan goes dormant and each shop's delivery contract is
  **restored to exactly how it was** before you assigned it.

### What to look for / report back
- Does the stock buffer **settle** after 2–3 weeks, or keep climbing? (It should converge.)
- Do the weekly costs stay under the caps you set?
- Do the toasts / weekly message read clearly?
- Anything that feels wrong, spammy, or confusing.

### Save safety
The mod stores its data inside your save (`modData`), plus a backup file under
`ModsLocal\..\Mods\StoreManager\`. Assigning a manager and saving is fine and supported —
only **SelfTest** asks you not to save right after.
