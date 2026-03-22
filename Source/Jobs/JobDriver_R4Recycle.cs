using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RRRR
{
    /// <summary>
    /// Recycle job: haul item to workbench → work → spawn materials → destroy item.
    /// TargetA = item to recycle, TargetB = workbench.
    /// 
    /// Toil sequence:
    /// 1. Go to item, pick it up
    /// 2. Carry to bench interaction cell
    /// 3. Drop at feet (near bench)
    /// 4. Work toil with progress bar
    /// 5. Spawn materials, destroy item, clear designation
    /// </summary>
    public class JobDriver_R4Recycle : JobDriver
    {
        private const TargetIndex ItemInd = TargetIndex.A;
        private const TargetIndex BenchInd = TargetIndex.B;

        private float workLeft;
        private float totalWork;

        private Thing Item => job.GetTarget(ItemInd).Thing;
        private Thing Bench => job.GetTarget(BenchInd).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (!pawn.Reserve(Item, job, 1, -1, null, errorOnFailed))
                return false;
            if (!pawn.Reserve(Bench, job, 1, -1, null, errorOnFailed))
                return false;
            return true;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref workLeft, "workLeft", 0f);
            Scribe_Values.Look(ref totalWork, "totalWork", 0f);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // Fail conditions: abort if item or bench is destroyed/forbidden/designation removed
            this.FailOnDestroyedNullOrForbidden(ItemInd);
            this.FailOnDestroyedNullOrForbidden(BenchInd);
            this.FailOnThingMissingDesignation(ItemInd, R4DefOf.R4_Recycle);

            // 1. Go to the item
            yield return Toils_Goto.GotoThing(ItemInd, PathEndMode.ClosestTouch)
                .FailOnSomeonePhysicallyInteracting(ItemInd);

            // 2. Pick up the item
            yield return Toils_Haul.StartCarryThing(ItemInd);

            // 3. Carry to bench interaction cell
            yield return Toils_Goto.GotoThing(BenchInd, PathEndMode.InteractionCell);

            // 4. Drop the item at the pawn's current position (near the bench)
            Toil dropToil = ToilMaker.MakeToil("R4_Recycle_Drop");
            dropToil.defaultCompleteMode = ToilCompleteMode.Instant;
            dropToil.initAction = delegate
            {
                if (pawn.carryTracker.CarriedThing != null)
                {
                    pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
                }
            };
            yield return dropToil;

            // 5. Work toil — face bench, do work over time
            Toil workToil = ToilMaker.MakeToil("R4_Recycle_Work");
            workToil.defaultCompleteMode = ToilCompleteMode.Never;
            workToil.handlingFacing = true;
            workToil.activeSkill = () => SkillDefOf.Crafting;
            workToil.FailOnCannotTouch(BenchInd, PathEndMode.InteractionCell);

            workToil.initAction = delegate
            {
                totalWork = MaterialUtility.GetRecycleWorkAmount(Item);
                if (totalWork <= 0f)
                    totalWork = 500f;

                // On fresh start, set workLeft. On save-load, it's already restored.
                if (workLeft <= 0f)
                    workLeft = totalWork;
            };

            workToil.tickAction = delegate
            {
                pawn.rotationTracker.FaceTarget(Bench);

                // GeneralLaborSpeed is the standard stat for non-recipe manual work.
                // Typical values: ~1.0 for healthy pawns, higher with traits/bionics.
                // Bench factor from WorkTableWorkSpeedFactor (typically 1.0 for crafting benches).
                float pawnSpeed = pawn.GetStatValue(StatDefOf.GeneralLaborSpeed, true);
                float benchFactor = Bench.GetStatValue(StatDefOf.WorkTableWorkSpeedFactor, true);
                float workDone = pawnSpeed * benchFactor;

                workLeft -= workDone;
                pawn.skills?.Learn(SkillDefOf.Crafting, 0.1f);

                if (workLeft <= 0f)
                {
                    ReadyForNextToil();
                }
            };

            workToil.WithProgressBar(ItemInd, delegate
            {
                if (totalWork <= 0f) return 0f;
                return 1f - (workLeft / totalWork);
            });

            yield return workToil;

            // 6. Completion: spawn products, destroy item, remove designation
            Toil finishToil = ToilMaker.MakeToil("R4_Recycle_Finish");
            finishToil.defaultCompleteMode = ToilCompleteMode.Instant;
            finishToil.initAction = delegate
            {
                Thing item = Item;
                if (item == null || item.Destroyed)
                    return;

                // Spawn materials at the pawn's position
                var products = MaterialUtility.DoRecycleProducts(item, pawn, pawn.Position, pawn.Map);

                // Debug log (remove in M5)
                if (products.Count > 0)
                {
                    var sb = new System.Text.StringBuilder();
                    sb.Append($"[R4] Recycled {item.LabelCap}: ");
                    for (int i = 0; i < products.Count; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append($"{products[i].stackCount}x {products[i].LabelCapNoCount}");
                    }
                    Log.Message(sb.ToString());
                }

                // Remove designation before destroying
                pawn.Map.designationManager.RemoveAllDesignationsOn(item);

                // Destroy the original item
                item.Destroy(DestroyMode.Vanish);
            };

            yield return finishToil;
        }
    }
}
