using RimWorld;
using Verse;
using Verse.AI;

namespace RRRR
{
    /// <summary>
    /// Designation-based recycle job: haul item to workbench → work → spawn materials → destroy item.
    ///   TargetA = item to recycle
    ///   TargetB = workbench
    ///
    /// Bill-based recycling uses vanilla JobDriver_DoBill → RecipeWorker_R4Recycle instead.
    /// This driver is only created by WorkGiver_R4Recycle (designation flow).
    /// </summary>
    public class JobDriver_R4Recycle : JobDriver
    {
        private const TargetIndex ItemInd  = TargetIndex.A;
        private const TargetIndex BenchInd = TargetIndex.B;

        private float workLeft;
        private float totalWork;

        private Thing Item  => job.GetTarget(ItemInd).Thing;
        private Thing Bench => job.GetTarget(BenchInd).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (!pawn.Reserve(Item,  job, 1, -1, null, errorOnFailed)) return false;
            if (!pawn.Reserve(Bench, job, 1, -1, null, errorOnFailed)) return false;
            return true;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref workLeft,  "workLeft",  0f);
            Scribe_Values.Look(ref totalWork, "totalWork", 0f);
        }

        protected override System.Collections.Generic.IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedNullOrForbidden(ItemInd);
            this.FailOnDestroyedNullOrForbidden(BenchInd);
            this.FailOnThingMissingDesignation(ItemInd, R4DefOf.R4_Recycle);

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
                if (workLeft  <= 0f) workLeft  = totalWork;
            };

            workToil.tickAction = delegate
            {
                pawn.rotationTracker.FaceTarget(Bench);
                float pawnSpeed   = pawn.GetStatValue(StatDefOf.GeneralLaborSpeed,      true);
                float benchFactor = Bench.GetStatValue(StatDefOf.WorkTableWorkSpeedFactor, true);
                workLeft -= pawnSpeed * benchFactor;
                pawn.skills?.Learn(SkillDefOf.Crafting, 0.1f);
                if (workLeft <= 0f)
                    ReadyForNextToil();
            };

            workToil.WithProgressBar(ItemInd, () => totalWork <= 0f ? 0f : 1f - (workLeft / totalWork));

            yield return workToil;

            Toil finishToil = ToilMaker.MakeToil("R4_Recycle_Finish");
            finishToil.defaultCompleteMode = ToilCompleteMode.Instant;
            finishToil.initAction = delegate
            {
                Thing item = Item;
                if (item == null || item.Destroyed)
                    return;

                MaterialUtility.DoRecycleProducts(item, pawn, pawn.Position, pawn.Map);
                pawn.Map.designationManager.RemoveAllDesignationsOn(item);
                item.Destroy(DestroyMode.Vanish);
            };

            yield return finishToil;
        }
    }
}
