using System.Collections.Generic;
using Verse;

namespace RRRR
{
    public class R4EligibilityExclusionDef : Def
    {
        // Explicit per-def blacklist — the escape hatch for untagged outliers.
        public List<ThingDef> excludedThingDefs;

        // Trade tags that auto-exclude any gear carrying them. Default: "AnimalPart",
        // the vanilla-authored marker on natural resource-weapons (horns, tusks) — a
        // semantic label, not a proxy, so it also covers modded animal-part gear.
        public List<string> excludedTradeTags;
    }
}