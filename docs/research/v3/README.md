# v3 research — native custom-skill roles

Multi-agent research (Build 3672 decompile) + synthesis + 2 adversarial critiques, 2026-09-02.
The user overrode D10 ("no custom skill") — they want native Filiaalmanager + Team Leader roles.

- `07_*` custom SkillData registration + the SkillHelper.GetData Harmony guard
- `08_*` HQ BizMan tab injection mechanics (BizManBusiness.SetUpTabs)
- `02_*` Recruitment Agency / candidate generation with an unknown skill
- `06_*` existing HQ meta-role plans (HrManagerPlan etc.) as templates
- `01_*` store departments (confirmed: no game concept — model as mod data)
- `04_*` job title + wage derivation from skill
- `03_*` HQ capacity/expansion (confirmed: no upgrade mechanic exists)
- `05_*` mod capability recheck for Build 3672
- `DESIGN.json` the full synthesised architecture (Phases A-D)
- `CRITIQUE_*` two adversarial reviews — both say: ship Store Manager only for v1, cut the HQ
  tab, cut sm:skill_assistantmanager, simplify departments; the stability critique adds the
  folder-delete-bricks-the-save risk and the kill-switch requirement.

**The distilled, decision-applied plan is `../DESIGN-v3.md`.**
