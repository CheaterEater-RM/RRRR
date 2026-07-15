using System.Collections.Generic;
using Verse;

namespace RRRR
{
    /// <summary>
    /// Optional per-item override for R4's material basis. When present on a ThingDef,
    /// its <see cref="materials"/> list is used as the item's synthetic full-rebuild
    /// cost — driving recycle recovery, repair-cycle cost, and clean cost alike —
    /// in place of the MarketValue fallback R4 would otherwise derive.
    ///
    /// This is a hand-tuning hook only: uncraftable gear already gets a MarketValue
    /// fallback automatically, so an extension is needed solely when that guess is a
    /// poor fit for a specific item (e.g. a modded ultratech weapon). Exclusion is a
    /// separate concern handled by the eligibility blacklist (R4EligibilityExclusionDef),
    /// never here.
    /// </summary>
    public class R4MaterialCostExtension : DefModExtension
    {
        public List<ThingDefCountClass> materials;
    }
}
