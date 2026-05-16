using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RRRR
{
    /// <summary>
    /// RecipeWorker for bill-based repair. The damaged item is the "ingredient",
    /// but actual repair logic is handled by R4BillJobFactory + JobDriver_R4Repair.
    /// This worker only prevents vanilla ingredient destruction if the item ever
    /// flows through recipe code paths.
    /// </summary>
    public class RecipeWorker_R4Repair : RecipeWorker
    {
        public override void ConsumeIngredient(Thing ingredient, RecipeDef recipe, Map map)
        {
            if (ingredient.def.IsWeapon || ingredient.def.IsApparel)
                return;

            base.ConsumeIngredient(ingredient, recipe, map);
        }

        public override void Notify_IterationCompleted(Pawn billDoer, List<Thing> ingredients)
        {
            // Repair bills run through JobDriver_R4Repair, not vanilla DoBill.
            // Leave this as a no-op safety hook to avoid duplicating repair logic
            // if recipe code paths are ever reached unexpectedly.
        }
    }
}
