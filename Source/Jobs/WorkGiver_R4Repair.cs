using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RRRR
{
    /// <summary>
    /// Designation-based repair WorkGiver. Registered under three WorkGiverDefs
    /// (Crafting / Smithing / Tailoring) so the work tab column matches the bench.
    /// </summary>
    public class WorkGiver_R4Repair : WorkGiver_R4DesignationBase
    {
        private int _cachedTick = -1;
        private Pawn _cachedPawn;
        private Thing _cachedTarget;
        private bool _cachedForced;
        private Job _cachedJob;

        /// <summary>
        /// Items at or above this HP fraction are minor damage and can be
        /// mended for free without consuming any materials.
        /// </summary>
        public static float MinorMendingThreshold => RRRR_Mod.Settings.minorMendingThreshold;

        protected override DesignationDef DesignationDef => R4DefOf.R4_Repair;

        public static bool IsMinorMending(Thing item)
        {
            if (!item.def.useHitPoints || item.MaxHitPoints <= 0)
                return false;
            return (float)item.HitPoints / item.MaxHitPoints >= MinorMendingThreshold;
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return GetOrCreateCachedJob(pawn, t, forced) != null;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return GetOrCreateCachedJob(pawn, t, forced);
        }

        private Job GetOrCreateCachedJob(Pawn pawn, Thing target, bool forced)
        {
            int currentTick = Find.TickManager.TicksGame;
            if (_cachedTick == currentTick && _cachedPawn == pawn && _cachedTarget == target && _cachedForced == forced)
            {
                R4Log.Debug(
                    $"Repair designation cache hit: pawn={pawn.LabelShort} target={target?.ThingID ?? target?.GetUniqueLoadID() ?? "null"} tick={currentTick} hasJob={_cachedJob != null}");
                return _cachedJob;
            }

            Job job = CreateJobOnThing(pawn, target, forced);
            _cachedTick = currentTick;
            _cachedPawn = pawn;
            _cachedTarget = target;
            _cachedForced = forced;
            _cachedJob = job;
            return job;
        }

        private Job CreateJobOnThing(Pawn pawn, Thing t, bool forced)
        {
            DesignationManager designationManager = pawn.Map.designationManager;
            if (designationManager.DesignationOn(t, R4DefOf.R4_Repair) == null)
                return null;
            if (designationManager.DesignationOn(t, R4DefOf.R4_Recycle) != null)
                return null;
            if (t.IsForbidden(pawn) || !pawn.CanReserve(t, 1, -1, null, forced))
                return null;
            if (!ItemHasMatchingBench(pawn, t))
                return null;

            Thing bench = FindBench(pawn, t, forced);
            if (bench == null)
                return null;

            // Clear stale ingredients before starting a new job
            if (bench is IBillGiver bg)
            {
                Job haulOff = WorkGiverUtility.HaulStuffOffBillGiverJob(pawn, bg, null);
                if (haulOff != null) return haulOff;
            }

            Job job = JobMaker.MakeJob(R4DefOf.RRRR_Repair, bench);
            job.count        = 1;
            job.haulMode     = HaulMode.ToCellNonStorage;
            job.placedThings = null;
            job.targetQueueA = new List<LocalTargetInfo> { t };
            job.targetQueueB = new List<LocalTargetInfo>();
            job.countQueue   = new List<int>();

            if (!IsMinorMending(t))
            {
                var cycleCost = MaterialUtility.GetRepairCycleCost(t);
                if (cycleCost.Count > 0)
                {
                    if (!MaterialUtility.TryFindIngredients(cycleCost, pawn, bench.Position, 999f,
                            out var foundThings, out var foundCounts))
                        return null;
                    for (int i = 0; i < foundThings.Count; i++)
                    {
                        job.targetQueueB.Add(foundThings[i]);
                        job.countQueue.Add(foundCounts[i]);
                    }
                }
            }

            R4Log.Debug(
                $"Repair designation scan: pawn={pawn.LabelShort} item={t.ThingID ?? t.GetUniqueLoadID()} bench={bench.def.defName} queuedIngredients={job.targetQueueB.Count}");

            return job;
        }
    }
}
