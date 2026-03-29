# R⁴: Reduce, Reuse, Recycle in RimWorld

Recycle, repair, and clean your weapons and apparel using existing workbenches — no new research, buildings, or technologies required.

## Features

- **Recycle** items into raw materials at the appropriate workbench. Returns scale with item condition, quality, and crafter skill via a smooth sigmoid curve.
- **Repair** damaged gear in skill-based cycles. Higher tech items are harder to fix; unskilled pawns risk making things worse. Minor damage (≤5%) is mended for free.
- **Clean** tainted apparel to remove the corpse-worn penalty, consuming some materials.
- **Per-item gizmos** let you mark specific items for processing — pawns pick up the work automatically. Rich tooltips show bench routing, material costs, and success chances.
- **Orders menu designators** for drag-selecting multiple items at once (recycle, repair, clean taint).
- **Bill support** for automated batch recycling, repairing, and cleaning with full filter control at any appropriate workbench.
- **Comprehensive settings** to tune material returns, repair difficulty, cycle size, and cleaning costs.

## How It Works

Select any weapon or apparel on the ground and use the gizmo buttons to mark it for recycling, repair, or cleaning. A crafter will haul it to the nearest appropriate workbench (tailoring bench for clothes, machining table for guns, smithy for melee weapons, fabrication bench for advanced gear) and get to work.

Alternatively, set up standing bills at workbenches for automated processing — just like any other crafting bill, with full ingredient filtering.

**Workbench routing** is automatic: items go to the bench that originally crafted them. Items without a known crafting source (loot, quest rewards) are routed by tech level.

## Vanilla Smelting

R4 replaces the vanilla SmeltWeapon and SmeltApparel recipes on the electric smelter with its own skill-based recycling bills. The Destroy recipes and ExtractMetalFromSlag are unaffected.

## Save Safety

R4 is safe to add or remove from an ongoing save. It does not add custom components to items or maps. On removal, any pending R4 designations and bills are cleaned up automatically. You may need to re-add vanilla smelting bills to your electric smelters after uninstalling.

## Requirements

- RimWorld 1.6+
- [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077)

## License

MIT
