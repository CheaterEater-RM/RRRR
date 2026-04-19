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

            string toil = __instance.curDriver?.CurToilString ?? "null";
            Thing workItem = GetWorkItem(curJob);
            R4Log.Debug(
                $"EndCurrentJob {curJob.def.defName}: condition={condition} toil={toil} " +
                $"item={DescribeItem(workItem)} tracked={DescribePlacedThings(curJob)}");
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

        private static string DescribePlacedThings(Job job)
        {
            if (job?.placedThings == null || job.placedThings.Count == 0)
                return "none";

            var parts = new List<string>(job.placedThings.Count);
            for (int i = 0; i < job.placedThings.Count; i++)
            {
                ThingCountClass entry = job.placedThings[i];
                if (entry?.thing == null)
                {
                    parts.Add("<null>");
                    continue;
                }

                string location = entry.thing.Spawned ? entry.thing.PositionHeld.ToString() : "unspawned";
                parts.Add($"{entry.thing.def.defName} tracked={entry.Count} stack={entry.thing.stackCount} at={location}");
            }

            return string.Join("; ", parts);
        }
    }
}