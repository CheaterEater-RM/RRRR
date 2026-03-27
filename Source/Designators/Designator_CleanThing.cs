using RimWorld;
using UnityEngine;
using Verse;

namespace RRRR
{
    /// <summary>
    /// Map-order designator for cleaning taint from apparel.
    /// Only accepts tainted apparel (WornByCorpse). Supports drag-select.
    /// </summary>
    public class Designator_CleanThing : Designator
    {
        protected override DesignationDef Designation => R4DefOf.R4_Clean;

        public Designator_CleanThing()
        {
            defaultLabel = "R4_CleanLabel".Translate();
            defaultDesc = "R4_CleanDesc".Translate();
            icon = ContentFinder<Texture2D>.Get("UI/Designators/R4CleanDesignation", reportFailure: false);
            if (icon == null)
            {
                icon = ContentFinder<Texture2D>.Get("UI/Designators/Unforbid", reportFailure: true);
            }
            soundDragSustain = SoundDefOf.Designate_DragStandard;
            soundDragChanged = SoundDefOf.Designate_DragStandard_Changed;
            soundSucceeded = SoundDefOf.Designate_Haul;
            useMouseIcon = true;
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
            if (t.def == null || t.Map == null)
                return false;

            // Must be tainted apparel
            if (!(t is Apparel apparel))
                return false;

            if (!apparel.WornByCorpse)
                return "R4_NotTainted".Translate();

            // Not already designated for cleaning
            if (base.Map.designationManager.DesignationOn(t, Designation) != null)
                return "R4_AlreadyDesignatedClean".Translate();

            // Not designated for recycle (conflicting)
            if (base.Map.designationManager.DesignationOn(t, R4DefOf.R4_Recycle) != null)
                return "R4_AlreadyDesignatedRecycle".Translate();

            return true;
        }

        public override void DesignateThing(Thing t)
        {
            base.Map.designationManager.AddDesignation(new Designation(t, Designation));
        }

        public override void SelectedUpdate()
        {
            GenUI.RenderMouseoverBracket();
        }
    }
}
