using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RRRR
{
    /// <summary>
    /// Soft dependency on Bill Ingredient Source (lks.billingredientsource).
    /// When BIS is loaded and a bill has a storage source configured,
    /// restricts which items are eligible for repair/clean to items
    /// located in that storage. Material ingredients are unaffected —
    /// they use vanilla's TryFindBestFixedIngredients unconditionally.
    ///
    /// Design note: BIS was designed for ingredient sources, but R4's
    /// repair/clean bills have a two-tier structure (item + materials).
    /// We apply the storage filter to the item tier because "only repair
    /// items from this stockpile" is the useful player-facing control.
    /// Materials are fungible and usually near the bench.
    /// </summary>
    public static class BISCompat
    {
        private static bool _initialized;
        private static bool _bisActive;

        // BillDataStore.TryGet(Bill, out BillData) → bool
        private static System.Reflection.MethodInfo _tryGetMethod;
        // BillData.UseAllStorages field
        private static AccessTools.FieldRef<object, bool> _useAllStoragesRef;
        // BillData.SelectedStorageGroup field
        private static AccessTools.FieldRef<object, object> _selectedStorageGroupRef;
        // StorageIngredientSource.GetCandidateThings(Map, Bill_Production) → List<Thing>
        private static System.Reflection.MethodInfo _getCandidateThingsMethod;

        public static bool IsActive => _bisActive;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            // BIS package ID has capital L: "Lk.billingredientsource"
            _bisActive = ModsConfig.IsActive("Lk.billingredientsource");
            if (!_bisActive)
            {
                R4Log.Debug("BIS not active — BISCompat disabled.");
                return;
            }

            try
            {
                var billDataStoreType = AccessTools.TypeByName("BillIngredientSource.BillDataStore");
                var billDataType = AccessTools.TypeByName("BillIngredientSource.BillData");
                var storageSourceType = AccessTools.TypeByName("BillIngredientSource.StorageIngredientSource");

                if (billDataStoreType == null || billDataType == null || storageSourceType == null)
                {
                    R4Log.Warn("BIS types not found — BISCompat disabled.");
                    _bisActive = false;
                    return;
                }

                _tryGetMethod = AccessTools.Method(billDataStoreType, "TryGet");
                _useAllStoragesRef = AccessTools.FieldRefAccess<bool>(billDataType, "UseAllStorages");
                _selectedStorageGroupRef = AccessTools.FieldRefAccess<object>(billDataType, "SelectedStorageGroup");
                _getCandidateThingsMethod = AccessTools.Method(storageSourceType, "GetCandidateThings",
                    new Type[] { typeof(Map), typeof(Bill_Production) });

                if (_tryGetMethod == null || _getCandidateThingsMethod == null ||
                    _useAllStoragesRef == null || _selectedStorageGroupRef == null)
                {
                    R4Log.Warn("BIS methods or fields not found — BISCompat disabled. " +
                        $"TryGet={_tryGetMethod != null} GetCandidateThings={_getCandidateThingsMethod != null} " +
                        $"UseAllStorages={_useAllStoragesRef != null} SelectedStorageGroup={_selectedStorageGroupRef != null}");
                    _bisActive = false;
                    return;
                }

                R4Log.Debug("BISCompat initialized successfully.");
            }
            catch (Exception e)
            {
                R4Log.Warn($"BISCompat initialization failed: {e.Message}");
                _bisActive = false;
            }
        }

        /// <summary>
        /// If BIS has a storage source configured for this bill, returns a
        /// HashSet of thingIDNumbers for all items in that storage. The caller
        /// uses this to filter candidate items (the things being repaired/cleaned).
        /// Returns null if BIS is not active, not configured, or no storage selected.
        /// </summary>
        public static HashSet<int> GetStorageCandidateIDs(Bill bill, Map map)
        {
            var things = GetStorageCandidateThings(bill, map);
            if (things == null)
                return null;

            var idSet = new HashSet<int>(things.Count);
            for (int i = 0; i < things.Count; i++)
                idSet.Add(things[i].thingIDNumber);

            return idSet;
        }

        /// <summary>
        /// If BIS has a storage source configured for this bill, returns the
        /// List of Things in that storage. Returns null if BIS is not active,
        /// not configured, no storage selected, or the storage is empty.
        /// </summary>
        public static List<Thing> GetStorageCandidateThings(Bill bill, Map map)
        {
            if (!_bisActive || bill == null || map == null)
                return null;

            var productionBill = bill as Bill_Production;
            if (productionBill == null)
                return null;

            try
            {
                var args = new object[] { productionBill, null };
                bool found = (bool)_tryGetMethod.Invoke(null, args);
                object billData = args[1];

                if (!found || billData == null)
                {
                    R4Log.Debug($"BISCompat: no BillData for bill {bill.GetUniqueLoadID()}.");
                    return null;
                }

                bool useAllStorages = _useAllStoragesRef(billData);
                object selectedGroup = _selectedStorageGroupRef(billData);

                R4Log.Debug($"BISCompat: bill={bill.GetUniqueLoadID()} useAllStorages={useAllStorages} selectedGroup={selectedGroup} selectedGroupType={selectedGroup?.GetType()?.Name ?? "null"}");

                if (!useAllStorages && selectedGroup == null)
                {
                    R4Log.Debug($"BISCompat: no storage configured for bill {bill.GetUniqueLoadID()}.");
                    return null;
                }

                var candidates = (List<Thing>)_getCandidateThingsMethod.Invoke(null,
                    new object[] { map, productionBill });

                R4Log.Debug($"BISCompat: GetCandidateThings returned {candidates?.Count ?? 0} items for bill {bill.GetUniqueLoadID()}.");

                if (candidates == null || candidates.Count == 0)
                    return null;

                return candidates;
            }
            catch (Exception e)
            {
                R4Log.Warn($"BISCompat.GetStorageCandidateIDs failed: {e.Message}\n{e.StackTrace}");
                return null;
            }
        }
    }
}
