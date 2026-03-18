using RimWorld;
using Verse;

namespace RRRR
{
    /// <summary>
    /// DefOf references for R4 custom defs.
    /// RimWorld auto-populates these static fields from the DefDatabase
    /// after Defs load, matching field names to defNames.
    /// </summary>
    [DefOf]
    public static class R4DefOf
    {
        public static DesignationDef R4_Recycle;

        static R4DefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(R4DefOf));
        }
    }
}
