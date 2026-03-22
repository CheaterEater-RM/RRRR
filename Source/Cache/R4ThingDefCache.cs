using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RRRR
{
    /// <summary>
    /// Startup cache of workbench lists built by scanning DefDatabase.
    /// Triggered explicitly from Setup.cs via RuntimeHelpers.RunClassConstructor.
    /// </summary>
    public static class R4ThingDefCache
    {
        /// <summary>Benches that have SmeltWeapon or SmeltApparel recipes.</summary>
        public static List<ThingDef> SmeltBenches { get; private set; } = new List<ThingDef>();

        /// <summary>Benches that have apparel crafting recipes (tailor benches).</summary>
        public static List<ThingDef> ApparelBenches { get; private set; } = new List<ThingDef>();

        /// <summary>Union of all benches that can do any R4 work.</summary>
        public static List<ThingDef> AllR4Benches { get; private set; } = new List<ThingDef>();

        // Known recipe defNames for routing
        private static readonly HashSet<string> SmeltRecipes = new HashSet<string>
        {
            "SmeltWeapon", "SmeltApparel"
        };

        private static readonly HashSet<string> ApparelCraftRecipes = new HashSet<string>
        {
            "Make_Apparel_BasicShirt", "Make_Apparel_TribalA"
        };

        static R4ThingDefCache()
        {
            BuildCache();
        }

        public static void BuildCache()
        {
            SmeltBenches.Clear();
            ApparelBenches.Clear();
            AllR4Benches.Clear();

            foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def.AllRecipes == null || def.AllRecipes.Count == 0)
                    continue;

                bool isSmelter = false;
                bool isApparelBench = false;

                for (int i = 0; i < def.AllRecipes.Count; i++)
                {
                    var recipe = def.AllRecipes[i];
                    if (recipe?.defName == null)
                        continue;

                    if (SmeltRecipes.Contains(recipe.defName))
                        isSmelter = true;

                    if (ApparelCraftRecipes.Contains(recipe.defName))
                        isApparelBench = true;
                }

                if (isSmelter)
                    SmeltBenches.Add(def);

                if (isApparelBench)
                    ApparelBenches.Add(def);
            }

            // Build union (reuse existing list to keep external references valid)
            AllR4Benches.Clear();
            var allSet = new HashSet<ThingDef>(SmeltBenches);
            allSet.UnionWith(ApparelBenches);
            AllR4Benches.AddRange(allSet);

            Log.Message($"[R4] Cache built: {SmeltBenches.Count} smelt benches, {ApparelBenches.Count} apparel benches, {AllR4Benches.Count} total.");
        }
    }
}
