using RimWorld;
using UnityEngine;
using Verse;

namespace RRRR
{
    /// <summary>
    /// Map-order designator for recycling items (Orders menu / drag-designate).
    /// Recycle is mutually exclusive with Repair and Clean.
    /// Uses recycle-specific eligibility to match the gizmo and bill systems.
    /// </summary>
    public class Designator_RecycleThing : Designator
    {
        protected override DesignationDef Designation => R4DefOf.R4_Recycle;

        public override DrawStyleCategoryDef DrawStyleCategory => DrawStyleCategoryDefOf.FilledRectangle;

        public Designator_RecycleThing()
        {
            defaultLabel = "R4_RecycleLabel".Translate();
            defaultDesc  = "R4_RecycleDesc".Translate();
            icon = ContentFinder<Texture2D>.Get("UI/Designators/R4RecycleMenu", reportFailure: false)
                ?? ContentFinder<Texture2D>.Get("UI/Designators/Haul", reportFailure: true);
            soundDragSustain = SoundDefOf.Designate_DragStandard;
            soundDragChanged = SoundDefOf.Designate_DragStandard_Changed;
            soundSucceeded   = SoundDefOf.Designate_Haul;
            useMouseIcon     = true;
            hasDesignateAllFloatMenuOption = true;
            designateAllLabel = "R4_RecycleLabel".Translate();
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
            return "R4_MustDesignateRecyclable".Translate();
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
            if (!R4WorkbenchFilterCache.IsRecycleEligible(t.def))
                return false;
            if (t.Map == null)
                return false;
            if (base.Map.designationManager.DesignationOn(t, Designation) != null)
                return "R4_AlreadyDesignatedRecycle".Translate();
            return true;
        }

        public override void DesignateThing(Thing t)
        {
            var dm = base.Map.designationManager;
            // Recycle cancels repair and clean
            var repair = dm.DesignationOn(t, R4DefOf.R4_Repair);
            if (repair != null) dm.RemoveDesignation(repair);
            var clean = dm.DesignationOn(t, R4DefOf.R4_Clean);
            if (clean != null) dm.RemoveDesignation(clean);
            dm.AddDesignation(new Designation(t, Designation));
        }

        public override void SelectedUpdate()
        {
            GenUI.RenderMouseoverBracket();
        }
    }
}
