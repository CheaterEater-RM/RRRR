using RimWorld;
using UnityEngine;
using Verse;

namespace RRRR
{
    /// <summary>
    /// Map-order designator for repairing damaged items.
    /// Only accepts items below max HP. Supports drag-select.
    /// </summary>
    public class Designator_RepairThing : Designator
    {
        protected override DesignationDef Designation => R4DefOf.R4_Repair;

        public Designator_RepairThing()
        {
            defaultLabel = "R4_RepairLabel".Translate();
            defaultDesc = "R4_RepairDesc".Translate();
            icon = ContentFinder<Texture2D>.Get("UI/Designators/R4RepairDesignation", reportFailure: false);
            if (icon == null)
            {
                icon = ContentFinder<Texture2D>.Get("UI/Designators/Claim", reportFailure: true);
            }
            soundDragSustain = SoundDefOf.Designate_DragStandard;
            soundDragChanged = SoundDefOf.Designate_DragStandard_Changed;
            soundSucceeded = SoundDefOf.Designate_Haul;
            useMouseIcon = true;
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
            if (t.def == null || t.Map == null)
                return false;

            if (!t.def.IsWeapon && !t.def.IsApparel)
                return false;

            // Must be damaged
            if (!t.def.useHitPoints || t.HitPoints >= t.MaxHitPoints)
                return "R4_NotDamaged".Translate();

            // Not already designated for repair
            if (base.Map.designationManager.DesignationOn(t, Designation) != null)
                return "R4_AlreadyDesignatedRepair".Translate();

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
