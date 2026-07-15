using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RRRR
{
    /// <summary>
    /// Remembers which R4 designations (Repair / Recycle / Clean) were on an item
    /// at the moment it was hauled into a pawn's inventory, so they can be
    /// re-applied when the same item is dropped back onto a map.
    ///
    /// Background: when an item enters a pawn's inventory, vanilla
    /// <c>ThingOwner.NotifyAdded</c> strips all of its map designations (because
    /// <c>Pawn_InventoryTracker</c> is treated as an enclosing container). Mods
    /// like Pick Up And Haul route haulables through inventory for bulk hauling,
    /// so without this tracker the player's repair/recycle/clean marks vanish in
    /// transit. The capture patch records the designations just before the strip;
    /// the restore patch re-applies them when the item respawns.
    ///
    /// Keyed by <see cref="Thing.thingIDNumber"/>, which is stable across the
    /// round-trip because R4 items are non-stackable (apparel/weapons) — a
    /// <c>SplitOff</c> of a full stack returns the same Thing and inventory never
    /// merges it with another stack.
    ///
    /// Always active — there is no setting to gate it; the capture/restore pair is
    /// read-only on the strip path and additive (with a fresh eligibility check) on
    /// respawn, so it is safe to run unconditionally for any inventory-hauling mod.
    /// </summary>
    public class R4HauledDesignationTracker : GameComponent
    {
        /// <summary>Cached singleton so hot patches avoid scanning Game.components.</summary>
        public static R4HauledDesignationTracker Instance { get; private set; }

        // How long a record may linger before pruning (items destroyed/traded while
        // in inventory never respawn, so their entries are never consumed).
        private const int MaxRecordAgeTicks = 1_800_000; // ~30 in-game days
        private const int MaxRecords        = 2000;
        private const int PruneIntervalTicks = 60_000;

        private Dictionary<int, Entry> _records = new Dictionary<int, Entry>();

        // Scratch list reused by ExposeData (save side only).
        private List<Entry> _scribeList;

        public R4HauledDesignationTracker(Game game)
        {
            Instance = this;
        }

        public bool IsEmpty => _records.Count == 0;

        /// <summary>
        /// Record the R4 designations present on the item being hauled into
        /// inventory. <paramref name="defs"/> is copied; the caller may reuse it.
        /// </summary>
        public void Capture(int thingID, List<DesignationDef> defs)
        {
            if (defs == null || defs.Count == 0)
                return;

            _records[thingID] = new Entry
            {
                thingID = thingID,
                defs = new List<DesignationDef>(defs),
                capturedTick = Find.TickManager?.TicksGame ?? 0,
            };
        }

        /// <summary>
        /// Remove and return the designations recorded for this item, if any.
        /// </summary>
        public bool TryConsume(int thingID, out List<DesignationDef> defs)
        {
            if (_records.TryGetValue(thingID, out Entry entry))
            {
                _records.Remove(thingID);
                defs = entry.defs;
                return true;
            }

            defs = null;
            return false;
        }

        public void Clear()
        {
            _records.Clear();
        }

        /// <summary>
        /// Load-time sweep (runs on game load, not new games): drop any R4 designation
        /// that survived in a save but whose target item is no longer eligible for that
        /// operation — e.g. the item became excluded by a new blacklist entry, trade-tag
        /// rule, or eligibility change in a mod update. The WorkGivers already refuse to
        /// act on such designations; this additionally clears the orphaned map overlay so
        /// it doesn't linger. Distinct from the inventory round-trip tracking above; it
        /// shares this component only to avoid adding a second GameComponent.
        /// </summary>
        public override void LoadedGame()
        {
            base.LoadedGame();
            CleanStaleDesignations();
        }

        private static void CleanStaleDesignations()
        {
            List<Map> maps = Find.Maps;
            if (maps == null)
                return;

            for (int m = 0; m < maps.Count; m++)
            {
                DesignationManager dm = maps[m]?.designationManager;
                if (dm == null)
                    continue;

                RemoveIneligible(dm, R4DefOf.R4_Repair,  def => R4WorkbenchFilterCache.IsRepairEligible(def));
                RemoveIneligible(dm, R4DefOf.R4_Recycle, def => R4WorkbenchFilterCache.IsRecycleEligible(def));
                RemoveIneligible(dm, R4DefOf.R4_Clean,   def => R4WorkbenchFilterCache.IsCleanEligible(def));
            }
        }

        private static void RemoveIneligible(DesignationManager dm, DesignationDef desDef, Func<ThingDef, bool> eligible)
        {
            // Collect first — RemoveDesignation mutates the manager's list.
            List<Designation> toRemove = null;
            foreach (Designation des in dm.SpawnedDesignationsOfDef(desDef))
            {
                Thing t = des.target.Thing;
                if (t == null || !eligible(t.def))
                    (toRemove ?? (toRemove = new List<Designation>())).Add(des);
            }

            if (toRemove == null)
                return;

            for (int i = 0; i < toRemove.Count; i++)
                dm.RemoveDesignation(toRemove[i]);
        }

        public override void GameComponentTick()
        {
            if (_records.Count == 0)
                return;

            if (Find.TickManager.TicksGame % PruneIntervalTicks != 0)
                return;

            Prune();
        }

        private void Prune()
        {
            int now = Find.TickManager.TicksGame;

            // Age-based prune.
            List<int> stale = null;
            foreach (var kvp in _records)
            {
                if (now - kvp.Value.capturedTick > MaxRecordAgeTicks)
                {
                    if (stale == null)
                        stale = new List<int>();
                    stale.Add(kvp.Key);
                }
            }
            if (stale != null)
            {
                for (int i = 0; i < stale.Count; i++)
                    _records.Remove(stale[i]);
            }

            // Hard cap — evict oldest first.
            if (_records.Count > MaxRecords)
            {
                var ordered = new List<Entry>(_records.Values);
                ordered.Sort((a, b) => a.capturedTick.CompareTo(b.capturedTick));
                int toRemove = _records.Count - MaxRecords;
                for (int i = 0; i < toRemove; i++)
                    _records.Remove(ordered[i].thingID);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                _scribeList = new List<Entry>(_records.Values);
            }

            Scribe_Collections.Look(ref _scribeList, "records", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                _records = new Dictionary<int, Entry>();
                if (_scribeList != null)
                {
                    for (int i = 0; i < _scribeList.Count; i++)
                    {
                        Entry e = _scribeList[i];
                        // Drop entries whose defs failed to resolve (mod/def removed).
                        e.defs?.RemoveAll(d => d == null);
                        if (e.defs != null && e.defs.Count > 0)
                            _records[e.thingID] = e;
                    }
                }
                _scribeList = null;
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                Instance = this;
        }

        /// <summary>One persisted record. A simple IExposable class (not a record/struct).</summary>
        public class Entry : IExposable
        {
            public int thingID;
            public List<DesignationDef> defs;
            public int capturedTick;

            public void ExposeData()
            {
                Scribe_Values.Look(ref thingID, "thingID", 0);
                Scribe_Values.Look(ref capturedTick, "capturedTick", 0);
                Scribe_Collections.Look(ref defs, "defs", LookMode.Def);
            }
        }
    }
}
