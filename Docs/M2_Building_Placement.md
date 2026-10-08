# M2 – Building & Object Placement

Builds on [M1](M1_Grid_World_Camera.md). No paths, enclosures, animals, visitors, staff, economy deductions, quests or research exist; `PlaceableDefinition.cost` is stored but nothing reads it.

## What was added
```
Scripts/World/      Footprint.cs            Rotation90 + pure footprint maths (size swap, cells, snapping)
Scripts/Data/       PlaceableDefinition.cs  PlaceableCatalog.cs  PlacementConfig.cs   (ScriptableObjects)
Scripts/Input/      IPointerClaimant.cs     + PointerInputSource.AddClaimant/RemoveClaimant
Scripts/Placement/  (new assembly ZooGame.Placement)
                    PlacedObject.cs         runtime record: definition, origin, rotation, cells, view
                    PlacementMap.cs         registry + occupancy (writes the grid, tracks owner per cell)
                    PlacementValidation.cs  IPlacementRule, 3 default rules, PlacementValidator, PlacementResult
                    PlacementSession.cs     state machine: select / place / move / rotate / delete (pure C#)
                    PlacementController.cs  MonoBehaviour: input -> session, ghost + selection visuals
                    PlacementVisuals.cs     ghost, footprint plates, placed-object views
Scripts/Gameplay/   PlacementTestPanel.cs   temporary touch UI; ZooSceneBinder wires everything
Scripts/Editor/     M2ProjectSetup.cs       menu ZooGame > M2 > Create Or Update Project Assets
```
Dependencies: `Placement` references Core, Data, World, Input, Unity.InputSystem. `Gameplay` and `Editor` reference `Placement`. `Placement` has no UI dependency; the panel lives in `Gameplay`.

## Architecture
Four layers, each usable without the ones above it:

1. **Footprint maths** (`World/Footprint`): pure functions. A footprint is `width x height` cells (X by Z). Everything below uses the *rotated* size.
2. **`PlacementMap`** – the source of truth for what is placed. `Add / Move / Remove` mutate occupancy atomically and raise `Placed / Moved / Removed`.
3. **`PlacementValidator`** – an ordered list of `IPlacementRule`s evaluated against a `PlacementQuery` (grid, map, definition, origin, rotation, rotated size, object to ignore). Defaults: `BoundsRule`, `UnlockedLandRule`, `UnoccupiedRule`. Reports the first failure (`OutOfBounds`, `LockedLand`, `Occupied`, `RuleFailed`) with the blocking cell. UI and input never inspect cells themselves.
4. **`PlacementSession`** – the interaction state machine (`Idle`, `Placing`, `Moving`) holding the preview (definition, origin, rotation, result) and the selection. Commits to the map only after validation. `PlacementController` is a thin adapter from pointer input and scene objects to the session.

**Extending**: terrain requirements, path access, enclosure requirements and building prerequisites are new `IPlacementRule` classes added with `validator.AddRule(...)`. The session, map, controller and UI do not change; the failure text just appears in the panel (`PlacementFailure.RuleFailed` + `Detail`).

## Occupancy tracking
- Placing marks every footprint cell `CellFlags.Occupied` on the `ZooGrid` and stores the owning object id in a per-cell `int[]` inside `PlacementMap` (same flat indexing as the grid, no per-cell objects). `GetAt(cell)` returns the `PlacedObject`; `OwnerIdAt` returns its id.
- `PlacedObject.Cells` is the authoritative list of cells it occupies. Release always iterates that list, so cells are freed exactly.
- Overlap is impossible: `PlacementMap.Add/Move` refuse (return null/false, change nothing) if any cell is outside the grid or occupied by anything else. This holds even if a caller skips the validator. Locked land is the validator's job.
- **Move**: the object keeps its cells while the preview is shown. The validator is told to ignore the object being moved (`PlacementQuery.Ignore`), and the map treats cells it owns as free. On confirm, `Move` releases the old cells and claims the new ones in one step (so overlapping its own old position is fine). **Cancel therefore needs no restore step**: nothing was changed. If the object is removed mid-move by something else, the session falls back to idle.
- **Delete**: releases all cells, removes the record, destroys the view GameObject, clears the selection.

## Rotation handling
- `Rotation90` = 0/90/180/270. `Footprint.RotatedSize` swaps width and height for 90/270 (2x3 -> 3x2); 1x1 and square footprints are unaffected.
- The *origin* is the minimum-corner cell of the **rotated** rectangle, and rotating keeps the origin cell fixed (so four rotations always return to the start). Cells are `origin + [0, rotatedWidth) x [0, rotatedHeight)`.
- Rotation updates all three consistently: the session recomputes the preview size and **revalidates**; `PlacementMap` fills the new cell list on confirm; and the ghost/view are posed with `PlacementPose` – centre of the rotated rectangle, yaw `90 * step` about Y. A quarter turn of the prefab about its centre covers exactly the swapped rectangle, so visual and cells agree.
- `PlaceableDefinition.AllowRotation = false` makes `Rotate` a no-op.
- Rotating an already placed object (`Rotate` while selected) is validated first (ignoring itself) and refused with a reason if the rotated footprint would collide or leave unlocked land.

## Input and priority
- **Select a placeable**: palette buttons in the temporary panel (Editor: keys 1-9). The ghost appears at the screen centre.
- **Position**: *press on (or within `grabMarginCells` of) the ghost and drag* – the ghost follows the finger with the grab offset preserved and snaps to cells; *tap anywhere farther away* – the ghost snaps there; *drag starting elsewhere* – pans the camera as normal. **Pinch zoom keeps working** (it needs both fingers off the ghost).
- **Why the camera does not interfere**: `PointerInputSource` now asks registered `IPointerClaimant`s at the moment a pointer goes down, *before* gesture recognition. Placement claims the pointer (`InputOwner.Placement`) when it lands on the ghost; the recogniser ignores owned pointers, so no camera drag exists for that finger. UI presses are claimed by UI first, so placement never sees them.
- **Select an existing object**: tap it while idle (ray -> ground plane -> cell -> `PlacementMap.GetAt`; no colliders or physics). Tap empty ground to deselect. A tap never starts a camera drag (the drag dead zone swallows it).
- **Controls**: Move / Rotate / Delete for a selected object; Rotate / Place / Cancel while previewing (Place is disabled while the position is invalid; the status line says why). Editor fallback keys: `1-9` start placing, `R` rotate, `Enter`/`Space` place, `Esc` cancel/deselect, `M` move, `Delete` remove. The mouse works through the same pointer path as touch. Nothing uses hover or right-click.
- Placement works while the game is paused (no simulation clock involved).

## Visuals
- Ghost = see-through copy of the prefab plus a flat footprint plate, drawn with the unlit `PlacementGhost.mat` (Sprites/Default) tinted per renderer with a `MaterialPropertyBlock`: **green = valid, red = invalid** (colours in `PlacementConfig`). Selected object = yellow plate. When moving, the real object is hidden and the ghost stands in; cancel un-hides it.
- Everything is event driven (session `Changed`); there is no per-frame placement update apart from the Editor/standalone key checks.

## Test content
`ZooGame > M2 > Create Or Update Project Assets` creates (idempotent): primitive prefabs in `Prefabs/Placeables` (a body box plus a dark "front" nub so rotation is visible), definitions in `ScriptableObjects/Placeables` – **Small Decoration 1x1**, **Food Stall 2x2**, **Utility Building 2x3** – the `PlaceableCatalog`, `PlacementConfig`, `PlacementGhost.mat`, a `Placement` object and the `Placement Panel` UI in the Zoo scene, and wires `ZooSceneBinder`.

## Tests
- EditMode (71 new): `FootprintTests` (cell counts for 1x1/2x2/2x3/3x4, rotated sizes, snapping, NaN safety), `PlacementValidatorTests` (inside/outside, straddling the edge, locked, occupied, rotation changing results, ignore-self, custom rules), `PlacementMapTests` (multi-cell occupancy and ownership, overlap refusal, release after removal, move releasing/claiming, failed move changes nothing, events), `PlacementSessionTests` (preview validity, confirm/cancel, rotation, move confirm/cancel restoring, rotate-in-place, delete, selection).
- PlayMode (`PlacementInputTests`, 11 new, simulated Touchscreen on the real Zoo scene): ghost snapped and valid, tap to position + place, ghost drag without camera movement, drag away from the ghost still pans, locked/occupied rejection, rotation on screen, select/deselect by tap, move/cancel/confirm, delete, UI button touch ownership, pinch while placing.

## Manual Editor steps
1. Run **ZooGame > M2 > Create Or Update Project Assets** (after M0 and M1 if the scenes were regenerated; M0's menu rebuilds the Zoo scene, so the order is M0, M1, M2).
2. Press Play from Bootstrap or Zoo. Click a palette button (or press 1/2/3), drag the ghost or click the ground, rotate (R), Place (Enter). Click a placed object to select it, then Move / Rotate / Delete.
3. Real-device check (not automatable here): dragging the ghost with one finger, panning with a drag that starts away from it, pinch zoom while the ghost is shown, and button taps never moving the ghost.

## Assumptions
- Prefabs are authored for the unrotated footprint, centred on the pivot at ground level, in cell units; the view root is scaled by `cellSize`. A definition without a prefab gets a plain box.
- Rotation pivots on the origin cell (min corner), not the footprint centre; this keeps rotation reversible and deterministic.
- A press within `grabMarginCells` (default 1) of the ghost grabs it, so a tap that close to the ghost does not move it – drag it, or tap farther away.
- After placing, the new object is selected so it can be moved/rotated/deleted straight away; there is no multi-placement mode.
- Placed objects have no colliders; picking is by grid cell.
- The test panel is temporary and is shown in all builds until the real build UI exists.
