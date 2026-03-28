using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RRRR
{
    /// <summary>
    /// RecipeWorker for bill-based repair. Works with vanilla's JobDriver_DoBill.
    /// The damaged item is the "ingredient". We don't destroy it — instead we
    /// run one repair cycle (skill check, HP restoration, failure mechanics)
    /// and consume repair materials from nearby map stacks.
    /// 
    /// Minor mending: items at ≥95% HP are repaired for free.
    /// 
    /// After completion, the item stays on the bench. If still damaged,
    /// the bill will pick it up again for another cycle.
    /// </summary>
    public class RecipeWorker_R4Repair : RecipeWorker
    {
        public override void ConsumeIngredient(Thing ingredient, RecipeDef recipe, Map map)
        {
            if (ingredient.def.IsWeapon || ingredient.def.IsApparel)
                return;

            base.ConsumeIngredient(ingredient, recipe, map);
        }

        public override void Notify_IterationCompleted(Pawn billDoer, List<Thing> ingredients)
        {
            if (billDoer == null || billDoer.Map == null)
                return;

            for (int i = 0; i < ingredients.Count; i++)
            {
                Thing item = ingredients[i];
                if (item == null || item.Destroyed)
                    continue;
                if (!item.def.IsWeapon && !item.def.IsApparel)
                    continue;
                if (!item.def.useHitPoints)
                    continue;
                if (item.HitPoints >= item.MaxHitPoints)
                    continue;

                Map map = billDoer.Map;

                // Consume repair materials (unless minor mending)
                bool isMinorMending = WorkGiver_R4Repair.IsMinorMending(item);
                if (!isMinorMending)
                {
                    var cycleCost = MaterialUtility.GetRepairCycleCost(item);
                    if (cycleCost.Count > 0)
                    {
                        if (!TryConsumeRepairMaterials(cycleCost, billDoer, map))
                        {
                            Messages.Message(
                                "R4_RepairNoMaterials".Translate(item.LabelCap),
                                item, MessageTypeDefOf.RejectInput);
                            continue;
                        }
                    }
                }

                // Skill check
                int skillLevel = billDoer?.skills?.GetSkill(SkillDefOf.Crafting)?.Level ?? 0;
                float techDifficulty = SkillUtility.GetTechDifficulty(item.def);
                float successChance = SkillUtility.RepairSuccessChance(skillLevel, techDifficulty);

                if (Rand.Chance(successChance))
                {
                    int cycleHP = Mathf.Max(1, Mathf.RoundToInt(item.MaxHitPoints * 0.20f));
                    item.HitPoints = Mathf.Min(item.MaxHitPoints, item.HitPoints + cycleHP);
                }
                else
                {
                    if (SkillUtility.IsCriticalFailure(item))
                    {
                        SkillUtility.ApplyCriticalFailure(item);
                        Messages.Message("R4_RepairCriticalFailure".Translate(billDoer.LabelShort, item.LabelCap),
                            item, MessageTypeDefOf.NegativeEvent);
                    }
                    else
                    {
                        SkillUtility.ApplyMinorFailure(item);
                        Messages.Message("R4_RepairMinorFailure".Translate(billDoer.LabelShort, item.LabelCap),
                            item, MessageTypeDefOf.NeutralEvent);
                    }
                }

                // Handle destruction from critical failure
                if (item.HitPoints <= 0 && !item.Destroyed)
                {
                    string itemLabel = item.LabelCap;
                    MaterialUtility.SpawnPartialReclaim(item, billDoer, 0.25f, billDoer.Position, map);
                    map.designationManager.RemoveAllDesignationsOn(item);
                    item.Destroy(DestroyMode.Vanish);
                    Messages.Message("R4_RepairItemDestroyed".Translate(itemLabel),
                        new TargetInfo(billDoer.Position, map), MessageTypeDefOf.NegativeEvent);
                    continue;
                }

                // Remove designation if present
                if (item.HitPoints >= item.MaxHitPoints && item.Map != null)
                {
                    var des = item.Map.designationManager.DesignationOn(item, R4DefOf.R4_Repair);
                    if (des != null)
                        item.Map.designationManager.RemoveDesignation(des);
                }
            }
        }

        private bool TryConsumeRepairMaterials(List<ThingDefCountClass> costs, Pawn pawn, Map map)
        {
            for (int i = 0; i < costs.Count; i++)
            {
                int available = CountAvailableOnMap(costs[i].thingDef, map, pawn);
                if (available < costs[i].count)
                    return false;
            }
            for (int i = 0; i < costs.Count; i++)
            {
                ConsumeFromMap(costs[i].thingDef, costs[i].count, map, pawn);
            }
            return true;
        }

        private int CountAvailableOnMap(ThingDef matDef, Map map, Pawn pawn)
        {
            int total = 0;
            var things = map.listerThings.ThingsOfDef(matDef);
            if (things == null) return 0;
            for (int i = 0; i < things.Count; i++)
            {
                if (!things[i].IsForbidden(pawn))
                    total += things[i].stackCount;
            }
            return total;
        }

        private void ConsumeFromMap(ThingDef matDef, int amount, Map map, Pawn pawn)
        {
            int remaining = amount;
            var things = map.listerThings.ThingsOfDef(matDef);
            if (things == null) return;
            var sorted = new List<Thing>(things);
            sorted.Sort((a, b) =>
                a.Position.DistanceToSquared(pawn.Position)
                .CompareTo(b.Position.DistanceToSquared(pawn.Position)));
            for (int i = 0; i < sorted.Count && remaining > 0; i++)
            {
                Thing t = sorted[i];
                if (t.IsForbidden(pawn)) continue;
                int take = Mathf.Min(remaining, t.stackCount);
                t.SplitOff(take).Destroy();
                remaining -= take;
            }
        }
    }
}
