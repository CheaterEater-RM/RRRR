using RimWorld;
using UnityEngine;
using Verse;

namespace RRRR
{
    /// <summary>
    /// Map-order designator for cleaning tainted apparel (Orders menu / drag-designate).
    /// Clean cancels any pending recycle designation.
    /// Uses clean-specific eligibility to match the gizmo and bill systems.
    /// </summary>
    public class Designator_CleanThing : Designator
    {
        protected override DesignationDef Designation => R4DefOf.R4_Clean;

        public override DrawStyleCategoryDef DrawStyleCategory => DrawStyleCategoryDefOf.FilledRectangle;

        public Designator_CleanThing()
        {
            defaultLabel = "R4_CleanLabel".Translate();
            defaultDesc  = "R4_CleanDesc".Translate();
            icon = ContentFinder<Texture2D>.Get("UI/Designators/R4CleanMenu", reportFailure: false)
                ?? ContentFinder<Texture2D>.Get("UI/Designators/Unforbid", reportFailure: true);
            soundDragSustain = SoundDefOf.Designate_DragStandard;
            soundDragChanged = SoundDefOf.Designate_DragStandard_Changed;
            soundSucceeded   = SoundDefOf.Designate_Haul;
            useMouseIcon     = true;
            hasDesignateAllFloatMenuOption = true;
            designateAllLabel = "R4_CleanLabel".Translate();
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
            return "R4_MustDesignateTainted".Translate();
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
            if (!R4WorkbenchFilterCache.IsCleanEligible(t.def))
                return false;
            if (t.Map == null)
                return false;
            if (!(t is Apparel apparel) || !apparel.WornByCorpse)
                return "R4_NotTainted".Translate();
            if (base.Map.designationManager.DesignationOn(t, Designation) != null)
                return "R4_AlreadyDesignatedClean".Translate();
            return true;
        }

        public override void DesignateThing(Thing t)
        {
            var dm = base.Map.designationManager;
            // Clean cancels recycle
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
