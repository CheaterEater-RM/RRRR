# RimWorld Hotkey Reference
*RimWorld 1.6 · `net48`*

Hotkeys in RimWorld flow through a single system regardless of where the button appears — a `Command_Action` on a selected thing, a `Designator` in the Orders menu, or a `Designator` in the Architect menu. The mechanism is identical in all three cases because `Designator` inherits from `Command`. The only difference is *where* your class lives and *how* the hotkey def is created.

---

## The Core Mechanism — `Command.hotKey`

`Command` (the base class for all button-style gizmos) exposes one field:

```csharp
public KeyBindingDef hotKey;
```

During `GizmoOnGUIInt`, the engine:
1. Reads `hotKey.MainKey` — the player's currently bound key (respects rebinding).
2. Checks `GizmoGridDrawer.drawnHotKeys` — a per-frame `HashSet<KeyCode>` that prevents two buttons claiming the same key in one draw pass. The first button drawn wins.
3. If the key is not yet claimed, renders the key label in the button's top-left corner.
4. Calls `hotKey.KeyDownEvent` to detect activation.

**Assigning `hotKey` is all you need.** Label display, input detection, and per-frame deduplication are vanilla-automatic.

### What `KeyDownEvent` checks

- `Event.current.type == EventType.KeyDown`
- Key matches `keyBindingA` or `keyBindingB` from the player's saved prefs
- No search widget is focused
- `⌘` (Meta/Command) is not held unless the binding itself is a meta key

---

## Step 1 — Define a `KeyBindingDef` in XML

Always define your own def. Do not reuse a vanilla `Misc*` def if you want a labelled, rebindable key.

```xml
<!-- Defs/KeyBindingDefs.xml -->
<Defs>

  <KeyBindingCategoryDef>
    <defName>MyMod_KeyCategory</defName>
    <label>My Mod</label>
  </KeyBindingCategoryDef>

  <KeyBindingDef>
    <defName>MyMod_DoThing</defName>
    <label>Do Thing</label>
    <category>MyMod_KeyCategory</category>
    <defaultKeyCodeA>H</defaultKeyCodeA>
    <!-- <defaultKeyCodeB>None</defaultKeyCodeB>  optional second binding -->
  </KeyBindingDef>

</Defs>
```

This registers the binding in **Options → Key Bindings** so the player can rebind it. `defaultKeyCodeA` is the shipped default; `defaultKeyCodeB` is an optional alternative.

---

## Step 2a — Gizmo on a Selected Thing (`Command_Action`)

Override `GetGizmos()` on your `Thing`, `ThingComp`, or `MapComponent` and assign `hotKey`:

```csharp
public override IEnumerable<Gizmo> GetGizmos()
{
    yield return new Command_Action
    {
        defaultLabel = "Do Thing",
        defaultDesc  = "Does the thing.",
        icon         = ContentFinder<Texture2D>.Get("UI/Icons/MyIcon"),
        hotKey       = MyMod_KeyBindingDefOf.MyMod_DoThing,
        action       = () => DoThing()
    };
}
```

The gizmo appears in the bottom bar when the thing is selected. The hotkey is active whenever that bar is visible.

---

## Step 2b — Designator in an Orders / Architect Menu

`Designator` inherits `Command.hotKey` directly. Assign it in the constructor:

```csharp
public class Designator_MyOrder : Designator
{
    public Designator_MyOrder()
    {
        defaultLabel  = "MyOrderLabel".Translate();
        defaultDesc   = "MyOrderDesc".Translate();
        icon          = ContentFinder<Texture2D>.Get("UI/Designators/MyOrder");
        soundSucceeded = SoundDefOf.Designate_PlaceBuilding;
        hotKey        = MyMod_KeyBindingDefOf.MyMod_DoThing;
        isOrder       = true;   // shows in Orders panel; omit for Architect tab
    }

    public override AcceptanceReport CanDesignateCell(IntVec3 loc) { ... }
    public override void DesignateSingleCell(IntVec3 c)            { ... }
}
```

For **Orders menu** designators (`isOrder = true`), register the class via XML patch:

```xml
<!-- Patches/AddMyOrder.xml -->
<Operation Class="PatchOperationAdd">
  <xpath>Defs/DesignationCategoryDef[defName="Orders"]/specialDesignatorClasses</xpath>
  <value>
    <li>MyNamespace.Designator_MyOrder</li>
  </value>
</Operation>
```

For **Architect tab** designators, use a `DesignationCategoryDef` with `BuildableDef` entries as normal — the designator is instantiated by the architect menu system rather than `specialDesignatorClasses`.

---

## Step 3 — Expose the Def via `[DefOf]` (Recommended)

Avoid `DefDatabase<KeyBindingDef>.GetNamed("...")` string lookups at runtime:

```csharp
[DefOf]
public static class MyMod_KeyBindingDefOf
{
    public static KeyBindingDef MyMod_DoThing;

    static MyMod_KeyBindingDefOf()
    {
        DefOfHelper.EnsureInitializedInCtor(typeof(MyMod_KeyBindingDefOf));
    }
}
```

The `[DefOf]` attribute auto-populates the static field after Defs load. Reference it as `MyMod_KeyBindingDefOf.MyMod_DoThing` anywhere in C#.

---

## Vanilla `Misc*` Bindings — When to Reuse Them

Vanilla ships `Misc1`–`Misc12` as anonymous hotkey slots. They appear in Key Bindings as "Misc 1" through "Misc 12" with no player-visible label. Vanilla designators reuse them:

| Vanilla designator | Hotkey def |
|---|---|
| Mine | `KeyBindingDefOf.Misc10` |
| Cancel | `KeyBindingDefOf.Designator_Cancel` |
| Deconstruct | `KeyBindingDefOf.Designator_Deconstruct` |

**Reuse a `Misc*` def only if:** your button is a single, globally-meaningful action and you are comfortable sharing its slot with whatever vanilla or another mod may assign there. For any mod-specific action, define your own `KeyBindingDef` so it gets its own labelled row in the settings screen.

---

## Behaviour Reference

| Behaviour | Detail |
|---|---|
| **Label rendering** | `hotKey.MainKey.ToStringReadable()` drawn top-left of the button, automatically |
| **Per-frame deduplication** | `GizmoGridDrawer.drawnHotKeys` — first button drawn claims the key; later buttons with the same key show no label and do not respond |
| **Command grouping** | `Command.GroupsWith` requires matching `hotKey`, `Label`, `icon`, and `groupKey` — if you intend grouping across multi-selected things, keep these consistent |
| **Disabled state** | Keypress still intercepted but returns `GizmoState.Mouseover` and shows `disabledReason` message — no extra work needed |
| **Player rebinding** | Always use `hotKey.MainKey` / `hotKey.KeyDownEvent`; never hardcode `KeyCode` values in logic |
| **Scope** | Gizmo hotkeys are only active when the bottom bar containing them is visible; designator hotkeys only while the designator panel is open |
