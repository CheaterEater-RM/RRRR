using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RRRR
{
    /// <summary>
    /// RecipeWorker for bill-based taint cleaning. Works with vanilla's JobDriver_DoBill.
    /// The tainted apparel is the "ingredient". Instead of being destroyed, we
    /// remove the taint and leave the item on the bench for hauling.
    /// </summary>
    public class RecipeWorker_R4Clean : RecipeWorker
    {
        public override void ConsumeIngredient(Thing ingredient, RecipeDef recipe, Map map)
        {
            // Don't destroy apparel — we modify it in place
            if (ingredient is Apparel)
                return;

            base.ConsumeIngredient(ingredient, recipe, map);
        }

        public override void Notify_IterationCompleted(Pawn billDoer, List<Thing> ingredients)
        {
            for (int i = 0; i < ingredients.Count; i++)
            {
                Thing item = ingredients[i];
                if (item == null || item.Destroyed)
                    continue;

                if (item is Apparel apparel && apparel.WornByCorpse)
                {
                    apparel.WornByCorpse = false;
                    apparel.Notify_ColorChanged();

                    if (apparel.Map != null)
                    {
                        var des = apparel.Map.designationManager.DesignationOn(apparel, R4DefOf.R4_Clean);
                        if (des != null)
                            apparel.Map.designationManager.RemoveDesignation(des);
                    }
                }
            }
        }
    }
}
