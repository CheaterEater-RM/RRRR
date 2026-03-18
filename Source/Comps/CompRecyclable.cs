using Verse;

namespace RRRR
{
    /// <summary>
    /// Minimal ThingComp attached to weapons/apparel via XML patch.
    /// MVP: Logs attachment, provides no gameplay yet.
    /// Future: Will track designation state and provide gizmos.
    /// </summary>
    public class CompRecyclable : ThingComp
    {
        // Typed accessor for convenience
        public CompProperties_Recyclable Props => (CompProperties_Recyclable)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            // Only log on fresh spawn, not load — avoids log spam on save load
            if (!respawningAfterLoad)
            {
                Log.Message($"[R4] CompRecyclable attached to: {parent.LabelCap} ({parent.def.defName})");
            }
        }
    }
}
