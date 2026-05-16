# R4 Bill Ordering Fix — Design Document

*RimWorld 1.6 · Harmony 2.4.x · RRRR mod*

---

## Problem Statement

When a player places multiple R4 and vanilla bills on the same workbench, bill
ordering is not respected. A repair bill placed below a clean bill will still
execute first, and R4 bills can jump ahead of vanilla bills or vice versa
regardless of their position in the bill stack.

Players expect bill stack ordering to be authoritative — the bill at the top of
the list should always be attempted first, exactly as vanilla behaves with its
own bills.

---

## Root Cause

R4 splits bill execution across **three independent WorkGivers** per bench:
vanilla's `WorkGiver_DoBill`, R4's `WorkGiver_R4RepairBill`, and R4's
`WorkGiver_R4CleanBill`. Each scans the same `BillStack` but only sees "its
own" recipe types. Because `JobGiver_Work` iterates WorkGivers by descending
`priorityInType` and selects the first valid target, the WorkGiver with the
highest priority always wins — regardless of where its bills sit in the stack.

### Concrete example: machining table

Three WorkGivers scan the same bench (Smithing work type):

| WorkGiver | priorityInType | Sees |
|---|---|---|
| `DoBillsMachiningTable` (vanilla) | 75 | Vanilla recipes + R4 recycle |
| `RRRR_RepairBill_Machining` | 72 | R4 repair only |
| `RRRR_CleanBill_Machining` | 70 | R4 clean only |

Player sets bill order: Clean → Repair → Make component. Actual execution:
vanilla at priority 75 checks first, skips clean/repair (wrong
`requiredGiverWorkType`), finds "Make component", returns that job. Repair at
72 and clean at 70 never get asked. Even if vanilla finds nothing, repair at 72
always beats clean at 70.

The visible bill order in the UI is irrelevant — WorkGiver `priorityInType`
decides.

---

## How Vanilla Bill Ordering Actually Works

Verified against decompiled RimWorld 1.6 source.

### The bill dispatch chain

```
JobGiver_Work.TryIssueJobPackage
  → iterates WorkGiversInOrderNormal (sorted by descending priorityInType)
  → for each WorkGiver at the current priority tier:
      → WorkGiver_DoBill.JobOnThing(pawn, bench)
        → precondition checks (IBillGiver, usable, reservable, not burning, etc.)
        → StartOrResumeBillJob(pawn, billGiver)
          → for i = 0..BillStack.Count:
              → skip: requiredGiverWorkType mismatch
              → skip: nextTickToSearchForIngredients cooldown
              → skip: !ShouldDoNow or !PawnAllowedToStartAnew
              → skip: skill requirements not met
              → skip: medical/mech/UfT/autonomous special cases
              → TryFindBestBillIngredients → if fails, set cooldown, continue
              → TryStartNewDoBillJob → return Job (JobDefOf.DoBill)
```

### Key properties

1. **Single-scanner, top-to-bottom.** `StartOrResumeBillJob` is a flat `for`
   loop over `BillStack` by index. No sorting, no priority weighting. First
   runnable bill wins.

2. **`requiredGiverWorkType` is a per-bill filter, not a per-bench filter.**
   Each bill's recipe can declare a `requiredGiverWorkType`. If the current
   `WorkGiver_DoBill`'s `def.workType` doesn't match, the bill is skipped in
   the loop — but the loop continues to the next bill. This is how tailoring
   recipes are invisible to a smithing WorkGiver even though they share a bench
   (fabrication bench).

3. **Ingredient failure sets a cooldown.** When `TryFindBestBillIngredients`
   fails, vanilla sets `bill.nextTickToSearchForIngredients` to
   `TicksGame + [500..600]`. Future scans skip that bill until the cooldown
   expires. This prevents per-tick ingredient searches on unsatisfiable bills.

4. **Same-tier WorkGivers compete on proximity, not bill order.**
   `JobGiver_Work` accumulates the best (closest) target across all WorkGivers
   at the same `priorityInType`. If two WorkGivers at the same priority both
   return a job for the same bench, the last one in iteration order wins
   (overwrites `bestTargetOfLastPriority`). There is no mechanism for
   cross-WorkGiver bill-order coordination.

### What this means for R4

Vanilla was designed for one `WorkGiver_DoBill` per bench (per work type). That
single scanner walks the bill stack in order and returns the first runnable
bill. R4 broke this by introducing parallel scanners that each see a subset of
the stack. No amount of `priorityInType` tuning can restore bill ordering
across separate WorkGivers — it's architecturally impossible without
coordination.

---

## Approaches Considered

### Approach 1: Separate WorkGivers with priority tuning (current)

Give each R4 bill type its own WorkGiver at a carefully chosen
`priorityInType`.

**Why it fails:** `priorityInType` determines which *WorkGiver* goes first, not
which *bill* goes first. Bill stack order is only respected within a single
WorkGiver's scan. Clean at priority 70 will always lose to repair at 72,
regardless of bill position.

### Approach 2: Same-priority WorkGivers with "defer to earlier bill" logic

Put all WorkGivers (vanilla, R4 repair, R4 clean) at the same
`priorityInType`. Each R4 WorkGiver scans the full bill stack and returns null
if it finds a runnable non-R4 bill above its own best candidate.

**Why it fails:** The "defer" check cannot cheaply determine whether vanilla
will actually execute a bill that R4 defers to. Specifically:

- R4 can check `ShouldDoNow()`, `PawnAllowedToStartAnew()`, skill
  requirements, and `nextTickToSearchForIngredients` cooldown. These are cheap.
- R4 **cannot** check whether vanilla can find ingredients — that requires the
  full region-traversal ingredient search, which is expensive and would
  duplicate vanilla's work.
- On the first tick where a vanilla bill fails ingredients (before its cooldown
  is set), R4's defer check sees the bill as "runnable" and returns null. But
  vanilla also fails the bill in the same scan pass and continues to a lower
  bill. Both scanners run sequentially in `JobGiver_Work`, so vanilla sets the
  cooldown during its scan, but R4's scan may have already run (depending on
  iteration order) and made the wrong decision.
- Even when cooldowns are correctly set, `JobGiver_Work`'s same-tier logic
  picks the *last* WorkGiver that returned a valid target for the closest
  bench. With two WorkGivers returning jobs for the same bench, the winner is
  list-order-dependent, not bill-order-dependent.

### Approach 3: Prefix on `WorkGiver_DoBill.JobOnThing` — replace vanilla's scanner

Patch `JobOnThing` with a bool prefix that replaces `StartOrResumeBillJob`
with an R4-aware unified scanner when R4 bills are present.

**Why it's fragile:**

- **High-conflict patch target.** `WorkGiver_DoBill.JobOnThing` is the single
  most commonly patched WorkGiver method in the modding ecosystem. Mods like
  Common Sense, Better Workbench Management, Bill Ingredient Source, and
  various bill-management mods all touch this code path. A bool prefix that
  returns `false` (skipping the original) prevents all postfixes from other
  mods from receiving vanilla's result.
- **Must replicate vanilla internals.** The replacement scanner must handle
  every check vanilla performs: refuelable, `UsableForBillsAfterFueling`,
  `RemoveIncompletableBills`, `CanReserveSittableOrSpot`, the
  `FloatMenuMakerMap.makingFor` float-menu ingredient display path,
  `Bill_ProductionWithUft` handling, `Bill_Autonomous` handling,
  `Bill_Medical` handling. If vanilla adds new bill types or checks in a future
  version, R4's replacement scanner silently diverges.
- **Non-vanilla JobDef from vanilla method.** Other mods' postfixes on
  `JobOnThing` may inspect `__result.def` or `__result.bill` assuming the
  result is always `JobDefOf.DoBill` or null. Returning `RRRR_Repair` or
  `RRRR_Clean` from a method that normally only produces `DoBill` can confuse
  those postfixes.
- **Replaces vanilla state management.** Vanilla sets
  `nextTickToSearchForIngredients` on bills that fail ingredient searches.
  The replacement scanner must replicate this exactly, or bills that should
  be on cooldown get re-scanned every tick.

### Approach 4: Postfix on `JobOnThing` — inspect and substitute ✅ PROPOSED

Let vanilla's scanner run completely untouched. After it returns, a postfix
inspects the result and asks: "was there a runnable R4 bill *above* what
vanilla chose?" If yes, substitute the R4 job. If no, pass vanilla's result
through unchanged.

**This is the proposed solution.** See full design below.

---

## Proposed Solution: `JobOnThing` Postfix with Bill-Order Arbitration

### Core concept

Vanilla runs first. It evaluates every bill it can see (vanilla bills + R4
recycle), skipping R4 repair/clean via `requiredGiverWorkType` mismatch. It
returns its best result — a job for the first runnable vanilla/recycle bill, or
null.

The R4 postfix then walks the bill stack top-to-bottom, looking for R4
repair/clean bills that sit *above* vanilla's choice (by stack index). For each
such bill, it attempts to create the R4 job. The first one that succeeds
replaces vanilla's result.

Vanilla bills between the top of the stack and vanilla's chosen bill are
**not re-evaluated** — vanilla already checked them, failed them, and set their
cooldowns. R4 trusts vanilla's decisions about vanilla bills.

### Pseudocode

```csharp
[HarmonyPatch(typeof(WorkGiver_DoBill), nameof(WorkGiver_DoBill.JobOnThing))]
static class Patch_R4BillOrdering
{
    static void Postfix(ref Job __result, Pawn pawn, Thing thing, bool forced)
    {
        // Fast path: no R4 repair/clean bills on this bench → don't interfere
        if (!(thing is IBillGiver bg) || !HasAnyR4RepairOrCleanBill(bg))
            return;

        // If vanilla returned a job for an R4 repair/clean recipe
        // (possible on CraftingSpot where requiredGiverWorkType=Crafting
        // matches the vanilla WorkGiver), discard it — we'll handle it
        // ourselves with the correct JobDef below.
        bool vanillaReturnedR4 = false;
        if (__result?.bill?.recipe != null && IsR4RepairOrClean(__result.bill.recipe))
        {
            vanillaReturnedR4 = true;
            __result = null;
        }

        // Determine the bill-stack index of vanilla's chosen bill.
        // If vanilla found nothing (or we just discarded its R4 result),
        // use MaxValue so every R4 bill qualifies as "above".
        int vanillaIndex = (__result?.bill != null)
            ? bg.BillStack.IndexOf(__result.bill)
            : int.MaxValue;

        // Walk stack top-to-bottom, checking R4 bills above vanilla's pick
        for (int i = 0; i < bg.BillStack.Count && i < vanillaIndex; i++)
        {
            Bill bill = bg.BillStack[i];
            if (!IsR4RepairOrClean(bill.recipe))
                continue;   // vanilla bill — vanilla already handled it

            // Replicate vanilla's cheap skip checks
            if (!bill.ShouldDoNow() || !bill.PawnAllowedToStartAnew(pawn))
                continue;
            if (!forced && Find.TickManager.TicksGame
                    <= bill.nextTickToSearchForIngredients)
                continue;
            if (bill.recipe.FirstSkillRequirementPawnDoesntSatisfy(pawn) != null)
                continue;

            // Attempt to create the R4 job (finds candidates + ingredients)
            Job r4Job = TryCreateR4BillJob(pawn, thing, bill, forced);
            if (r4Job != null)
            {
                __result = r4Job;
                return;
            }

            // R4 bill couldn't run — set vanilla-style cooldown
            if (!forced)
                bill.nextTickToSearchForIngredients =
                    Find.TickManager.TicksGame + ReCheckFailedBillTicksRange.RandomInRange;
        }

        // If we discarded vanilla's R4 result but found no R4 job above
        // vanilla's pick, we may still need to handle the discarded bill.
        // Fall through: check if the original R4 bill is now reachable.
        if (vanillaReturnedR4 && __result == null)
        {
            // Re-scan from vanillaIndex onward for any R4 bill
            // (the one vanilla originally found, or any below it).
            // This handles the case where vanilla found an R4 bill and
            // there was nothing above it — we still need to create the
            // correct R4 job for it rather than the DoBill job vanilla made.
            for (int i = 0; i < bg.BillStack.Count; i++)
            {
                Bill bill = bg.BillStack[i];
                if (!IsR4RepairOrClean(bill.recipe))
                    continue;
                if (!bill.ShouldDoNow() || !bill.PawnAllowedToStartAnew(pawn))
                    continue;
                if (!forced && Find.TickManager.TicksGame
                        <= bill.nextTickToSearchForIngredients)
                    continue;
                if (bill.recipe.FirstSkillRequirementPawnDoesntSatisfy(pawn) != null)
                    continue;

                Job r4Job = TryCreateR4BillJob(pawn, thing, bill, forced);
                if (r4Job != null)
                {
                    __result = r4Job;
                    return;
                }
                if (!forced)
                    bill.nextTickToSearchForIngredients =
                        Find.TickManager.TicksGame + ReCheckFailedBillTicksRange.RandomInRange;
            }
        }
    }
}
```

### Scenario traces

#### Scenario 1: All bills valid

Bill stack: `[Vanilla A, R4 Repair B, Vanilla C]`

1. Vanilla's `StartOrResumeBillJob`: A is runnable → returns `DoBill` job for A (index 0).
2. R4 postfix: `vanillaIndex = 0`. Loop checks bills at index < 0 → none →
   loop exits immediately. `__result` stays as job A.
3. **Pawn does A.** ✅

#### Scenario 2: Bill A has no ingredients

Bill stack: `[Vanilla A, R4 Repair B, Vanilla C]`

1. Vanilla's `StartOrResumeBillJob`: A passes basic checks but
   `TryFindBestBillIngredients` fails → sets
   `A.nextTickToSearchForIngredients` → continues → B is skipped
   (`requiredGiverWorkType` mismatch) → C is runnable → returns `DoBill` job
   for C (index 2).
2. R4 postfix: `vanillaIndex = 2`. Loop checks index 0 and 1:
   - Index 0 (A): not R4 → `continue` (vanilla already handled it)
   - Index 1 (B): R4 repair → passes cheap checks → `TryCreateR4BillJob`
     finds candidate and ingredients → returns repair job.
3. `__result` replaced with R4 repair job.
4. **Pawn does B.** ✅

#### Scenario 3: Both A and B can't run

Bill stack: `[Vanilla A, R4 Repair B, Vanilla C]`

1. Vanilla: A fails ingredients (cooldown set) → B skipped → C succeeds →
   returns job C (index 2).
2. R4 postfix: index 0 (A) skipped (not R4), index 1 (B) is R4 →
   `TryCreateR4BillJob` fails (no candidates or no materials) → sets cooldown
   on B → loop continues → index 2 not < `vanillaIndex` → loop exits.
3. `__result` stays as job C.
4. **Pawn does C.** ✅

#### Scenario 4: Vanilla found nothing

Bill stack: `[R4 Clean A, R4 Repair B]`

1. Vanilla: both skipped via `requiredGiverWorkType` → returns null.
2. R4 postfix: `vanillaIndex = MaxValue`. Loop checks all bills:
   - Index 0 (A): R4 clean → passes checks → `TryCreateR4BillJob` succeeds →
     returns clean job.
3. `__result` set to clean job.
4. **Pawn does A.** ✅

#### Scenario 5: CraftingSpot (requiredGiverWorkType matches)

Bill stack: `[R4 Repair A, Vanilla B]` on CraftingSpot.

Vanilla's `DoBillsUseCraftingSpot` has `workType=Crafting`. R4 repair has
`requiredGiverWorkType=Crafting`. So vanilla's scanner sees R4 repair A as a
valid bill.

1. Vanilla: A passes all checks including `requiredGiverWorkType` → ingredient
   search → creates `DoBill` job for A (index 0).
2. R4 postfix: detects `__result.bill.recipe` is R4 repair/clean → sets
   `vanillaReturnedR4 = true`, sets `__result = null`, sets
   `vanillaIndex = MaxValue`.
3. Loop: index 0 (A) is R4 repair → `TryCreateR4BillJob` → succeeds → sets
   `__result` to `RRRR_Repair` job with correct custom JobDef.
4. **Pawn does A with R4's custom JobDriver.** ✅

Without the postfix, vanilla would dispatch the R4 bill using `JobDefOf.DoBill`
and `JobDriver_DoBill`, which doesn't know about R4's custom repair cycles,
material consumption, or failure mechanics.

#### Scenario 6: Interleaved R4 and vanilla, middle bills fail

Bill stack: `[R4 Clean A, Vanilla B, R4 Repair C, Vanilla D]`

A has no tainted items. B has no ingredients.

1. Vanilla: A skipped (`requiredGiverWorkType`) → B fails ingredients (cooldown
   set) → C skipped (`requiredGiverWorkType`) → D succeeds → returns job D
   (index 3).
2. R4 postfix: `vanillaIndex = 3`. Loop:
   - Index 0 (A): R4 clean → `TryCreateR4BillJob` → no tainted items → fails →
     sets cooldown → continue
   - Index 1 (B): not R4 → `continue`
   - Index 2 (C): R4 repair → `TryCreateR4BillJob` → finds candidate →
     succeeds → sets `__result` to repair job.
3. **Pawn does C.** ✅

Bill B was correctly skipped: vanilla already failed it and set its cooldown.
Bill A was correctly attempted first but had no work. Bill C goes next. Bill D
is deferred because C was above it.

---

## Files to Remove

The postfix replaces the entire separate-WorkGiver bill architecture for
repair and clean:

| File | Reason |
|---|---|
| `Source/Jobs/WorkGiver_R4CleanBill.cs` | Replaced by postfix |
| `Source/Jobs/WorkGiver_R4RepairBill.cs` | Replaced by postfix |
| `Source/Patches/Patch_WorkGiver_DoBill_Repair.cs` | Merged into the new postfix |

### WorkGiverDefs to remove from `1.6/Defs/WorkGivers.xml`

All 10 bill-based WorkGiverDefs:

- `RRRR_CleanBill_CraftingSpot`
- `RRRR_CleanBill_Tailor`
- `RRRR_CleanBill_Smithy`
- `RRRR_CleanBill_Machining`
- `RRRR_CleanBill_Fabrication`
- `RRRR_RepairBill_CraftingSpot`
- `RRRR_RepairBill_Tailor`
- `RRRR_RepairBill_Smithy`
- `RRRR_RepairBill_Machining`
- `RRRR_RepairBill_Fabrication`

The designation-based WorkGivers (`RRRR_Repair_Crafting`, `RRRR_Clean_Crafting`,
etc.) are **unaffected** — they handle the designator flow, not bills.

---

## Files to Create

| File | Purpose |
|---|---|
| `Source/Patches/Patch_R4BillOrdering.cs` | The `JobOnThing` postfix |
| `Source/Jobs/R4BillJobFactory.cs` | Extracted job-creation logic (shared between repair and clean bill paths) |

### Job-creation logic extraction

`WorkGiver_R4RepairBill.CreateJobOnThing` and `WorkGiver_R4CleanBill.CreateJobOnThing`
each contain candidate-finding and job-assembly logic. This moves into
`R4BillJobFactory` as two static methods:

```csharp
static class R4BillJobFactory
{
    // Returns a RRRR_Repair job or null
    public static Job TryCreateRepairBillJob(
        Pawn pawn, Thing bench, Bill bill, bool forced) { ... }

    // Returns a RRRR_Clean job or null
    public static Job TryCreateCleanBillJob(
        Pawn pawn, Thing bench, Bill bill, bool forced) { ... }
}
```

These contain the candidate search (region traversal or BIS fast-path),
ingredient search (`TryFindBestFixedIngredients`), haul-off check, and job
assembly — everything currently in the WorkGiver `CreateJobOnThing` methods.

---

## Files to Modify

| File | Change |
|---|---|
| `1.6/Defs/WorkGivers.xml` | Remove 10 bill-based WorkGiverDefs |
| `1.6/Defs/Recipes.xml` | `requiredGiverWorkType=Crafting` can optionally stay as belt-and-suspenders, or be removed (see open question below) |
| `DESIGN.md` | Update architecture tables, Harmony patch list, WorkGiver section |

---

## What Vanilla's `requiredGiverWorkType=Crafting` Does in This Design

R4 repair/clean recipes currently set `requiredGiverWorkType=Crafting`. This
has two effects:

1. **On non-Crafting benches** (smithy, tailor, machining, fabrication): vanilla's
   `WorkGiver_DoBill` (with `workType=Smithing` or `workType=Tailoring`) skips
   R4 repair/clean bills in `StartOrResumeBillJob` because
   `requiredGiverWorkType != def.workType`. This is desirable — vanilla
   shouldn't create `DoBill` jobs for R4 recipes.

2. **On CraftingSpot**: vanilla's `DoBillsUseCraftingSpot` has
   `workType=Crafting`, which *matches*. Vanilla can and will create a `DoBill`
   job for R4 repair/clean recipes. The postfix detects this case
   (`vanillaReturnedR4`) and substitutes the correct R4 job.

**Recommendation: keep `requiredGiverWorkType=Crafting`.** It reduces the
postfix's work on non-Crafting benches (vanilla skips R4 bills in its loop, so
vanilla's `__result` is never an R4 bill — the `vanillaReturnedR4` path is
never triggered). The CraftingSpot case still works correctly via the postfix
fallback. Removing it would mean vanilla creates phantom `DoBill` jobs for R4
recipes on every bench type, which the postfix would then discard — wasted
ingredient searches.

---

## Performance Considerations

### Happy path (no R4 bills on bench)

`HasAnyR4RepairOrCleanBill` is an O(N) scan over the bill stack checking
`recipe.workerClass`. For typical bill stacks (1–10 bills), this is negligible.
Can be cached per-bench-per-tick if profiling shows it's hot.

### Typical path (R4 bills present, vanilla found a bill)

The postfix iterates from index 0 to `vanillaIndex`, checking only R4 bills.
Vanilla bills are skipped with a single `IsR4RepairOrClean` check. For
interleaved stacks, this is at most N iterations with cheap checks per vanilla
bill and one `TryCreateR4BillJob` call per R4 bill.

### Worst case (bench with only R4 repair/clean bills)

Vanilla returns null (all R4 bills are skipped via `requiredGiverWorkType`).
The postfix walks the entire stack and attempts `TryCreateR4BillJob` on each.
This is equivalent to what the current `WorkGiver_R4RepairBill` and
`WorkGiver_R4CleanBill` do — no regression.

### Removed overhead

The current architecture runs *three* full WorkGiver scans per bench (vanilla +
repair + clean), each with `HasJobOnThing` + `JobOnThing` calls. The new
approach runs *one* vanilla scan plus one lightweight postfix pass. Net
improvement even in the worst case.

---

## Compatibility Considerations

### Other mods patching `WorkGiver_DoBill.JobOnThing`

The postfix runs *after* vanilla and after other mods' postfixes. It only
modifies `__result` when it finds an R4 bill that should go first. Other mods'
postfixes see vanilla's original result and can modify it; R4's postfix then
inspects whatever came out of the chain.

If another mod's postfix nulls vanilla's result (e.g. Common Sense deciding the
bench needs cleaning first), `vanillaIndex` becomes `MaxValue` and R4 checks
all its bills — correct behavior, since the other mod decided nothing vanilla
should run.

If another mod's postfix *substitutes* a different vanilla bill, R4 will use
that bill's index. If the substituted bill is lower in the stack than an R4
bill, R4 will correctly attempt the R4 bill first. If higher, R4 defers.

### Bill Ingredient Source (BIS) compatibility

The candidate search in `R4BillJobFactory` already supports BIS via
`BISCompat.GetStorageCandidateIDs` — this is carried over from the existing
WorkGiver code. No changes needed.

### Mods adding new bill types

The postfix only recognizes R4 repair/clean recipes (by `workerClass` check).
All other bill types — vanilla, modded, future vanilla — pass through
untouched. The postfix doesn't interfere with `Bill_Medical`,
`Bill_ProductionWithUft`, `Bill_Autonomous`, or any custom `Bill` subclass.

### Save compatibility

No new `IExposable` state. No changes to bill serialization. The removed
WorkGiverDefs are XML-only (not saved). The removed WorkGiver classes are
C#-only (not saved). Existing saves with R4 repair/clean bills on benches will
work — the bills themselves are `Bill_Production` instances with R4 `RecipeDef`
references, which are unchanged.

---

## Pitfalls and Edge Cases

### Pitfall: double ingredient search on CraftingSpot

On the CraftingSpot, `requiredGiverWorkType=Crafting` matches vanilla's
`WorkGiver_DoBill`. Vanilla will run `TryFindBestBillIngredients` on the R4
bill before the postfix can intervene. If the R4 bill is first in the stack,
vanilla does a full ingredient search, creates a `DoBill` job, and the postfix
discards it and creates an R4 job (which does its own candidate + ingredient
search). This means two ingredient searches for the same bill on the same tick.

**Mitigation:** This only happens on CraftingSpot (the only bench with
`workType=Crafting`). It only happens when the R4 bill is the first runnable
bill in the stack. The cost is one extra `TryFindBestBillIngredients` call per
affected bench per scan cycle — acceptable for a workbench that typically has
few bills.

**Future optimization:** A void prefix on `StartOrResumeBillJob` (or
`TryFindBestBillIngredients`) could skip the ingredient search for R4
repair/clean recipes. Not needed for correctness, only for performance if
profiling shows it matters.

### Pitfall: `nextTickToSearchForIngredients` double-write on CraftingSpot

If vanilla searches ingredients for an R4 bill on CraftingSpot, fails, and sets
the cooldown — then the postfix also tries the same bill and fails — the
cooldown gets overwritten with a new random value. This is harmless (both
values are in the same 500–600 range) but worth knowing about.

### Pitfall: `FloatMenuMakerMap.makingFor` ingredient display

Vanilla's `StartOrResumeBillJob` has a `FloatMenuMakerMap.makingFor` code path
that tracks missing ingredients for right-click menu display. The postfix's R4
bill checks don't participate in this — R4 repair/clean bills won't show
"missing materials" in the float menu. This is a pre-existing limitation (the
current WorkGiver architecture doesn't support it either) and is not a
regression.

### Pitfall: recycle bill ordering

R4 recycle bills use vanilla's `WorkGiver_DoBill` pipeline (no
`requiredGiverWorkType` set). Vanilla's scanner sees them as normal bills and
handles them in stack order alongside other vanilla bills. The postfix does not
need to handle recycle — it's already correctly ordered by vanilla.

However, if a recycle bill is below an R4 repair/clean bill, and the repair/
clean bill is runnable, the postfix correctly substitutes the repair/clean job.
If the repair/clean bill fails, the postfix continues and eventually falls
through, leaving vanilla's recycle job as `__result`. Correct in all cases.

### Edge case: multiple R4 bills of different types

Bill stack: `[R4 Clean A, R4 Repair B, Vanilla C]`

The postfix finds A first (clean). If `TryCreateCleanBillJob` succeeds, pawn
does A. If it fails (no tainted items), the postfix continues to B (repair).
If B succeeds, pawn does B. If both fail, vanilla's job C passes through.
Bill order is respected across R4 types. ✅

### Edge case: pawn can't do the work type

If a pawn has Smithing disabled but Crafting enabled, and the bench is a smithy,
`JobGiver_Work.PawnCanUseWorkGiver` will skip the vanilla `DoBillsMakeWeapons`
WorkGiver entirely — `JobOnThing` is never called, the postfix never runs.
Correct — the pawn shouldn't work at that bench at all.

### Edge case: bench has no interaction cell (CraftingSpot is a 1×1)

`WorkGiver_DoBill.JobOnThing` checks `thing.def.hasInteractionCell` and
`CanReserveSittableOrSpot`. These checks run before `StartOrResumeBillJob` and
before the postfix. If they fail, `__result` is null and the postfix sees no
R4 bills to process. Correct.

---

## Implementation Sequence

1. **Create `R4BillJobFactory.cs`** — extract `TryCreateRepairBillJob` and
   `TryCreateCleanBillJob` from existing WorkGiver code. Test that the
   extracted methods compile and produce identical jobs.

2. **Create `Patch_R4BillOrdering.cs`** — implement the postfix. Wire it to
   call `R4BillJobFactory` methods.

3. **Test basic ordering** — place interleaved vanilla + R4 bills, verify
   correct execution order with debug logging.

4. **Remove `Patch_WorkGiver_DoBill_Repair.cs`** — its functionality is merged
   into the new postfix.

5. **Remove `WorkGiver_R4RepairBill.cs` and `WorkGiver_R4CleanBill.cs`** —
   their job-creation logic now lives in `R4BillJobFactory`.

6. **Remove 10 bill-based WorkGiverDefs from `WorkGivers.xml`**.

7. **Test CraftingSpot specifically** — verify the `vanillaReturnedR4` path
   works correctly (vanilla sees R4 bills due to matching `workType`).

8. **Test ingredient failure scenarios** — verify cooldowns are set correctly
   and bills are skipped on subsequent scans.

9. **Test with BIS** — verify candidate search still works with BIS storage
   source configured.

10. **Update `DESIGN.md`** — reflect new architecture.

---

## Open Questions

1. **Remove `requiredGiverWorkType=Crafting`?** It provides a performance
   benefit (vanilla skips R4 bills on non-Crafting benches) at the cost of the
   CraftingSpot edge case (double ingredient search). Recommend keeping it, but
   it's not required for correctness.

2. **Cache `HasAnyR4RepairOrCleanBill`?** The per-bench-per-scan check is
   cheap for typical bill stacks. Profile before optimizing.

3. **Float menu ingredient display for R4 bills?** Pre-existing gap. Could be
   addressed in a future pass by extending the postfix to participate in the
   `FloatMenuMakerMap.makingFor` path, but this is polish, not correctness.

4. **Designation-based WorkGivers and bill WorkGivers on the same bench?** If a
   player both designates an item for repair AND has a repair bill on a bench,
   both systems could try to repair the same item. This is a pre-existing
   concern unrelated to the bill ordering fix, but worth noting. The
   designation WorkGiver checks for existing reservations, which should prevent
   double-dispatch, but this should be explicitly tested.
