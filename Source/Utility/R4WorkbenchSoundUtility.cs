using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RRRR
{
    public static class R4WorkbenchSoundUtility
    {
        private const string SmithSoundDefName = "Recipe_Smith";
        private const string TailorSoundDefName = "Recipe_Tailor";

        private static readonly HashSet<string> TailorBenchDefNames = new HashSet<string>
        {
            "HandTailoringBench",
            "ElectricTailoringBench",
        };

        private static SoundDef _smithSound;
        private static SoundDef _tailorSound;

        public static SoundDef WorkingSoundFor(Job job, Thing bench, Thing workItem)
        {
            SoundDef billSound = job?.bill?.recipe?.soundWorking;
            if (billSound != null)
                return billSound;

            if (job?.def == R4DefOf.RRRR_Clean)
                return TailorSound();

            SoundDef recipeSound = SoundFromBenchRecipes(bench, workItem);
            if (recipeSound != null)
                return recipeSound;

            return SoundFromBenchAndItem(job, bench, workItem) ?? SmithSound();
        }

        private static SoundDef SoundFromBenchRecipes(Thing bench, Thing workItem)
        {
            List<RecipeDef> recipes = bench?.def?.AllRecipes;
            if (recipes == null || recipes.Count == 0)
                return null;

            SoundDef matchingRecipeSound = null;
            SoundDef firstVanillaSound = null;
            SoundDef firstR4Sound = null;

            for (int i = 0; i < recipes.Count; i++)
            {
                RecipeDef recipe = recipes[i];
                SoundDef sound = recipe?.soundWorking;
                if (sound == null)
                    continue;

                if (workItem != null && recipe.ProducedThingDef == workItem.def)
                    matchingRecipeSound = sound;
                else if (recipe.defName != null && recipe.defName.StartsWith("RRRR_"))
                    firstR4Sound = firstR4Sound ?? sound;
                else
                    firstVanillaSound = firstVanillaSound ?? sound;
            }

            return matchingRecipeSound ?? firstVanillaSound ?? firstR4Sound;
        }

        private static SoundDef SoundFromBenchAndItem(Job job, Thing bench, Thing workItem)
        {
            ThingDef benchDef = bench?.def;
            if (benchDef != null)
            {
                string defName = benchDef.defName ?? string.Empty;
                string label = benchDef.label ?? string.Empty;

                if (TailorBenchDefNames.Contains(defName) ||
                    defName.ToLowerInvariant().Contains("tailor") ||
                    label.ToLowerInvariant().Contains("tailor"))
                    return TailorSound();
            }

            if (workItem?.def?.IsApparel == true)
                return TailorSound();

            return SmithSound();
        }

        private static SoundDef SmithSound()
        {
            if (_smithSound == null)
                _smithSound = DefDatabase<SoundDef>.GetNamedSilentFail(SmithSoundDefName);
            return _smithSound;
        }

        private static SoundDef TailorSound()
        {
            if (_tailorSound == null)
                _tailorSound = DefDatabase<SoundDef>.GetNamedSilentFail(TailorSoundDefName);
            return _tailorSound;
        }
    }
}
