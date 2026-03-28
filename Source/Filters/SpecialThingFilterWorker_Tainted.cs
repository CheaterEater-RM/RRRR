using RimWorld;
using Verse;

namespace RRRR
{
    /// <summary>
    /// Filter for tainted apparel. Used by the clean bill to only target
    /// corpse-tainted items.
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
}
