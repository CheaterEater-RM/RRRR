using RimWorld;
using UnityEngine;
using Verse;

namespace RRRR
{
    /// <summary>
    /// Map-order designator for repairing damaged items (Orders menu / drag-designate).
    /// Repair cancels any pending recycle designation.
    /// Uses IsR4Eligible to match the same eligibility as the gizmo system.
    /// </summary>
    public class Designator_RepairThing : Designator
    {
        protected override DesignationDef Designation => R4DefOf.R4_Repair;

        public override DrawStyleCategoryDef DrawStyleCategory => DrawStyleCategoryDefOf.FilledRectangle;

        public Designator_RepairThing()
        {
            defaultLabel = "R4_RepairLabel".Translate();
            defaultDesc  = "R4_RepairDesc".Translate();
            icon = ContentFinder<Texture2D>.Get("UI/Designators/R4RepairMenu", reportFailure: false)
                ?? ContentFinder<Texture2D>.Get("UI/Designators/Claim", reportFailure: true);
            soundDragSustain = SoundDefOf.Designate_DragStandard;
            soundDragChanged = SoundDefOf.Designate_DragStandard_Changed;
            soundSucceeded   = SoundDefOf.Designate_Haul;
            useMouseIcon     = true;
            hasDesignateAllFloatMenuOption = true;
            designateAllLabel = "R4_RepairLabel".Translate();
        }

        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            if (!c.InBounds(base.Map) || c.Fogged(base.Map))
                return false;
            var things = c.GetThingList(base.Map);
            for (int i = 0; i < things.Count; i++)
            {
                if (CanDesignateThing(things[i]).Accepted)
                    return true;
            }
            return "R4_MustDesignateRepairable".Translate();
        }

        public override void DesignateSingleCell(IntVec3 c)
        {
            var things = c.GetThingList(base.Map);
            for (int i = things.Count - 1; i >= 0; i--)
            {
                if (CanDesignateThing(things[i]).Accepted)
                    DesignateThing(things[i]);
            }
        }

        public override AcceptanceReport CanDesignateThing(Thing t)
        {
            if (!R4WorkbenchFilterCache.IsR4Eligible(t.def))
                return false;
            if (t.Map == null)
                return false;
            if (!t.def.useHitPoints || t.HitPoints >= t.MaxHitPoints)
                return "R4_NotDamaged".Translate();
            if (base.Map.designationManager.DesignationOn(t, Designation) != null)
                return "R4_AlreadyDesignatedRepair".Translate();
            return true;
        }

        public override void DesignateThing(Thing t)
        {
            var dm = base.Map.designationManager;
            // Repair cancels recycle
            var recycle = dm.DesignationOn(t, R4DefOf.R4_Recycle);
            if (recycle != null) dm.RemoveDesignation(recycle);
            dm.AddDesignation(new Designation(t, Designation));
        }

        public override void SelectedUpdate()
        {
            GenUI.RenderMouseoverBracket();
        }
    }
}
