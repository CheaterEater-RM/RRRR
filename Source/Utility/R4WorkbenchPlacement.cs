using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RRRR
{
    public static class R4WorkbenchPlacement
    {
        private const int MaxSearchCells = 200;

        public static bool TryGetPreferredDisplayCell(Thing bench, Thing workItem, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;

            if (bench == null || workItem == null || bench.MapHeld == null)
                return false;

            List<IntVec3> occupiedCells = GetOrderedOccupiedBenchCells(bench);
            if (occupiedCells.Count <= 1)
                return false;

            for (int i = 0; i < occupiedCells.Count; i++)
            {
                IntVec3 candidate = occupiedCells[i];
                if (GenSpawn.CanSpawnAt(workItem.def, candidate, bench.MapHeld))
                {
                    cell = candidate;
                    return true;
                }
            }

            return false;
        }

        public static IEnumerable<IntVec3> IngredientPlaceCellsInOrder(Job job, Thing bench, Thing ingredient, Thing workItem)
        {
            if (bench?.MapHeld == null)
                yield break;

            Map map = bench.MapHeld;
            IntVec3 interactionCell = GetInteractionCell(bench);
            List<IntVec3> occupiedCells = GetOrderedOccupiedBenchCells(bench);
            HashSet<IntVec3> visitedCells = new HashSet<IntVec3>();

            IntVec3 displayCell = IntVec3.Invalid;
            bool reserveDisplayCell = occupiedCells.Count > 1 && TryGetPreferredDisplayCell(bench, workItem, out displayCell);

            for (int i = 0; i < occupiedCells.Count; i++)
            {
                IntVec3 cell = occupiedCells[i];
                visitedCells.Add(cell);

                if (reserveDisplayCell && cell == displayCell)
                    continue;

                yield return cell;
            }

            int maxSteps = Mathf.Min(MaxSearchCells, GenRadial.RadialPattern.Length);
            for (int i = 0; i < maxSteps; i++)
            {
                IntVec3 cell = interactionCell + GenRadial.RadialPattern[i];
                if (visitedCells.Contains(cell) || !cell.InBounds(map))
                    continue;

                Building edifice = cell.GetEdifice(map);
                if (edifice != null &&
                    edifice.def.passability == Traversability.Impassable &&
                    edifice.def.surfaceType == SurfaceType.None)
                {
                    continue;
                }

                yield return cell;
            }
        }

        public static bool TryPlaceCarriedWorkItemAtBench(Pawn pawn, Thing bench, Job job, out Thing placedThing, out string failReason)
        {
            placedThing = null;
            failReason = null;

            if (pawn?.carryTracker?.CarriedThing == null)
                return true;

            if (bench?.MapHeld == null || job == null)
            {
                failReason = "missing bench or job";
                return false;
            }

            Thing carriedThing = pawn.carryTracker.CarriedThing;
            int attemptedCells = 0;

            foreach (IntVec3 cell in WorkItemDisplayCellsInOrder(bench, carriedThing))
            {
                attemptedCells++;

                if (!CellAcceptsWorkItemDisplay(job, bench, carriedThing, cell, out string rejectionReason))
                {
                    failReason = rejectionReason;
                    continue;
                }

                if (pawn.carryTracker.TryDropCarriedThing(cell, ThingPlaceMode.Direct, out placedThing))
                    return true;

                failReason = $"direct drop failed at {cell}";
            }

            if (attemptedCells == 0 && failReason == null)
                failReason = "no candidate display cells were available";

            return false;
        }

        public static bool CellAcceptsTrackedIngredientPlacement(Job job, Thing carriedThing, List<Thing> thingsAtCell, out string rejectionReason)
        {
            rejectionReason = null;

            if (thingsAtCell == null || thingsAtCell.Count == 0)
                return true;

            for (int i = 0; i < thingsAtCell.Count; i++)
            {
                Thing existingThing = thingsAtCell[i];
                if (existingThing == null || existingThing.def.category != ThingCategory.Item)
                    continue;

                if (!existingThing.CanStackWith(carriedThing))
                {
                    rejectionReason = $"blocked by non-stackable {MaterialUtility.DescribeThingReference(existingThing)}";
                    return false;
                }

                if (existingThing.stackCount >= existingThing.def.stackLimit)
                {
                    rejectionReason = $"blocked by full stack {MaterialUtility.DescribeThingReference(existingThing)}";
                    return false;
                }

                if (!IsTrackedByJob(job, existingThing))
                {
                    rejectionReason = $"blocked by foreign stack {MaterialUtility.DescribeThingReference(existingThing)}";
                    return false;
                }
            }

            return true;
        }

        private static IEnumerable<IntVec3> WorkItemDisplayCellsInOrder(Thing bench, Thing workItem)
        {
            Map map = bench.MapHeld;
            if (map == null)
                yield break;

            HashSet<IntVec3> yieldedCells = new HashSet<IntVec3>();
            List<IntVec3> occupiedCells = GetOrderedOccupiedBenchCells(bench);

            if (TryGetPreferredDisplayCell(bench, workItem, out IntVec3 preferredCell))
            {
                yieldedCells.Add(preferredCell);
                yield return preferredCell;
            }

            for (int i = 0; i < occupiedCells.Count; i++)
            {
                IntVec3 cell = occupiedCells[i];
                if (yieldedCells.Contains(cell))
                    continue;

                yieldedCells.Add(cell);
                yield return cell;
            }

            IntVec3 interactionCell = GetInteractionCell(bench);
            int maxSteps = Mathf.Min(MaxSearchCells, GenRadial.RadialPattern.Length);
            for (int i = 0; i < maxSteps; i++)
            {
                IntVec3 cell = interactionCell + GenRadial.RadialPattern[i];
                if (yieldedCells.Contains(cell) || !cell.InBounds(map))
                    continue;

                Building edifice = cell.GetEdifice(map);
                if (edifice != null &&
                    edifice.def.passability == Traversability.Impassable &&
                    edifice.def.surfaceType == SurfaceType.None)
                {
                    continue;
                }

                yieldedCells.Add(cell);
                yield return cell;
            }
        }

        private static bool CellAcceptsWorkItemDisplay(Job job, Thing bench, Thing workItem, IntVec3 cell, out string rejectionReason)
        {
            rejectionReason = null;
            Map map = bench.MapHeld;

            if (map == null)
            {
                rejectionReason = "bench has no map";
                return false;
            }

            if (!GenSpawn.CanSpawnAt(workItem.def, cell, map))
            {
                rejectionReason = $"cell {cell} cannot spawn {workItem.def.defName}";
                return false;
            }

            List<Thing> thingsAtCell = map.thingGrid.ThingsListAt(cell);
            for (int i = 0; i < thingsAtCell.Count; i++)
            {
                Thing existingThing = thingsAtCell[i];
                if (existingThing == null || existingThing == bench)
                    continue;

                if (IsTrackedByJob(job, existingThing))
                {
                    rejectionReason = $"cell {cell} contains tracked ingredient {MaterialUtility.DescribeThingReference(existingThing)}";
                    return false;
                }

                if (existingThing.def.category == ThingCategory.Item)
                {
                    rejectionReason = $"cell {cell} contains item {MaterialUtility.DescribeThingReference(existingThing)}";
                    return false;
                }
            }

            return true;
        }

        private static bool IsTrackedByJob(Job job, Thing thing)
        {
            if (job?.placedThings == null || thing == null)
                return false;

            for (int i = 0; i < job.placedThings.Count; i++)
            {
                ThingCountClass placedThing = job.placedThings[i];
                if (placedThing?.thing == thing)
                    return true;
            }

            return false;
        }

        private static List<IntVec3> GetOrderedOccupiedBenchCells(Thing bench)
        {
            var cells = new List<IntVec3>();
            if (bench == null)
                return cells;

            if (bench is IBillGiver billGiver)
            {
                foreach (IntVec3 cell in billGiver.IngredientStackCells)
                    cells.Add(cell);
            }
            else
            {
                foreach (IntVec3 cell in GenAdj.CellsOccupiedBy(bench))
                    cells.Add(cell);
            }

            IntVec3 interactionCell = GetInteractionCell(bench);
            cells.Sort((a, b) =>
                (a - interactionCell).LengthHorizontalSquared.CompareTo((b - interactionCell).LengthHorizontalSquared));

            return cells;
        }

        private static IntVec3 GetInteractionCell(Thing bench)
        {
            if (bench is Building building && building.def.hasInteractionCell)
                return building.InteractionCell;

            return bench?.Position ?? IntVec3.Invalid;
        }
    }
}