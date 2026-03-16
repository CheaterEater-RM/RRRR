using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RRRR
{
    public enum R4Designation
    {
        None,
        MarkedRecycle,
        MarkedRepair,
        MarkedClean
    }

    public class CompRecyclable : ThingComp
    {
        public R4Designation Designation = R4Designation.None;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref Designation, "r4Designation", R4Designation.None);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            // Only show gizmos for items on the ground/stockpile, not forbidden, player-accessible
            if (!parent.Spawned || parent.IsForbidden(Faction.OfPlayer))
                yield break;

            // Don't show gizmos for equipped or carried items
            if (parent.ParentHolder is Pawn_ApparelTracker || parent.ParentHolder is Pawn_EquipmentTracker
                || parent.ParentHolder is Pawn_InventoryTracker)
                yield break;

            bool hasMaterials = MaterialUtility.GetBaseMaterials(parent).Count > 0;

            // ── Recycle gizmo ───────────────────────────────────────────
            if (hasMaterials)
            {
                if (Designation == R4Designation.MarkedRecycle)
                {
                    yield return new Command_Action
                    {
                        defaultLabel = "RRRR_Gizmo_CancelRecycle".Translate(),
                        defaultDesc = "RRRR_Gizmo_CancelRecycle_Desc".Translate(),
                        icon = ContentFinder<Texture2D>.Get("UI/Designators/Cancel", true),
                        action = () => Designation = R4Designation.None,
                        Order = -90f
                    };
                }
                else if (Designation == R4Designation.None)
                {
                    var returnPreview = MaterialUtility.GetRecycleReturnPreview(parent);
                    yield return new Command_Action
                    {
                        defaultLabel = "RRRR_Gizmo_Recycle".Translate(),
                        defaultDesc = "RRRR_Gizmo_Recycle_Desc".Translate() + "\n\n" + FormatReturnPreview(returnPreview),
                        icon = ContentFinder<Texture2D>.Get("UI/Designators/Strip", true), // placeholder icon
                        action = () => Designation = R4Designation.MarkedRecycle,
                        Order = -90f
                    };
                }
            }
            else if (Designation == R4Designation.None)
            {
                // Show disabled gizmo with explanation
                var disabledRecycle = new Command_Action
                {
                    defaultLabel = "RRRR_Gizmo_Recycle".Translate(),
                    defaultDesc = "RRRR_Gizmo_NoMaterials".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Designators/Strip", true),
                    Order = -90f
                };
                disabledRecycle.Disable("RRRR_Gizmo_NoMaterials".Translate());
                yield return disabledRecycle;
            }

            // ── Repair gizmo (M2) ──────────────────────────────────────
            if (parent.HitPoints < parent.MaxHitPoints && hasMaterials)
            {
                if (Designation == R4Designation.MarkedRepair)
                {
                    yield return new Command_Action
                    {
                        defaultLabel = "RRRR_Gizmo_CancelRepair".Translate(),
                        defaultDesc = "RRRR_Gizmo_CancelRepair_Desc".Translate(),
                        icon = ContentFinder<Texture2D>.Get("UI/Designators/Cancel", true),
                        action = () => Designation = R4Designation.None,
                        Order = -89f
                    };
                }
                else if (Designation == R4Designation.None)
                {
                    var disabledRepair = new Command_Action
                    {
                        defaultLabel = "RRRR_Gizmo_Repair".Translate(),
                        defaultDesc = "RRRR_Gizmo_Repair_Desc".Translate(),
                        icon = ContentFinder<Texture2D>.Get("UI/Designators/Claim", true), // placeholder icon
                        Order = -89f
                    };
                    disabledRepair.Disable("Coming in a future update.");
                    yield return disabledRepair;
                }
            }

            // ── Clean gizmo (M3) ───────────────────────────────────────
            if (parent is Apparel apparel && apparel.WornByCorpse)
            {
                if (Designation == R4Designation.MarkedClean)
                {
                    yield return new Command_Action
                    {
                        defaultLabel = "RRRR_Gizmo_CancelClean".Translate(),
                        defaultDesc = "RRRR_Gizmo_CancelClean_Desc".Translate(),
                        icon = ContentFinder<Texture2D>.Get("UI/Designators/Cancel", true),
                        action = () => Designation = R4Designation.None,
                        Order = -88f
                    };
                }
                else if (Designation == R4Designation.None)
                {
                    var disabledClean = new Command_Action
                    {
                        defaultLabel = "RRRR_Gizmo_Clean".Translate(),
                        defaultDesc = "RRRR_Gizmo_Clean_Desc".Translate(),
                        icon = ContentFinder<Texture2D>.Get("UI/Designators/Unforbid", true), // placeholder icon
                        Order = -88f
                    };
                    disabledClean.Disable("Coming in a future update.");
                    yield return disabledClean;
                }
            }
        }

        public override void PostDraw()
        {
            base.PostDraw();
            if (Designation == R4Designation.None)
                return;

            // Draw a small overlay icon above the item to indicate designation
            // Using the vanilla overlay system position offset
            Vector3 drawPos = parent.DrawPos;
            drawPos.y = AltitudeLayer.MetaOverlays.AltitudeFor();
            drawPos.z += 0.35f;

            Material mat;
            switch (Designation)
            {
                case R4Designation.MarkedRecycle:
                    mat = MaterialPool.MatFrom("UI/Designators/Strip", ShaderDatabase.MetaOverlay); // placeholder
                    break;
                case R4Designation.MarkedRepair:
                    mat = MaterialPool.MatFrom("UI/Designators/Claim", ShaderDatabase.MetaOverlay);
                    break;
                case R4Designation.MarkedClean:
                    mat = MaterialPool.MatFrom("UI/Designators/Unforbid", ShaderDatabase.MetaOverlay);
                    break;
                default:
                    return;
            }

            // Draw at 0.5 scale
            Vector3 s = new Vector3(0.5f, 1f, 0.5f);
            Matrix4x4 matrix = default;
            matrix.SetTRS(drawPos, Quaternion.identity, s);
            Graphics.DrawMesh(MeshPool.plane10, matrix, mat, 0);
        }

        public override string CompInspectStringExtra()
        {
            switch (Designation)
            {
                case R4Designation.MarkedRecycle:
                    return "RRRR_Gizmo_Recycle".Translate();
                case R4Designation.MarkedRepair:
                    return "RRRR_Gizmo_Repair".Translate();
                case R4Designation.MarkedClean:
                    return "RRRR_Gizmo_Clean".Translate();
                default:
                    return null;
            }
        }

        private string FormatReturnPreview(List<ThingDefCountClass> returns)
        {
            if (returns == null || returns.Count == 0)
                return "RRRR_Gizmo_NoMaterials".Translate();

            var sb = new System.Text.StringBuilder();
            sb.Append("~");
            for (int i = 0; i < returns.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(returns[i].count).Append("x ").Append(returns[i].thingDef.LabelCap);
            }
            sb.Append(" (skill 10)");
            return sb.ToString();
        }
    }
}
