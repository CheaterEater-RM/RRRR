using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace RRRR
{
    /// <summary>
    /// Harmony postfix on Toils_Haul.PlaceHauledThingInCell (a static factory method).
    ///
    /// Vanilla's PlaceHauledThingInCell has a hardcoded whitelist of job defs
    /// (DoBill, RecolorApparel, RefuelAtomic, RearmTurretAtomic) that trigger
    /// ingredient tracking via job.placedThings. R4's RRRR_Repair and RRRR_Clean
    /// are not in this list, so ingredients placed by CollectIngredientsToils
    /// are never tracked.
    ///
    /// This postfix wraps the returned Toil's initAction to snapshot carry state,
    /// run the original, and call UpdateJobWithPlacedThings for R4 jobs.
    /// See PATCH_ANALYSIS.md for full edge case analysis.
    /// </summary>
    [HarmonyPatch(typeof(Toils_Haul), nameof(Toils_Haul.PlaceHauledThingInCell),
        new[] { typeof(TargetIndex), typeof(Toil), typeof(bool), typeof(bool) })]
    public static class Patch_PlaceHauledThingInCell
    {
        static void Postfix(
            Toil __result,
            TargetIndex cellInd,
            Toil nextToilOnPlaceFailOrIncomplete,
            bool storageMode,
            bool tryStoreInSameStorageIfSpotCantHoldWholeStack)
        {
            Action originalAction = __result.initAction;

            __result.initAction = delegate
            {
                Pawn actor = __result.actor;
                Job curJob = actor.jobs.curJob;

                // Only wrap for R4 jobs — all other jobs use the original unmodified
                if (!IsR4WorkJob(curJob))
                {
                    originalAction?.Invoke();
                    return;
                }

                IntVec3 cell = curJob.GetTarget(cellInd).Cell;
                if (actor.carryTracker.CarriedThing == null)
                {
                    R4Log.Error($"PlaceHauledThingInCell: pawn={actor.LabelShort} jobId={curJob.loadID} tried to place hauled thing in cell but is not hauling anything.");
                    return;
                }

                SlotGroup slotGroup = actor.Map.haulDestinationManager.SlotGroupAt(cell);
                if (slotGroup != null && slotGroup.Settings.AllowedToAccept(actor.carryTracker.CarriedThing))
                {
                    actor.Map.designationManager.TryRemoveDesignationOn(actor.carryTracker.CarriedThing, DesignationDefOf.Haul);
                }

                Thing trackedThing = null;
                int trackedAdded = 0;
                Action<Thing, int> placedAction = delegate(Thing thing, int added)
                {
                    trackedThing = thing;
                    trackedAdded += added;
                    HaulAIUtility.UpdateJobWithPlacedThings(curJob, thing, added);
                };

                if (!actor.carryTracker.TryDropCarriedThing(cell, ThingPlaceMode.Direct, out Thing resultingThing, placedAction))
                {
                    if (storageMode)
                    {
                        IntVec3 storeCell;
                        IntVec3 foundCell;
                        if (nextToilOnPlaceFailOrIncomplete != null &&
                            (((tryStoreInSameStorageIfSpotCantHoldWholeStack && curJob.bill != null) &&
                              StoreUtility.TryFindBestBetterStoreCellForIn(
                                  actor.carryTracker.CarriedThing,
                                  actor,
                                  actor.Map,
                                  StoragePriority.Unstored,
                                  actor.Faction,
                                  curJob.bill.GetSlotGroup(),
                                  out foundCell)) ||
                             StoreUtility.TryFindBestBetterStoreCellFor(
                                 actor.carryTracker.CarriedThing,
                                 actor,
                                 actor.Map,
                                 StoragePriority.Unstored,
                                 actor.Faction,
                                 out foundCell)))
                        {
                            if (!actor.CanReserve(foundCell) || !actor.Reserve(foundCell, actor.CurJob))
                            {
                                AbortRedirectedHaul(actor, curJob,
                                    $"could not reserve redirected haul target {foundCell} for {DescribeThingReference(actor.carryTracker.CarriedThing)}");
                                return;
                            }

                            actor.CurJob.SetTarget(cellInd, foundCell);
                            actor.jobs.curDriver.JumpToToil(nextToilOnPlaceFailOrIncomplete);
                        }
                        else if (HaulAIUtility.CanHaulAside(actor, actor.carryTracker.CarriedThing, out storeCell))
                        {
                            if (nextToilOnPlaceFailOrIncomplete == null)
                            {
                                AbortRedirectedHaul(actor, curJob,
                                    $"HaulAIUtility.CanHaulAside found fallback cell {storeCell} but no follow-up toil was provided for {DescribeThingReference(actor.carryTracker.CarriedThing)}");
                                return;
                            }

                            curJob.SetTarget(cellInd, storeCell);
                            curJob.count = int.MaxValue;
                            curJob.haulOpportunisticDuplicates = false;
                            curJob.haulMode = HaulMode.ToCellNonStorage;
                            actor.jobs.curDriver.JumpToToil(nextToilOnPlaceFailOrIncomplete);
                        }
                        else
                        {
                            R4Log.Warn(
                                $"PlaceHauledThingInCell: incomplete haul for {actor}. Could not find anywhere to put {actor.carryTracker.CarriedThing} near {actor.Position}. Destroying.");
                            actor.carryTracker.CarriedThing.Destroy();
                        }
                    }
                    else if (nextToilOnPlaceFailOrIncomplete != null)
                    {
                        actor.jobs.curDriver.JumpToToil(nextToilOnPlaceFailOrIncomplete);
                    }

                    R4Log.Debug(
                        $"PlaceHauledThingInCell: pawn={actor.LabelShort} jobId={curJob.loadID} direct drop failed for {curJob.def.defName} " +
                        $"targetCell={cell} tracked={MaterialUtility.DescribePlacedThings(curJob)}");
                }
                else
                {
                    Thing resolvedThing = trackedThing ?? resultingThing;
                    if (trackedThing == null || trackedAdded <= 0)
                    {
                        R4Log.Warn(
                            $"PlaceHauledThingInCell: pawn={actor.LabelShort} jobId={curJob.loadID} drop succeeded for {curJob.def.defName} at {cell} " +
                            $"but no placed-action callback fired. Result={DescribeThing(resolvedThing)}");
                    }
                    else
                    {
                        R4Log.Debug(
                            $"PlaceHauledThingInCell: pawn={actor.LabelShort} jobId={curJob.loadID} tracked {trackedAdded} of " +
                            $"{resolvedThing?.def?.defName ?? "null"} ref={DescribeThingReference(resolvedThing)} targetCell={cell} " +
                            $"actual={DescribeThing(resolvedThing)} trackedNow={MaterialUtility.DescribePlacedThings(curJob)}");
                    }
                }
            };
        }

        private static bool IsR4WorkJob(Job job)
        {
            return job?.def == R4DefOf.RRRR_Repair || job?.def == R4DefOf.RRRR_Clean;
        }

        private static void AbortRedirectedHaul(Pawn actor, Job curJob, string reason)
        {
            R4Log.Warn(
                $"PlaceHauledThingInCell: pawn={actor.LabelShort} jobId={curJob.loadID} aborting redirected haul for {curJob.def.defName}: {reason}");
            actor.jobs.curDriver.EndJobWith(JobCondition.Incompletable);
        }

        private static string DescribeThing(Thing thing)
        {
            if (thing == null)
                return "null";

            string location = thing.Spawned ? thing.PositionHeld.ToString() : "unspawned";
            return $"{thing.def.defName} stack={thing.stackCount} at={location}";
        }

        private static string DescribeThingReference(Thing thing)
        {
            if (thing == null)
                return "null";

            string location = thing.Spawned ? thing.PositionHeld.ToString() : "unspawned";
            string uniqueId = thing.ThingID ?? thing.GetUniqueLoadID();
            return $"{thing.def.defName}[{uniqueId}] stack={thing.stackCount} at={location}";
        }
    }
}
