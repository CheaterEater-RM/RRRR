using RimWorld;
using UnityEngine;
using Verse;

namespace RRRR
{
    /// <summary>
    /// Map-order designator for recycling items.
    /// Appears in the Orders tab and as a gizmo on selected items.
    /// MVP: Places designation, no job execution yet.
    /// </summary>
    public class Designator_RecycleThing : Designator
    {
        protected override DesignationDef Designation => R4DefOf.R4_Recycle;

        public Designator_RecycleThing()
        {
            defaultLabel = "R4_RecycleLabel".Translate();
            defaultDesc = "R4_RecycleDesc".Translate();
            // Use a built-in icon as fallback — the custom texture may not load on first attempt
            icon = ContentFinder<Texture2D>.Get("UI/Designators/R4RecycleDesignation", reportFailure: false);
            if (icon == null)
            {
                // Fallback to a vanilla icon so the designator still works
                Log.Warning("[R4] Custom recycle texture not found, using fallback icon.");
                icon = ContentFinder<Texture2D>.Get("UI/Designators/Haul", reportFailure: true);
            }
            else
            {
                Log.Message("[R4] Designator_RecycleThing: Custom texture loaded OK.");
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
            {
                return false;
            }

            // Look for the first recyclable item at this cell
            var things = c.GetThingList(base.Map);
            for (int i = 0; i < things.Count; i++)
            {
                if (CanDesignateThing(things[i]).Accepted)
                {
                    return true;
                }
            }

            return "R4_MustDesignateRecyclable".Translate();
        }

        public override void DesignateSingleCell(IntVec3 c)
        {
            var things = c.GetThingList(base.Map);
            for (int i = 0; i < things.Count; i++)
            {
                if (CanDesignateThing(things[i]).Accepted)
                {
                    DesignateThing(things[i]);
                    return;
                }
            }
        }

        public override AcceptanceReport CanDesignateThing(Thing t)
        {
            // Must be an item on the map (not held by pawn, not in inventory)
            if (t.def == null || t.Map == null)
            {
                return false;
            }

            // Must be a weapon or apparel
            if (!t.def.IsWeapon && !t.def.IsApparel)
            {
                return false;
            }

            // Already designated?
            if (base.Map.designationManager.DesignationOn(t, Designation) != null)
            {
                return "R4_AlreadyDesignatedRecycle".Translate();
            }

            return true;
        }

        public override void DesignateThing(Thing t)
        {
            base.Map.designationManager.AddDesignation(new Designation(t, Designation));
            Log.Message($"[R4] Designated for recycle: {t.LabelCap} ({t.def.defName})");
        }

        public override void SelectedUpdate()
        {
            GenUI.RenderMouseoverBracket();
        }
    }
}
