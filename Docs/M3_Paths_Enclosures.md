# M3 – Paths & Enclosure Construction

Builds on [M2](M2_Building_Placement.md). No animals, visitors, staff, economy deductions, quests, research or breeding exist. `cost` fields are stored but unused, and gates only *mark* where access will go (`EdgeKind.IsPassable`); nothing navigates.

## What was added
```
Scripts/World/        Edges.cs               Direction4, EdgeCoord, EdgeKind, FenceEdge, EdgeMath (snap/runs/routing)
                      FenceMap.cs            fences + gates as cell-edge data
                      PathConnectivity.cs    neighbour mask, PathShape, rotation
                      ConstructionValidation.cs   IPathRule / IFenceRule, validators, BuildResult
                      EnclosureMap.cs        closed-region detection, incremental
                      ConstructionModel.cs   the one entry point: paths, fences, enclosures, events
                      GroundProbe.cs         screen -> ground point (shared by all tools)
                      ZooGrid.PathsChanged   new event
Scripts/Data/         PathDefinition, FenceDefinition, ConstructionCatalog, ConstructionConfig   (ScriptableObjects)
Scripts/Input/        BuildTools.cs          BuildMode + IBuildTool;  InputOwner.Construction
Scripts/Construction/ (new assembly ZooGame.Construction)
                      BuildModeController    single input front door + mode switching
                      PathTool, FenceTool, DemolishTool, BuildContext
                      PathRenderer, FenceRenderer, ConstructionPreview, ColoredMeshBuilder
Scripts/Gameplay/     ConstructionTestPanel  temporary toolbar
Scripts/Editor/       M3ProjectSetup         menu ZooGame > M3 > Create Or Update Project Assets
```
**Changed**: `PlacementController` is now one of the build tools (it no longer subscribes to input itself); `PlacementValidator` gained `NoPathUnderObjectRule`; `ZooSceneBinder` wires construction.

## Path representation
- A path cell is the existing `CellFlags.HasPath` bit in `ZooGrid`. There is no object per cell and no separate path store. The grid already exposes occupancy (`IsOccupied`), path (`HasPath`) and enclosure id (`EnclosureId`) per cell.
- Cells do not record a path *type* yet; with one placeholder type every path draws with the catalog's first `PathDefinition`. A per-cell type byte is the natural extension.
- Validation (`PathValidator`, ordered `IPathRule`s): inside the grid, unlocked, not occupied by an object. New rules (terrain, slope, prerequisites) are added with `PathRules.AddRule` and the tool does not change. In the other direction, objects cannot be placed on a path cell.
- `ConstructionModel.RemovePath` clears only the path bit: occupancy, enclosure ids and fences are untouched.

## Path connection logic
Connectivity is **derived, never stored**, so it cannot go stale: `PathConnectivity.GetMask` checks the four orthogonal neighbours' path bits (N=1, E=2, S=4, W=8; diagonals never connect). `Classify(mask)` gives a `PathShape` (Isolated, DeadEnd, Straight, Corner, TJunction, Cross) and a quarter-turn `Rotation` from the shape's canonical orientation, for future art authored per shape. `PathRenderer` draws a centre tile plus an arm toward each connected neighbour, so every shape (and the update when a neighbour is added or removed) falls out of the mask. It is one mesh, rebuilt in `LateUpdate` at most once per frame and only after `ZooGrid.PathsChanged`.

## Fence edge representation
- `FenceMap` stores two flat byte arrays: edges running along X (between a cell and its north/south neighbour) and along Z (east/west). Each edge has one canonical `EdgeCoord(axis, x, z)`, so `EdgeCoord.OfCell(c, North)` equals `OfCell(c + north, South)`: a shared edge is one segment with one owner, and duplicates are impossible. Border edges are valid. Fences do **not** mark cells occupied.
- Each edge holds `EdgeKind`: None, Fence or Gate. Setting the same kind again is a no-op with no event; a gate over a fence replaces it.
- Validation (`FenceValidator`, ordered `IFenceRule`s): valid edge, and at least one adjacent cell unlocked (so the border of the unlocked area can be fenced, but locked land cannot).
- `FenceRenderer` draws one mesh: rails are lengthened by half their thickness so corners and junctions join, gates are shorter and a different colour with taller posts, and fences not in a closed enclosure are tinted ("open layout").

## Enclosure detection
- `EnclosureMap.Recalculate(changedEdges)` runs when a batch is **committed** (`ConstructionModel.BuildFences/RemoveFences`), never per touch movement. It takes the cells beside the changed edges as flood-fill seeds, floods across any edge without a fence or gate (stamp array, no per-call allocation of the map), and treats a region that reaches the grid edge as **open**. The grid border is not a wall. Only a closed region becomes an `Enclosure`: id, cells, boundary edges (interior walls excluded), gates, `Area`.
- Only the enclosures the seeds already belong to are dissolved and rebuilt; others are untouched (same object).
- **Ids**: a rebuilt region keeps the id of the old enclosure it overlaps most (adding a gate, enlarging, or the larger half of a split keeps its id; a merge continues one of the old ids). Genuinely new regions get a fresh id. Ids are never reused after an enclosure disappears. Interior cells carry the id in `ZooGrid` (`EnclosureId`), cleared when it is dissolved.
- Open vs closed: `IsClosedBoundary(edge)` tells whether a fence belongs to a closed boundary; stray or incomplete fences belong to none and create no enclosure.

## Gate handling
A gate occupies an edge, replaces a fence there, counts as boundary (so a gated pen is still closed), is listed in `Enclosure.Gates` and has its own colour/shape and `FenceDefinition`. `EdgeKind.IsPassable` (gate = true, fence = false) is the hook for future keeper/visitor access logic.

## Build-mode architecture and input
- `BuildMode`: None, ObjectPlacement, PathPlacement, FencePlacement, Demolition. `IBuildTool` (Enter/Exit/WantsPointer/OnPointer/OnTapped/Confirm/Cancel) is implemented by `PlacementController`, `PathTool`, `FenceTool`, `DemolishTool`. A future construction system adds a tool and a mode value.
- `BuildModeController` is the only component that registers a pointer claimant and the tap subscription. UI-owned touches never arrive; a claimed finger never pans the camera; only one mode is active. Starting object placement (palette, keys) drops any construction preview and vice versa.
- **Path**: press a cell and drag; cells are joined by orthogonal routes, shown green (valid) or red (rejected); Confirm builds the valid ones. **Fence/Gate**: press near a corner, drag, release adds a straight run (dominant axis); several runs accumulate (corners = consecutive runs); a tap without dragging picks the nearest single edge. **Demolish**: touch or drag over paths, or near a fence line, to mark them (orange); Confirm removes. Cancel discards the preview; Cancel with nothing pending leaves the mode.
- Previews are one mesh (`ConstructionPreview`) and never touch grid or fence data.
- **Camera**: with a construction tool active every world press draws, so the camera cannot pan from a press. The toolbar's **Pan** toggle hands one-finger drag and pinch back to the camera while keeping the tool. Pinch zoom works in Pan mode (and during object placement as in M2).
- Editor keys: `P` path, `F` fence, `G` gate, `X` demolish, `Enter`/`Space` confirm, `Esc` cancel/leave mode (object-placement keys unchanged).

## Test content
`ZooGame > M3 > Create Or Update Project Assets` (idempotent) creates Basic Path, Basic Fence and Basic Gate definitions, the catalog, `ConstructionConfig`, `ConstructionVertexColor.mat` (Sprites/Default), the Paths/Fences/Preview objects, the Build Mode object and the toolbar, and wires the binder.

## Tests
- EditMode (80 new): `PathTests` (build/remove, locked/out-of-bounds/occupied rejection, custom rules, neighbour masks, straight/corner/T/cross/dead-end, rotations, neighbour updates on add/remove, routing), `FenceTests` (shared-edge identity, no duplicates, removal, border edges, gates, locked/bounds/custom rules, runs and snapping), `EnclosureTests` (closed rectangle, open gap, interior cells, area, ids on the grid, gates, remove/rebuild, split, merge, nested, border-closed, id stability, local recalculation, events), `ConstructionInteropTests` (M2 objects vs paths/fences).
- PlayMode (`ConstructionInputTests`, 16 new, simulated touch): path drag preview/confirm/cancel, locked cells, Pan mode, fence rectangle -> enclosure, open layout, gate tap, locked fence, demolish tap and drag, mode switching both ways, object placement afterwards, UI touch isolation, pinch in Pan mode. M2 placement tests run unchanged through the new controller.

## Manual Editor steps
1. Run **ZooGame > M3 > Create Or Update Project Assets** (order after M0 recreates the scene: M0, M1, M2, M3).
2. Play. Right-hand toolbar: Basic Path, Basic Fence, Basic Gate, Demolish, Pan. Drag in the world, Confirm. Fence a rectangle with four drags, Confirm, and watch the status line count the enclosure; remove one fence with Demolish and it drops to 0.
3. Real-device check (not automatable here): drag-drawing with one finger, the Pan toggle with pinch, and toolbar taps never drawing.

## Assumptions
- Fences are allowed on any edge touching at least one unlocked cell; an enclosure's cells may in principle include locked cells if the boundary is fully fenced.
- Fences and paths may overlap on the map (a path crossing a fence); a rule can forbid it later.
- Paths and fences cannot be removed by the object Delete control; Demolish handles them. Demolish does not remove placed objects.
- Confirm builds the valid items and skips rejected ones (shown red in the preview).
- Enclosure ids are `ushort`-bounded by the grid (65,535).
- The construction toolbar is temporary and shown in all builds.
