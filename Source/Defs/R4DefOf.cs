using RimWorld;
using Verse;

namespace RRRR
{
    [DefOf]
    public static class R4DefOf
    {
        public static DesignationDef R4_Recycle;
        public static DesignationDef R4_Repair;

        public static JobDef RRRR_Recycle;
        public static JobDef RRRR_Repair;

        static R4DefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(R4DefOf));
        }
    }
}
