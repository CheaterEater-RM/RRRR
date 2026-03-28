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
    /// Works for both designation-based and bill-based recycling.
    /// When job.bill is set, notifies the bill on completion and skips
    /// designation checks.
    /// </summary>
    public class JobDriver_R4Recycle : JobDriver
    {
        private const TargetIndex ItemInd = TargetIndex.A;
        private const TargetIndex BenchInd = TargetIndex.B;

        private float workLeft;
        private float totalWork;

        private Thing Item => job.GetTarget(ItemInd).Thing;
        private Thing Bench => job.GetTarget(BenchInd).Thing;

        private bool IsBillDriven => job.bill != null;

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
            this.FailOnDestroyedNullOrForbidden(ItemInd);
            this.FailOnDestroyedNullOrForbidden(BenchInd);

            // Only check designation for non-bill jobs
            if (!IsBillDriven)
                this.FailOnThingMissingDesignation(ItemInd, R4DefOf.R4_Recycle);

            // For bill jobs, fail if bill is deleted or suspended
            if (IsBillDriven)
            {
                this.FailOn(delegate
                {
                    return job.bill.DeletedOrDereferenced || job.bill.suspended;
                });
            }

            yield return Toils_Goto.GotoThing(ItemInd, PathEndMode.ClosestTouch)
                .FailOnSomeonePhysicallyInteracting(ItemInd);

            yield return Toils_Haul.StartCarryThing(ItemInd);

            yield return Toils_Goto.GotoThing(BenchInd, PathEndMode.InteractionCell);

            Toil dropToil = ToilMaker.MakeToil("R4_Recycle_Drop");
            dropToil.defaultCompleteMode = ToilCompleteMode.Instant;
            dropToil.initAction = delegate
            {
                if (pawn.carryTracker.CarriedThing != null)
                    pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
            };
            yield return dropToil;

            Toil workToil = ToilMaker.MakeToil("R4_Recycle_Work");
            workToil.defaultCompleteMode = ToilCompleteMode.Never;
            workToil.handlingFacing = true;
            workToil.activeSkill = () => SkillDefOf.Crafting;
            workToil.FailOnCannotTouch(BenchInd, PathEndMode.InteractionCell);

            workToil.initAction = delegate
            {
                totalWork = MaterialUtility.GetRecycleWorkAmount(Item);
                if (totalWork <= 0f) totalWork = 500f;
                if (workLeft <= 0f) workLeft = totalWork;
            };

            workToil.tickAction = delegate
            {
                pawn.rotationTracker.FaceTarget(Bench);
                float pawnSpeed = pawn.GetStatValue(StatDefOf.GeneralLaborSpeed, true);
                float benchFactor = Bench.GetStatValue(StatDefOf.WorkTableWorkSpeedFactor, true);
                workLeft -= pawnSpeed * benchFactor;
                pawn.skills?.Learn(SkillDefOf.Crafting, 0.1f);
                if (workLeft <= 0f)
                    ReadyForNextToil();
            };

            workToil.WithProgressBar(ItemInd, delegate
            {
                if (totalWork <= 0f) return 0f;
                return 1f - (workLeft / totalWork);
            });

            yield return workToil;

            Toil finishToil = ToilMaker.MakeToil("R4_Recycle_Finish");
            finishToil.defaultCompleteMode = ToilCompleteMode.Instant;
            finishToil.initAction = delegate
            {
                Thing item = Item;
                if (item == null || item.Destroyed)
                    return;

                var products = MaterialUtility.DoRecycleProducts(item, pawn, pawn.Position, pawn.Map);

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

                // Notify the bill if this was bill-driven
                if (IsBillDriven)
                {
                    var ingredients = new List<Thing> { item };
                    job.bill.Notify_IterationCompleted(pawn, ingredients);
                }

                pawn.Map.designationManager.RemoveAllDesignationsOn(item);
                item.Destroy(DestroyMode.Vanish);
            };

            yield return finishToil;
        }
    }
}
