using System.Collections.Generic;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace RRRR
{
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.EndCurrentJob))]
    public static class Patch_PawnJobTracker_EndCurrentJob
    {
        static void Prefix(Pawn_JobTracker __instance, JobCondition condition)
        {
            Job curJob = __instance.curJob;
            if (curJob?.def != R4DefOf.RRRR_Repair && curJob?.def != R4DefOf.RRRR_Clean)
                return;

            Pawn pawn = __instance.curDriver?.pawn;
            string toil = __instance.curDriver?.CurToilString ?? "null";
            Thing workItem = GetWorkItem(curJob);
            R4Log.Debug(
                $"EndCurrentJob {curJob.def.defName}: pawn={pawn?.LabelShort ?? "null"} jobId={curJob.loadID} condition={condition} toil={toil} " +
                $"item={DescribeItem(workItem)} tracked={MaterialUtility.DescribePlacedThings(curJob)}");
        }

        private static Thing GetWorkItem(Job job)
        {
            List<LocalTargetInfo> queue = job?.targetQueueA;
            if (queue == null || queue.Count == 0)
                return null;

            return queue[0].Thing;
        }

        private static string DescribeItem(Thing thing)
        {
            if (thing == null)
                return "null";

            return thing.def.useHitPoints
                ? $"{thing.LabelShort} hp={thing.HitPoints}/{thing.MaxHitPoints} spawned={thing.Spawned} pos={thing.PositionHeld}"
                : $"{thing.LabelShort} spawned={thing.Spawned} pos={thing.PositionHeld}";
        }

    }
}