using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RRRR
{
    public class JobDriver_R4Repair : JobDriver_R4WorkBase
    {
        protected override DesignationDef WorkDesignationDef => R4DefOf.R4_Repair;

        protected override string GetJobReportKey() => "R4_JobReport_Repair";

        protected override bool IsWorkItemStillValid(Thing item)
        {
            return item.def.useHitPoints && item.HitPoints < item.MaxHitPoints;
        }

        protected override float CalculateTotalWork(Thing item)
        {
            float baseWork = item.def.GetStatValueAbstract(StatDefOf.WorkToMake, item.Stuff);
            if (baseWork <= 0f) baseWork = 1000f;
            return Mathf.Clamp(baseWork * 0.05f, 200f, 800f);
        }

        protected override List<ThingDefCountClass> GetCycleCost(Thing item)
        {
            if (WorkGiver_R4Repair.IsMinorMending(item))
                return new List<ThingDefCountClass>();
            return MaterialUtility.GetRepairCycleCost(item);
        }

        protected override bool ShouldContinueWorking(Thing item)
        {
            return item.def.useHitPoints && item.HitPoints < item.MaxHitPoints;
        }

        protected override float GetSkillXpPerTick() => 0.12f;

        protected override float GetSkillSpeedBonus(int skillLevel) => 1f;

        protected override void ApplyWorkResult(Thing item, Pawn worker)
        {
            int skillLevel       = worker?.skills?.GetSkill(SkillDefOf.Crafting)?.Level ?? 0;
            float techDifficulty = SkillUtility.GetTechDifficulty(item.def);
            float successChance  = SkillUtility.RepairSuccessChance(skillLevel, techDifficulty);

            if (Rand.Chance(successChance))
            {
                float hpFraction = RRRR_Mod.Settings.repairHpPerCycle;
                int cycleHP = Mathf.Max(1, Mathf.RoundToInt(item.MaxHitPoints * hpFraction));
                item.HitPoints = Mathf.Min(item.MaxHitPoints, item.HitPoints + cycleHP);
            }
            else
            {
                if (SkillUtility.IsCriticalFailure(item))
                {
                    SkillUtility.ApplyCriticalFailure(item);
                    Messages.Message("R4_RepairCriticalFailure".Translate(worker.LabelShort, item.LabelCap),
                        item, MessageTypeDefOf.NegativeEvent);
                }
                else
                {
                    SkillUtility.ApplyMinorFailure(item);
                    Messages.Message("R4_RepairMinorFailure".Translate(worker.LabelShort, item.LabelCap),
                        item, MessageTypeDefOf.NeutralEvent);
                }
            }
        }

        protected override void OnItemDestroyed(Thing item)
        {
            string itemLabel = item.LabelCap;
            if (!item.Destroyed)
            {
                MaterialUtility.SpawnPartialReclaim(item, pawn, 0.25f, pawn.Position, pawn.Map);
                pawn.Map.designationManager.RemoveAllDesignationsOn(item);
                item.Destroy(DestroyMode.Vanish);
            }
            Messages.Message("R4_RepairItemDestroyed".Translate(itemLabel),
                new TargetInfo(pawn.Position, pawn.Map), MessageTypeDefOf.NegativeEvent);
        }
    }
}
