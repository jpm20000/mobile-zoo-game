# M1 – Zoo Grid, World and Mobile Camera

Builds on [M0](M0_Foundation.md). No buildings, placement, paths, enclosures, animals, visitors or economy exist yet; the grid only *represents* path/enclosure/occupancy so those milestones have somewhere to write.

## What was added
```
Scripts/World/    GridTypes.cs (GridCoord, GridCell, TerrainType, CellFlags, GridSettings)
                  ZooGrid.cs (authoritative grid)  TerrainView.cs  GridDebugOverlay.cs
Scripts/Data/     WorldConfig.cs   CameraConfig.cs           (ScriptableObjects)
Scripts/Input/    PointerGestureRecognizer.cs (tap / drag / pinch); PointerInputSource gains Gestures + Scrolled
Scripts/Cameras/  ZooCameraController.cs  CameraMath.cs      (new assembly ZooGame.Cameras)
Scripts/Editor/   M1ProjectSetup.cs   (menu ZooGame > M1 > Create Or Update Project Assets)
```
New assembly `ZooGame.Cameras` references Core, Data, World, Input. `Data` now references `World` (config → layout types). Dependency direction is unchanged otherwise.

## The grid (`ZooGrid`)
- 64 × 64 cells on the world X/Z plane; one cell = 1 world unit (`WorldConfig.cellSize`). The grid's minimum corner is at `WorldConfig.origin` (default world 0,0,0), so the map spans world X/Z 0–64 and its centre is (32, 0, 32).
- **No GameObject or MonoBehaviour per cell.** Data lives in three flat arrays indexed `z * Width + x`: a flags byte (`Unlocked`, `Occupied`, `HasPath`), a terrain byte, and a `ushort` enclosure id (`0` = none). A cell is read as a by-value `GridCell` snapshot (no allocation).
- Initial unlocked area: **32 × 32, centred** (cells 16–47 on both axes, 1024 cells). Everything else is locked land.
- Conversions are deterministic and use `floor`: cell *(x, z)* covers `[origin + x·size, origin + (x+1)·size)`, so a point exactly on a shared edge belongs to the higher-index cell, and the far map edge (world 64) is outside. NaN, ±Infinity and huge values are clamped and always land *outside* the grid.

| API | Behaviour |
|---|---|
| `WorldToGrid(Vector3)` | Cell containing the point (Y ignored); may be outside the grid |
| `TryWorldToGrid(Vector3, out GridCoord)` | False if outside |
| `GridToWorld(GridCoord)` | Cell **centre** at ground height (`origin.y`) |
| `GridToWorldCorner(GridCoord)` | Cell minimum corner |
| `IsInsideGrid(GridCoord)` / `(x, z)` | Bounds check |
| `GetCell(GridCoord)` | `GridCell`; `IsValid == false` when outside, never throws |
| `TryGetCell(GridCoord, out GridCell)` | False when outside |
| `IsUnlocked(GridCoord)` | Inside **and** unlocked |
| `IsAvailable(GridCoord)` | Inside, unlocked and **not occupied** (path / enclosure membership are separate facts and do not change availability — later milestones decide what may be built on a path) |
| `SetUnlocked / UnlockRect / SetOccupied / SetPath / SetTerrain / SetEnclosure` | Return false for invalid cells; `Changed` fires only on real change (once per `UnlockRect`) |

`ZooSceneBinder.Grid` exposes the instance to scene systems. Later systems receive it from the binder; do not search for it.

## World representation
- `TerrainView`: one quad covering the grid with a point-filtered texture of one texel per cell (terrain colour, tinted when locked). It subscribes to `ZooGrid.Changed` and re-uploads once per frame at most (`LateUpdate`, then the component disables itself until the next change). Uses `MaterialPropertyBlock`, so the shared `Ground.mat` is never instanced.
- `GridDebugOverlay`: every cell boundary as GPU line segments in a single static mesh (one draw call, ~260 vertices). Every 8th line is brighter and the map border is yellow. Visible on start in the Editor and Development builds only (`WorldConfig.showGridOverlay`); the HUD **Grid** button toggles it. Hidden = renderer disabled, no cost. Uses `Sprites/Default` (unlit, vertex colour, alpha) in `GridOverlay.mat`.
- Orientation: terrain texel (x, z) ↔ cell (x, z) ↔ world X/Z; the overlay and terrain are built from the same `WorldMin/WorldMax`, so they align by construction.

## Input
`PointerInputSource` remains the single place that polls devices and now also owns `Gestures`, a `PointerGestureRecognizer` (pure C#, allocation-free, time injected for tests).

| Gesture | Events | Notes |
|---|---|---|
| Tap | `Tapped(id, pos)` | Released within `maxTapSeconds`, never moved past the drag threshold |
| Drag | `DragStarted / DragMoved / DragEnded` | Starts after moving `dragThresholdDp` (density-independent, converted with `Screen.dpi`, 160 if unknown). The dead zone is swallowed so the view does not jump |
| Pinch | `PinchStarted / PinchChanged(centre, ratio) / PinchEnded` | A second finger converts a press/drag into a pinch (the drag is ended first). After a pinch, the remaining finger is suppressed until it lifts, so zoom never jumps into pan. Max two pointers tracked |
| UI | — | A pointer that begins over uGUI is claimed `InputOwner.Ui` by the source before the recognizer runs; the recognizer ignores owned pointers, so **UI touches produce no gestures at all** |

Editor/desktop only: mouse left-drag = drag, wheel = `PointerInputSource.Scrolled` (ignored over UI). The mouse path is compiled out of device builds.

**Sharing rule for later systems (selection, placement):** gesture events do not imply ownership. In a `*Started` handler call `Ownership.TryClaim(pointerId, InputOwner.X)` and ignore the gesture if it returns false; release in the matching `*Ended`. First claim wins, claims are released automatically when the pointer ends. `PointerInputSource` runs at execution order −100, after `EventSystem` (−1000), so UI hit-testing already includes this frame's touches when ownership is decided.

## Camera (`ZooCameraController`)
- Orthographic, fixed pitch/yaw (30° / 45°), configured in `CameraConfig`; rotation never changes at runtime. Position is derived from a ground **focus point**: `position = focus − forward × 100`.
- **Pan**: one-finger drag. The ground follows the finger exactly at `panSpeed = 1` (vertical drag is divided by `sin(pitch)`, so the ground, not the screen plane, tracks the finger).
- **Zoom**: pinch (`pinchZoomSpeed` exponent on the finger-distance ratio) or mouse wheel (`mouseWheelZoomSpeed`). Zoom is the orthographic size, clamped to `[minZoom, maxZoom]` (default 5–20, start 10). The ground point under the pinch centre / cursor stays fixed.
- **Smoothing**: exponential, frame-rate independent, time constant `smoothingSeconds` (0 = instant), on unscaled time, so the camera still works while the game is paused. Idle frames cost one bool check.
- **Bounds**: the focus is clamped to the whole map (locked land included) expanded by `boundsMargin` (default 4 units). Targets are clamped, so over-dragging at an edge does not build up hidden slack.
- All tuning (pan speed, zoom speeds, min/max zoom, smoothing, margin, drag threshold, tap time) is in `CameraConfig`; grid size/unlocked area/colours are in `WorldConfig`.

## Tests
- EditMode (47 new): `ZooGridTests` (conversion both ways, round trips, boundaries, NaN/∞, locked/unlocked, availability, safe invalid access, change events), `PointerGestureRecognizerTests` (tap vs drag vs pinch, UI exclusion, cancel, third finger), `CameraMathTests` (pan follows finger, zoom anchor, clamps, smoothing).
- PlayMode (`ZooCameraInputTests`): simulated Touchscreen/Mouse devices drive the real Zoo scene — pan, tap, pinch with limits, bounds, settling, mouse drag/wheel, and UI touch/click blocking.

## Manual Editor steps
1. Open the project in Unity 6.6. If scenes are missing run **ZooGame > M0 > Create Or Update Project Assets**, then **ZooGame > M1 > Create Or Update Project Assets** (M0's menu rebuilds the Zoo scene without M1 content; always run M1 after it).
2. Press Play from the Zoo or Bootstrap scene. Drag with the mouse to pan, scroll to zoom; use the HUD **Grid** button to toggle the overlay. Check Device Simulator for notch/safe-area layouts.
3. Tune `Assets/ZooGame/ScriptableObjects/CameraConfig.asset` and `WorldConfig.asset`.
4. Real-device check (cannot be automated here): one-finger pan, two-finger pinch, and that touches on the HUD buttons never move the camera.

## Assumptions
- The unlocked 32×32 block is centred; change `unlockedWidth/Depth` or add a future unlock rule in `GridSettings`.
- `IsAvailable` ignores path/enclosure state (see above).
- The whole 64×64 map is visible/pannable; locked land is shown darker rather than hidden.
- The camera bounds clamp the view centre, not the view edges, so at maximum zoom-out the edge of the map can leave the screen by up to the margin plus half the view.
