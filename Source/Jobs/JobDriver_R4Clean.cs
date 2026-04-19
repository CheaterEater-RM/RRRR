using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RRRR
{
    public class JobDriver_R4Clean : JobDriver_R4WorkBase
    {
        protected override DesignationDef WorkDesignationDef => R4DefOf.R4_Clean;

        protected override string GetJobReportKey() => "R4_JobReport_Clean";

        protected override bool IsWorkItemStillValid(Thing item)
        {
            return item is Apparel apparel && apparel.WornByCorpse;
        }

        protected override float CalculateTotalWork(Thing item)
        {
            float workToMake = item.def.GetStatValueAbstract(StatDefOf.WorkToMake, item.Stuff);
            if (workToMake <= 0f) workToMake = 1000f;
            return Mathf.Clamp(workToMake * 0.15f, 300f, 1500f);
        }

        protected override List<ThingDefCountClass> GetCycleCost(Thing item)
        {
            return MaterialUtility.GetCleanCost(item);
        }

        protected override bool ShouldContinueWorking(Thing item)
        {
            // Single-shot operation — never continue after one cycle
            return false;
        }

        protected override float GetSkillXpPerTick() => 0.08f;

        protected override float GetSkillSpeedBonus(int skillLevel)
        {
            return 1f + (skillLevel * 0.03f);
        }

        protected override void ApplyWorkResult(Thing item, Pawn worker)
        {
            if (item is Apparel apparel)
            {
                apparel.WornByCorpse = false;
                apparel.Notify_ColorChanged();
            }
        }
    }
}
