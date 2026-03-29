using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RRRR
{
    /// <summary>
    /// Postfix on Thing.GetGizmos to inject per-item R4 action gizmos when a
    /// weapon or apparel is selected. Uses Command_Action for dynamic per-item
    /// descriptions showing bench routing, material costs, and success chance.
    ///
    /// Mutual exclusivity (silent toggle, no locking):
    ///   Clicking Recycle while Repair/Clean are active cancels them, and vice
    ///   versa. Buttons are never disabled — they always show the full tooltip.
    ///   Cancel state shows the same rich description as the active state, plus
    ///   a note that the designation is already set.
    ///
    ///   Repair and Clean can coexist (a tainted damaged item may need both).
    ///
    /// Eligibility uses IsR4Eligible, which requires smeltable=true, excluding
    /// improvised-weapon items like beer and wood logs.
    ///
    /// All cost/success estimates assume Crafting skill 10, stated explicitly.
    /// </summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.GetGizmos))]
    public static class Patch_Thing_GetGizmos
    {
        private const int EstimateSkillLevel = 10;

        static void Postfix(Thing __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!R4WorkbenchFilterCache.IsR4Eligible(__instance.def))
                return;
            if (__instance.Map == null)
                return;

            // Dropped items have Faction == null; only reject items belonging
            // to a non-player faction.
            Faction f = __instance.Faction;
            if (f != null && f != Faction.OfPlayer)
                return;

            var extra = new List<Gizmo>();
            DesignationManager dm = __instance.Map.designationManager;

            BuildRecycleGizmo(__instance, dm, extra);
            BuildRepairGizmo(__instance, dm, extra);
            BuildCleanGizmo(__instance, dm, extra);

            if (extra.Count == 0)
                return;

            __result = AppendGizmos(__result, extra);
        }

        // ── Recycle ───────────────────────────────────────────────────────────

        static void BuildRecycleGizmo(Thing t, DesignationManager dm, List<Gizmo> out_list)
        {
            bool already = dm.DesignationOn(t, R4DefOf.R4_Recycle) != null;
            string richDesc = BuildRecycleDesc(t);

            // Cancel state: show the same rich desc + cancellation note
            string desc = already
                ? richDesc + "\n\n" + "R4_Desc_AlreadyDesignated".Translate().ToString()
                : richDesc;

            var cmd = new Command_Action
            {
                defaultLabel = already
                    ? "R4_CancelRecycleLabel".Translate().ToString()
                    : "R4_RecycleLabel".Translate().ToString(),
                icon        = ContentFinder<Texture2D>.Get("UI/Designators/R4RecycleMenu", reportFailure: false)
                              ?? BaseContent.BadTex,
                defaultDesc = desc,
                action      = delegate
                {
                    if (already)
                    {
                        RemoveDesignationIfPresent(dm, t, R4DefOf.R4_Recycle);
                    }
                    else
                    {
                        // Recycle cancels repair and clean
                        RemoveDesignationIfPresent(dm, t, R4DefOf.R4_Repair);
                        RemoveDesignationIfPresent(dm, t, R4DefOf.R4_Clean);
                        dm.AddDesignation(new Designation(t, R4DefOf.R4_Recycle));
                    }
                }
            };

            out_list.Add(cmd);
        }

        // ── Repair ────────────────────────────────────────────────────────────

        static void BuildRepairGizmo(Thing t, DesignationManager dm, List<Gizmo> out_list)
        {
            bool canRepair   = t.def.useHitPoints && t.HitPoints < t.MaxHitPoints;
            bool already     = dm.DesignationOn(t, R4DefOf.R4_Repair) != null;

            string richDesc = canRepair
                ? BuildRepairDesc(t)
                : "R4_NotDamaged".Translate().ToString();

            string desc = already
                ? richDesc + "\n\n" + "R4_Desc_AlreadyDesignated".Translate().ToString()
                : richDesc;

            var cmd = new Command_Action
            {
                defaultLabel = already
                    ? "R4_CancelRepairLabel".Translate().ToString()
                    : "R4_RepairLabel".Translate().ToString(),
                icon        = ContentFinder<Texture2D>.Get("UI/Designators/R4RepairMenu", reportFailure: false)
                              ?? BaseContent.BadTex,
                defaultDesc = desc,
                action      = delegate
                {
                    if (already)
                    {
                        RemoveDesignationIfPresent(dm, t, R4DefOf.R4_Repair);
                    }
                    else if (canRepair)
                    {
                        // Repair cancels recycle
                        RemoveDesignationIfPresent(dm, t, R4DefOf.R4_Recycle);
                        dm.AddDesignation(new Designation(t, R4DefOf.R4_Repair));
                    }
                }
            };

            // Only disable if the item literally can't be repaired AND isn't already designated
            if (!canRepair && !already)
                cmd.Disable("R4_NotDamaged".Translate().ToString());

            out_list.Add(cmd);
        }

        // ── Clean ─────────────────────────────────────────────────────────────

        static void BuildCleanGizmo(Thing t, DesignationManager dm, List<Gizmo> out_list)
        {
            if (!(t is Apparel apparel))
                return;

            bool tainted = apparel.WornByCorpse;
            bool already = dm.DesignationOn(t, R4DefOf.R4_Clean) != null;

            string richDesc = tainted
                ? BuildCleanDesc(t)
                : "R4_NotTainted".Translate().ToString();

            string desc = already
                ? richDesc + "\n\n" + "R4_Desc_AlreadyDesignated".Translate().ToString()
                : richDesc;

            var cmd = new Command_Action
            {
                defaultLabel = already
                    ? "R4_CancelCleanLabel".Translate().ToString()
                    : "R4_CleanLabel".Translate().ToString(),
                icon        = ContentFinder<Texture2D>.Get("UI/Designators/R4CleanMenu", reportFailure: false)
                              ?? BaseContent.BadTex,
                defaultDesc = desc,
                action      = delegate
                {
                    if (already)
                    {
                        RemoveDesignationIfPresent(dm, t, R4DefOf.R4_Clean);
                    }
                    else if (tainted)
                    {
                        // Clean cancels recycle
                        RemoveDesignationIfPresent(dm, t, R4DefOf.R4_Recycle);
                        dm.AddDesignation(new Designation(t, R4DefOf.R4_Clean));
                    }
                }
            };

            // Only disable if the item isn't tainted AND isn't already designated
            if (!tainted && !already)
                cmd.Disable("R4_NotTainted".Translate().ToString());

            out_list.Add(cmd);
        }

        // ── Description builders ──────────────────────────────────────────────

        static string BuildRecycleDesc(Thing t)
        {
            var sb = new StringBuilder();
            sb.AppendLine("R4_RecycleDesc_Short".Translate().ToString());
            sb.AppendLine();
            AppendBenchLine(sb, t, "R4_Desc_RecycleAt".Translate().ToString());

            float returnPct = MaterialUtility.CalculateReturnPercent(t, EstimateSkillLevel);
            var costList = t.def.CostListAdjusted(t.Stuff, errorOnNullStuff: false);
            if (costList != null && costList.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("R4_Desc_EstimatedReturns".Translate(EstimateSkillLevel).ToString());
                for (int i = 0; i < costList.Count; i++)
                {
                    var entry = costList[i];
                    if (entry.thingDef == null || entry.count <= 0) continue;
                    if (RRRR_Mod.Settings.skipIntricateComponents && entry.thingDef.intricate) continue;
                    float materialPct = MaterialUtility.GetMaterialReturnPct(entry.thingDef, returnPct);
                    int count = Mathf.RoundToInt(entry.count * materialPct);
                    sb.AppendLine($"  {entry.thingDef.LabelCap}: ~{count}");
                }
            }

            return sb.ToString().TrimEnd();
        }

        static string BuildRepairDesc(Thing t)
        {
            var sb = new StringBuilder();
            sb.AppendLine("R4_RepairDesc_Short".Translate().ToString());
            sb.AppendLine();
            AppendBenchLine(sb, t, "R4_Desc_RepairAt".Translate().ToString());

            if (t.def.useHitPoints)
            {
                float hpPct = (float)t.HitPoints / t.MaxHitPoints;
                sb.AppendLine($"{"R4_Desc_Condition".Translate()}: {t.HitPoints}/{t.MaxHitPoints} ({hpPct:P0})");
            }

            if (WorkGiver_R4Repair.IsMinorMending(t))
            {
                sb.AppendLine();
                sb.AppendLine("R4_Desc_MinorMending".Translate().ToString());
                return sb.ToString().TrimEnd();
            }

            var cycleCost = MaterialUtility.GetRepairCycleCost(t);
            if (cycleCost.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("R4_Desc_CostPerCycle".Translate().ToString());
                for (int i = 0; i < cycleCost.Count; i++)
                    sb.AppendLine($"  {cycleCost[i].thingDef.LabelCap}: {cycleCost[i].count}");
            }
            else
            {
                sb.AppendLine();
                sb.AppendLine("R4_Desc_FreeCycles".Translate().ToString());
            }

            float techDiff   = SkillUtility.GetTechDifficulty(t.def);
            float successPct = SkillUtility.RepairSuccessChance(EstimateSkillLevel, techDiff);
            sb.AppendLine();
            sb.AppendLine("R4_Desc_SuccessChance".Translate(EstimateSkillLevel, successPct.ToStringPercent()).ToString());

            return sb.ToString().TrimEnd();
        }

        static string BuildCleanDesc(Thing t)
        {
            var sb = new StringBuilder();
            sb.AppendLine("R4_CleanDesc_Short".Translate().ToString());
            sb.AppendLine();
            AppendBenchLine(sb, t, "R4_Desc_CleanAt".Translate().ToString());

            var cleanCost = MaterialUtility.GetCleanCost(t);
            if (cleanCost.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("R4_Desc_CleanCost".Translate().ToString());
                for (int i = 0; i < cleanCost.Count; i++)
                    sb.AppendLine($"  {cleanCost[i].thingDef.LabelCap}: {cleanCost[i].count}");
            }
            else
            {
                sb.AppendLine();
                sb.AppendLine("R4_Desc_FreeCycles".Translate().ToString());
            }

            return sb.ToString().TrimEnd();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        static void AppendBenchLine(StringBuilder sb, Thing t, string prefix)
        {
            var benchDefs = WorkbenchRouter.GetValidBenches(t);
            if (benchDefs == null || benchDefs.Count == 0)
            {
                sb.AppendLine($"{prefix}: {"R4_Desc_NoBench".Translate()}");
                return;
            }

            var seen   = new HashSet<string>();
            var labels = new List<string>();
            for (int i = 0; i < benchDefs.Count; i++)
            {
                string lbl = benchDefs[i].LabelCap;
                if (seen.Add(lbl))
                    labels.Add(lbl);
            }

            sb.AppendLine($"{prefix}: {labels.ToCommaList()}");
        }

        static void RemoveDesignationIfPresent(DesignationManager dm, Thing t, DesignationDef def)
        {
            var des = dm.DesignationOn(t, def);
            if (des != null) dm.RemoveDesignation(des);
        }

        static IEnumerable<Gizmo> AppendGizmos(IEnumerable<Gizmo> original, List<Gizmo> extra)
        {
            foreach (Gizmo g in original) yield return g;
            foreach (Gizmo g in extra)    yield return g;
        }
    }
}
