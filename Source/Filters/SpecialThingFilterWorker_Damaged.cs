using Verse;

namespace RRRR
{
    /// <summary>
    /// Filter for damaged items (HP &lt; MaxHP). Used by repair bills to
    /// only target items that actually need repair.
    /// </summary>
    public class SpecialThingFilterWorker_Damaged : SpecialThingFilterWorker
    {
        public override bool Matches(Thing t)
        {
            if (!t.def.useHitPoints)
                return false;
            return t.HitPoints < t.MaxHitPoints;
        }

        public override bool CanEverMatch(ThingDef def)
        {
            return def.useHitPoints;
        }
    }
}
