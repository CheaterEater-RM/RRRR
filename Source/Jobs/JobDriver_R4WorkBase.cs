using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RRRR
{
    /// <summary>
    /// Abstract base class for R4 workbench job drivers (Repair, Clean).
    /// Implements the full toil sequence with vanilla-aligned fail conditions,
    /// tick timing (tickAction + tickIntervalAction), ingredient tracking via
    /// job.placedThings, and bench usage (UsedThisTick). Subclasses override
    /// only the work-specific hooks.
    ///
    /// Target slots (matches vanilla JobDriver_DoBill):
    ///   TargetA        = bench (stable, never overwritten)
    ///   TargetQueueA[0]= work item (weapon/apparel being repaired/cleaned)
    ///   TargetQueueB   = ingredient stacks
    ///   TargetC        = ingredient placement cell
    /// </summary>
    public abstract class JobDriver_R4WorkBase : JobDriver
    {
        protected const TargetIndex BenchInd      = TargetIndex.A;
        protected const TargetIndex IngredientInd = TargetIndex.B;
        protected const TargetIndex CellInd       = TargetIndex.C;

        protected float cycleWorkLeft;
        protected float cycleWorkTotal;

        protected Thing Bench        => job.GetTarget(BenchInd).Thing;
        protected bool  IsBillDriven => job.bill != null;

        private Thing _cachedWorkItem;
        protected Thing WorkItem
        {
            get
            {
                if (_cachedWorkItem == null || _cachedWorkItem.Destroyed)
                {
                    var queue = job.GetTargetQueue(BenchInd);
                    if (queue != null && queue.Count > 0)
                        _cachedWorkItem = queue[0].Thing;
                }
                return _cachedWorkItem;
            }
        }

        // ── Abstract hooks for subclasses ──────────────────────────────────
        protected abstract DesignationDef WorkDesignationDef { get; }
        protected abstract float CalculateTotalWork(Thing item);
        protected abstract void ApplyWorkResult(Thing item, Pawn worker);
        protected abstract List<ThingDefCountClass> GetCycleCost(Thing item);
        protected abstract bool ShouldContinueWorking(Thing item);
        protected abstract bool IsWorkItemStillValid(Thing item);
        protected abstract float GetSkillXpPerTick();
        protected abstract float GetSkillSpeedBonus(int skillLevel);
        protected abstract string GetJobReportKey();

        // ── Virtual hooks with defaults ────────────────────────────────────
        protected virtual void OnItemDestroyed(Thing item) { }

        // ── Overrides ──────────────────────────────────────────────────────

        public override string GetReport()
        {
            Thing item  = WorkItem;
            Thing bench = Bench;
            string itemLabel  = item  != null ? item.LabelShort  : "unknown".Translate().ToString();
            string benchLabel = bench != null ? bench.LabelShort : "unknown".Translate().ToString();
            return GetJobReportKey().Translate(itemLabel, benchLabel);
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // Reserve the bench
            if (!pawn.Reserve(job.GetTarget(BenchInd), job, 1, -1, null, errorOnFailed))
                return false;

            // Reserve interaction cell (matches vanilla JobDriver_DoBill)
            Thing bench = Bench;
            if (bench != null && bench.def.hasInteractionCell)
            {
                if (!pawn.ReserveSittableOrSpot(bench.InteractionCell, job, errorOnFailed))
                    return false;
            }

            // Reserve the work item
            var itemQueue = job.GetTargetQueue(BenchInd);
            if (itemQueue != null && itemQueue.Count > 0)
            {
                Thing item = itemQueue[0].Thing;
                if (item != null && !pawn.Reserve(item, job, 1, -1, null, errorOnFailed))
                    return false;
            }

            // Reserve as many ingredients as possible
            pawn.ReserveAsManyAsPossible(job.GetTargetQueue(IngredientInd), job);
            return true;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref cycleWorkLeft,  "cycleWorkLeft",  0f);
            Scribe_Values.Look(ref cycleWorkTotal, "cycleWorkTotal", 0f);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // ── Global fail conditions (matches vanilla JobDriver_DoBill) ──

            // Bench must remain spawned
            AddEndCondition(delegate
            {
                Thing thing = GetActor().jobs.curJob.GetTarget(BenchInd).Thing;
                return (!(thing is Building) || thing.Spawned)
                    ? JobCondition.Ongoing
                    : JobCondition.Incompletable;
            });

            // Bench on fire
            this.FailOnBurningImmobile(BenchInd);

            // Bench usability + work item validity + bill/designation checks
            this.FailOn(delegate
            {
                Thing item = WorkItem;
                string failReason = GetGlobalFailReason(item);
                if (failReason != null)
                {
                    R4Log.Warn($"Global fail {job.def.defName}: {failReason}. item={DescribeItemState(item)} tracked={MaterialUtility.DescribePlacedThings(job)}");
                    return true;
                }

                return false;
            });

            Toil startToil = ToilMaker.MakeToil("R4_Work_Start");
            startToil.initAction = delegate
            {
                if (IsBillDriven)
                    job.bill.Notify_DoBillStarted(pawn);
            };
            yield return startToil;

            Toil gotoBillGiver = Toils_Goto.GotoThing(BenchInd, PathEndMode.InteractionCell);
            bool workItemFirst = true;

            IEnumerable<Toil> YieldIngredientToils(bool skipToWorkItemPhase)
            {
                if (!skipToWorkItemPhase && job.GetTargetQueue(IngredientInd).NullOrEmpty())
                    yield break;

                if (skipToWorkItemPhase)
                {
                    yield return Toils_Jump.JumpIf(gotoBillGiver,
                        () => job.GetTargetQueue(IngredientInd).NullOrEmpty());
                }

                foreach (Toil toil in R4CollectIngredientsToils.CollectIngredientsToils(
                    IngredientInd, BenchInd, CellInd,
                    subtractNumTakenFromJobCount: false,
                    failIfStackCountLessThanJobCount: false))
                {
                    yield return toil;
                }
            }

            IEnumerable<Toil> YieldWorkItemToils()
            {
                yield return gotoBillGiver;

                Toil gotoItem = ToilMaker.MakeToil("R4_Work_GotoItem");
                gotoItem.defaultCompleteMode = ToilCompleteMode.PatherArrival;
                gotoItem.initAction = delegate
                {
                    Thing item = WorkItem;
                    if (item == null || item.Destroyed)
                    {
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }
                    pawn.pather.StartPath(item, PathEndMode.ClosestTouch);
                };
                gotoItem.AddFailCondition(() =>
                {
                    Thing item = WorkItem;
                    return item == null || item.Destroyed || item.IsForbidden(pawn);
                });
                yield return gotoItem;

                Toil carryItem = ToilMaker.MakeToil("R4_Work_CarryItem");
                carryItem.defaultCompleteMode = ToilCompleteMode.Instant;
                carryItem.initAction = delegate
                {
                    Thing item = WorkItem;
                    if (item == null || item.Destroyed)
                    {
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }
                    if (pawn.carryTracker.CarriedThing == null)
                    {
                        int count = Mathf.Min(item.stackCount, pawn.carryTracker.AvailableStackSpace(item.def));
                        if (count <= 0 || pawn.carryTracker.TryStartCarry(item, count) <= 0)
                            EndJobWith(JobCondition.Incompletable);
                    }
                };
                yield return carryItem;

                yield return Toils_Goto.GotoThing(BenchInd, PathEndMode.InteractionCell);

                Toil dropItem = ToilMaker.MakeToil("R4_Work_DropItem");
                dropItem.defaultCompleteMode = ToilCompleteMode.Instant;
                dropItem.initAction = delegate
                {
                    if (pawn.carryTracker.CarriedThing != null)
                    {
                        if (!R4WorkbenchPlacement.TryPlaceCarriedWorkItemAtBench(pawn, Bench, job, out Thing placedWorkItem, out string failReason))
                        {
                            R4Log.Warn(
                                $"Work item placement failed for {job.def.defName}: pawn={pawn.LabelShort} jobId={job.loadID} " +
                                $"reason={failReason ?? "unknown"} tracked={MaterialUtility.DescribePlacedThings(job)}");
                            EndJobWith(JobCondition.Incompletable);
                        }
                        else
                            R4Log.Debug(
                                $"Work item placed for {job.def.defName}: {DescribeItemState(WorkItem)} " +
                                $"placedAt={DescribeThing(placedWorkItem)} tracked={MaterialUtility.DescribePlacedThings(job)}");
                    }
                };
                yield return dropItem;
            }

            if (workItemFirst)
            {
                foreach (Toil toil in YieldWorkItemToils())
                    yield return toil;

                foreach (Toil toil in YieldIngredientToils(skipToWorkItemPhase: false))
                    yield return toil;
            }
            else
            {
                foreach (Toil toil in YieldIngredientToils(skipToWorkItemPhase: true))
                    yield return toil;

                foreach (Toil toil in YieldWorkItemToils())
                    yield return toil;
            }

            // ── Phase 3: Work one cycle ──

            Toil workToil = ToilMaker.MakeToil("R4_Work_DoWork");
            workToil.defaultCompleteMode = ToilCompleteMode.Never;
            workToil.handlingFacing      = true;
            workToil.activeSkill         = () => SkillDefOf.Crafting;
            workToil.FailOnCannotTouch(BenchInd, PathEndMode.InteractionCell);
            workToil.AddFailCondition(() =>
            {
                string failReason = GetPlacedThingFailReason(BenchInd);
                if (failReason != null)
                {
                    R4Log.Warn($"Placed-things fail {job.def.defName}: {failReason}. tracked={MaterialUtility.DescribePlacedThings(job)}");
                    return true;
                }

                return false;
            });

            workToil.initAction = delegate
            {
                Thing item = WorkItem;
                if (item == null || item.Destroyed)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                if (!IsWorkItemStillValid(item))
                {
                    RemoveDesignation(item);
                    EndJobWith(JobCondition.Succeeded);
                    return;
                }
                cycleWorkTotal = CalculateTotalWork(item);
                if (cycleWorkLeft <= 0f)
                    cycleWorkLeft = cycleWorkTotal;

                R4Log.Debug(
                    $"Work start {job.def.defName}: bench={Bench?.LabelShort ?? "null"} " +
                    $"item={DescribeItemState(item)} queued={DescribeQueuedIngredients()} " +
                    $"cycleCost={MaterialUtility.DescribeCosts(GetCycleCost(item))} " +
                    $"tracked={MaterialUtility.DescribePlacedThings(job)}");
            };

            // Fires every tick — bench usage, facing, fuel consumption
            workToil.tickAction = delegate
            {
                Thing item = WorkItem;
                if (item == null || item.Destroyed)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                pawn.rotationTracker.FaceTarget(Bench);
                if (Bench is IBillGiverWithTickAction tickBench)
                    tickBench.UsedThisTick();
            };

            // Fires at game-speed-adjusted intervals — work progress, XP, comfort
            workToil.tickIntervalAction = delegate(int delta)
            {
                Thing item = WorkItem;
                if (item == null || item.Destroyed)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                AdvanceWork(item, delta);
            };

            void AdvanceWork(Thing item, int delta)
            {
                if (item == null || item.Destroyed)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                if (IsBillDriven)
                    job.bill.Notify_PawnDidWork(pawn);

                float speed       = pawn.GetStatValue(StatDefOf.GeneralLaborSpeed, true);
                float benchFactor = Bench.GetStatValue(StatDefOf.WorkTableWorkSpeedFactor, true);
                int   skillLevel  = pawn.skills?.GetSkill(SkillDefOf.Crafting)?.Level ?? 0;
                float skillBonus  = GetSkillSpeedBonus(skillLevel);

                float combinedSpeed = speed * benchFactor * skillBonus;
                if (DebugSettings.fastCrafting)
                    combinedSpeed *= 30f;

                cycleWorkLeft -= combinedSpeed * delta;

                pawn.skills?.Learn(SkillDefOf.Crafting, GetSkillXpPerTick() * delta);
                pawn.GainComfortFromCellIfPossible(delta, chairsOnly: true);

                // Vanilla checks for job overrides every 1000 ticks on long bills
                if (IsBillDriven && cycleWorkTotal > 3000f && pawn.IsHashIntervalTick(1000))
                {
                    pawn.jobs.CheckForJobOverride();
                }

                if (cycleWorkLeft <= 0f)
                {
                    R4Log.Debug(
                        $"Work cycle complete {job.def.defName}: item={DescribeItemState(item)} tracked={MaterialUtility.DescribePlacedThings(job)}");
                    ReadyForNextToil();
                }
            }

            workToil.WithProgressBar(BenchInd,
                () => cycleWorkTotal <= 0f ? 0f : 1f - (cycleWorkLeft / cycleWorkTotal));

            workToil.PlaySustainerOrSound(() => pawn.jobs.curJob?.bill?.recipe?.soundWorking);

            yield return workToil;

            // ── Phase 4: Apply result, consume ingredients ──

            Toil finishToil = ToilMaker.MakeToil("R4_Work_Finish");
            finishToil.defaultCompleteMode = ToilCompleteMode.Instant;
            finishToil.initAction = delegate
            {
                Thing item = WorkItem;
                if (item == null || item.Destroyed)
                {
                    R4Log.Warn($"Finish toil aborted for {job.def.defName}: {DescribeJobContext(item)} work item was null or destroyed.");
                    return;
                }

                List<ThingDefCountClass> cycleCost = GetCycleCost(item);
                int hpBefore = item.def.useHitPoints ? item.HitPoints : -1;

                R4Log.Debug(
                    $"Finish toil {job.def.defName}: {DescribeJobContext(item)} " +
                    $"cycleCost={MaterialUtility.DescribeCosts(cycleCost)} " +
                    $"tracked={MaterialUtility.DescribePlacedThings(job)}");

                List<Thing> extractedIngredients;
                if (cycleCost.Count == 0)
                {
                    extractedIngredients = new List<Thing>();
                    if (job.placedThings != null && job.placedThings.Count > 0)
                    {
                        R4Log.Debug(
                            $"Finish ingredients {job.def.defName}: {DescribeJobContext(item)} clearing tracked refs for zero-cost cycle. " +
                            $"tracked={MaterialUtility.DescribePlacedThings(job)}");
                    }

                    job.placedThings = null;
                }
                else
                {
                    extractedIngredients = MaterialUtility.ExtractPlacedIngredients(job);
                }

                R4Log.Debug(
                    $"Finish ingredients {job.def.defName}: {DescribeJobContext(item)} " +
                    $"extracted={MaterialUtility.DescribeThingList(extractedIngredients)}");
                MaterialUtility.LogPlacedIngredientMismatch(job, extractedIngredients, cycleCost);

                // Bill notification — before destroying ingredients, matching vanilla
                // (Notify_IterationCompleted receives live ingredient refs)
                if (IsBillDriven)
                {
                    var products = new List<Thing> { item };
                    job.bill.Notify_IterationCompleted(pawn, extractedIngredients);
                    RecordsUtility.Notify_BillDone(pawn, products);
                }

                MaterialUtility.DestroyExtractedIngredients(extractedIngredients);

                // Apply the subclass-specific work result
                ApplyWorkResult(item, pawn);

                R4Log.Debug(
                    $"Finish result {job.def.defName}: {DescribeJobContext(item)} beforeHP={(hpBefore >= 0 ? hpBefore.ToString() : "n/a")} " +
                    $"after={DescribeItemState(item)} destroyed={item.Destroyed}");

                // Check if item was destroyed during ApplyWorkResult (repair failure)
                if (item.Destroyed || (item.def.useHitPoints && item.HitPoints <= 0))
                {
                    OnItemDestroyed(item);
                    return;
                }

                // Remove designation if work is complete
                if (!ShouldContinueWorking(item))
                    RemoveDesignation(item);
            };

            yield return finishToil;

            // ── Phase 5: Store repaired/cleaned item per bill store mode ──

            Toil storeToil = ToilMaker.MakeToil("R4_Work_Store");
            storeToil.defaultCompleteMode = ToilCompleteMode.Instant;
            storeToil.initAction = delegate
            {
                if (!IsBillDriven)
                {
                    EndJobWith(JobCondition.Succeeded);
                    return;
                }

                Thing item = WorkItem;
                if (item == null || item.Destroyed || !item.Spawned || item.Map != pawn.Map)
                {
                    EndJobWith(JobCondition.Succeeded);
                    return;
                }

                if (job.bill.GetStoreMode() == BillStoreModeDefOf.DropOnFloor)
                {
                    DropItemNearPawn(item);
                    EndJobWith(JobCondition.Succeeded);
                    return;
                }

                IntVec3 foundCell = IntVec3.Invalid;

                if (job.bill.GetStoreMode() == BillStoreModeDefOf.BestStockpile)
                {
                    StoreUtility.TryFindBestBetterStoreCellFor(
                        item, pawn, pawn.Map, StoragePriority.Unstored,
                        pawn.Faction, out foundCell);
                }
                else if (job.bill.GetStoreMode() == BillStoreModeDefOf.SpecificStockpile)
                {
                    StoreUtility.TryFindBestBetterStoreCellForIn(
                        item, pawn, pawn.Map, StoragePriority.Unstored,
                        pawn.Faction, job.bill.GetSlotGroup(), out foundCell);
                }

                if (foundCell.IsValid)
                {
                    if (pawn.carryTracker.TryStartCarry(item, item.stackCount) > 0)
                    {
                        Job haulJob = HaulAIUtility.HaulToCellStorageJob(pawn, item, foundCell, fitInStoreCell: false);
                        if (haulJob != null)
                        {
                            pawn.jobs.StartJob(haulJob, JobCondition.Succeeded,
                                keepCarryingThingOverride: true);
                        }
                        else
                        {
                            pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
                            EndJobWith(JobCondition.Incompletable);
                        }
                    }
                    else
                    {
                        EndJobWith(JobCondition.Succeeded);
                    }
                }
                else
                {
                    // No valid stockpile cell — leave item where it is (matches vanilla)
                    EndJobWith(JobCondition.Succeeded);
                }
            };

            yield return storeToil;

            void DropItemNearPawn(Thing item)
            {
                if (item == null || item.Destroyed || !item.Spawned)
                    return;

                if (pawn.carryTracker.TryStartCarry(item, item.stackCount) > 0)
                {
                    pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
                }
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────

        protected void RemoveDesignation(Thing item)
        {
            if (item == null || item.Map == null)
                return;

            DesignationManager designationManager = item.Map.designationManager;
            var des = designationManager.DesignationOn(item, WorkDesignationDef);
            if (des != null)
                designationManager.RemoveDesignation(des);
        }

        private string DescribeQueuedIngredients()
        {
            List<LocalTargetInfo> queue = job.GetTargetQueue(IngredientInd);
            if (queue.NullOrEmpty())
                return "none";

            var parts = new List<string>(queue.Count);
            for (int i = 0; i < queue.Count; i++)
            {
                Thing thing = queue[i].Thing;
                int count = (job.countQueue != null && i < job.countQueue.Count)
                    ? job.countQueue[i]
                    : thing?.stackCount ?? 0;
                parts.Add($"{thing?.def?.defName ?? "null"}x{count}");
            }

            return string.Join(", ", parts);
        }

        private static string DescribeItemState(Thing item)
        {
            if (item == null)
                return "null";

            return item.def.useHitPoints
                ? $"{item.LabelShort} hp={item.HitPoints}/{item.MaxHitPoints}"
                : item.LabelShort;
        }

        private string DescribeJobContext(Thing item)
        {
            string benchLabel = Bench?.LabelShort ?? "null";
            string benchPos = Bench == null
                ? "null"
                : Bench.Spawned ? Bench.PositionHeld.ToString() : "unspawned";
            string itemPos = item == null
                ? "null"
                : item.Spawned ? item.PositionHeld.ToString() : "unspawned";

            return $"pawn={pawn.LabelShort} jobId={job.loadID} bench={benchLabel} benchPos={benchPos} item={DescribeItemState(item)} itemPos={itemPos}";
        }

        private string GetGlobalFailReason(Thing item)
        {
            Thing benchThing = job.GetTarget(BenchInd).Thing;
            if (benchThing is IBillGiver billGiver && !billGiver.CurrentlyUsableForBills())
                return "bench not currently usable for bills";

            if (item == null)
                return "work item is null";

            if (item.Destroyed)
                return "work item destroyed";

            if (!IsWorkItemStillValid(item))
                return "work item no longer valid for this job";

            if (IsBillDriven)
            {
                if (job.bill.DeletedOrDereferenced)
                    return "bill deleted or dereferenced";
                if (job.bill.suspended)
                    return "bill suspended";
                return null;
            }

            if (item.Map != null && item.Map.designationManager.DesignationOn(item, WorkDesignationDef) == null)
                return $"missing {WorkDesignationDef.defName} designation";

            return null;
        }

        private string GetPlacedThingFailReason(TargetIndex containerIndex)
        {
            if (job.placedThings == null)
                return null;

            for (int i = 0; i < job.placedThings.Count; i++)
            {
                ThingCountClass thingCountClass = job.placedThings[i];
                Thing thing = thingCountClass?.thing;
                ThingOwner thingOwner = job.GetTarget(containerIndex).Thing?.TryGetInnerInteractableThingOwner();

                if (thing == null)
                    return $"tracked thing #{i} is null";

                if (!thing.Spawned && (thingOwner == null || !thingOwner.Contains(thing)))
                    return $"tracked thing {MaterialUtility.DescribeThingReference(thing)} is not spawned and not inside bill giver";

                if (thing.MapHeld != pawn.Map)
                    return $"tracked thing {MaterialUtility.DescribeThingReference(thing)} moved to another map or holder";

                if (!job.ignoreForbidden && thing.IsForbidden(pawn))
                    return $"tracked thing {MaterialUtility.DescribeThingReference(thing)} became forbidden";
            }

            return null;
        }

        private static string DescribeThing(Thing thing)
        {
            if (thing == null)
                return "null";

            string location = thing.Spawned ? thing.PositionHeld.ToString() : "unspawned";
            return $"{thing.def.defName} stack={thing.stackCount} at={location}";
        }
    }
}
