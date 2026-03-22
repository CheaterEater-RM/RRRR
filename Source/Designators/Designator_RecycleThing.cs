using RimWorld;
using UnityEngine;
using Verse;

namespace RRRR
{
    /// <summary>
    /// Map-order designator for recycling items.
    /// Appears in the Orders tab and as a gizmo on selected items.
    /// Supports both click-to-designate and drag-to-designate.
    /// </summary>
    public class Designator_RecycleThing : Designator
    {
        protected override DesignationDef Designation => R4DefOf.R4_Recycle;

        public Designator_RecycleThing()
        {
            defaultLabel = "R4_RecycleLabel".Translate();
            defaultDesc = "R4_RecycleDesc".Translate();
            icon = ContentFinder<Texture2D>.Get("UI/Designators/R4RecycleDesignation", reportFailure: false);
            if (icon == null)
            {
                Log.Warning("[R4] Custom recycle texture not found, using fallback icon.");
                icon = ContentFinder<Texture2D>.Get("UI/Designators/Haul", reportFailure: true);
            }
            soundDragSustain = SoundDefOf.Designate_DragStandard;
            soundDragChanged = SoundDefOf.Designate_DragStandard_Changed;
            soundSucceeded = SoundDefOf.Designate_Haul;
            useMouseIcon = true;
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
            // Designate ALL matching items in this cell, not just the first.
            // This makes drag-select work properly over stockpiles with multiple items.
            var things = c.GetThingList(base.Map);
            for (int i = things.Count - 1; i >= 0; i--)
            {
                if (CanDesignateThing(things[i]).Accepted)
                {
                    DesignateThing(things[i]);
                }
            }
        }

        public override AcceptanceReport CanDesignateThing(Thing t)
        {
            if (t.def == null || t.Map == null)
                return false;

            if (!t.def.IsWeapon && !t.def.IsApparel)
                return false;

            if (base.Map.designationManager.DesignationOn(t, Designation) != null)
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
