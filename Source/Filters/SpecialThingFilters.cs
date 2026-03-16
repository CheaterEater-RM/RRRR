using RimWorld;
using Verse;

namespace RRRR
{
    /// <summary>
    /// Filter: matches only tainted apparel.
    /// </summary>
    public class SpecialThingFilterWorker_Tainted : SpecialThingFilterWorker
    {
        public override bool Matches(Thing t)
        {
            return t is Apparel apparel && apparel.WornByCorpse;
        }

        public override bool CanEverMatch(ThingDef def)
        {
            return def.IsApparel;
        }
    }

    /// <summary>
    /// Filter: matches only items with HP below max.
    /// </summary>
    public class SpecialThingFilterWorker_Damaged : SpecialThingFilterWorker
    {
        public override bool Matches(Thing t)
        {
            return t.def.useHitPoints && t.HitPoints < t.MaxHitPoints;
        }

        public override bool CanEverMatch(ThingDef def)
        {
            return def.useHitPoints;
        }
    }

    /// <summary>
    /// Filter: matches items marked for recycle via gizmo.
    /// </summary>
    public class SpecialThingFilterWorker_MarkedRecycle : SpecialThingFilterWorker
    {
        public override bool Matches(Thing t)
        {
            var comp = t.TryGetComp<CompRecyclable>();
            return comp != null && comp.Designation == R4Designation.MarkedRecycle;
        }

        public override bool CanEverMatch(ThingDef def)
        {
            return def.IsApparel || def.IsWeapon;
        }
    }

    /// <summary>
    /// Filter: matches items marked for repair via gizmo.
    /// </summary>
    public class SpecialThingFilterWorker_MarkedRepair : SpecialThingFilterWorker
    {
        public override bool Matches(Thing t)
        {
            var comp = t.TryGetComp<CompRecyclable>();
            return comp != null && comp.Designation == R4Designation.MarkedRepair;
        }

        public override bool CanEverMatch(ThingDef def)
        {
            return def.IsApparel || def.IsWeapon;
        }
    }

    /// <summary>
    /// Filter: matches items marked for cleaning via gizmo.
    /// </summary>
    public class SpecialThingFilterWorker_MarkedClean : SpecialThingFilterWorker
    {
        public override bool Matches(Thing t)
        {
            var comp = t.TryGetComp<CompRecyclable>();
            return comp != null && comp.Designation == R4Designation.MarkedClean;
        }

        public override bool CanEverMatch(ThingDef def)
        {
            return def.IsApparel;
        }
    }
}
