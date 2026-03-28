using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RRRR
{
    /// <summary>
    /// RecipeWorker for bill-based recycling. Works with vanilla's JobDriver_DoBill.
    /// The item to recycle is the "ingredient". We defer destruction to
    /// Notify_IterationCompleted where we have the worker pawn's skill level.
    /// </summary>
    public class RecipeWorker_R4Recycle : RecipeWorker
    {
        public override void ConsumeIngredient(Thing ingredient, RecipeDef recipe, Map map)
        {
            // Don't destroy weapons/apparel yet — we need them alive in
            // Notify_IterationCompleted for skill-based product calculation.
            if (ingredient.def.IsWeapon || ingredient.def.IsApparel)
                return;

            base.ConsumeIngredient(ingredient, recipe, map);
        }

        public override void Notify_IterationCompleted(Pawn billDoer, List<Thing> ingredients)
        {
            if (billDoer == null || billDoer.Map == null)
                return;

            for (int i = 0; i < ingredients.Count; i++)
            {
                Thing item = ingredients[i];
                if (item == null || item.Destroyed)
                    continue;
                if (!item.def.IsWeapon && !item.def.IsApparel)
                    continue;

                Map map = billDoer.Map;
                MaterialUtility.DoRecycleProducts(item, billDoer, item.Position, map);

                // Remove any R4_Recycle designation
                if (item.Map != null)
                    item.Map.designationManager.RemoveAllDesignationsOn(item);

                if (!item.Destroyed)
                    item.Destroy();
            }
        }
    }
}
