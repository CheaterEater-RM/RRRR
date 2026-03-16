using RimWorld;
using Verse;

namespace RRRR
{
    [DefOf]
    public static class R4DefOf
    {
        public static JobDef RRRR_Recycle;
        // M2: public static JobDef RRRR_Repair;
        // M3: public static JobDef RRRR_Clean;

        static R4DefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(R4DefOf));
        }
    }
}
