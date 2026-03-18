using Verse;

namespace RRRR
{
    /// <summary>
    /// CompProperties for the recyclable comp. 
    /// Currently empty — exists so XML injection has a valid target class.
    /// Will hold per-def configuration in later milestones.
    /// </summary>
    public class CompProperties_Recyclable : CompProperties
    {
        public CompProperties_Recyclable()
        {
            compClass = typeof(CompRecyclable);
        }
    }
}
