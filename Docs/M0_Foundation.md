# M0 – Project Foundation

Unity 6.6 (6000.6.x), URP, Input System, uGUI. Mobile-first, landscape, iOS + Android.

## Folder structure
```
Assets/ZooGame/
  Art, Audio, Materials, Prefabs/{Animals,Buildings,Environment,Staff,Visitors,UI}
  Scenes/            Bootstrap.unity, Zoo.unity
  ScriptableObjects/ GameConfig.asset
  Scripts/
    Core/       pure C# (no UnityEngine): EventBus, GameState, GameClock, IGameContext
    Data/       ScriptableObject definitions (GameConfig)
    World/      reserved for grid/world (empty in M0)
    Platform/   runtime device settings; future platform services behind interfaces
    Input/      pointer sampling + ownership arbitration
    UI/         SafeAreaFitter, DebugHud, UI pointer blocker
    Gameplay/   GameManager, ZooSceneBinder (composition)
    Editor/     M0ProjectSetup (menu ZooGame > M0 > Create Or Update Project Assets)
    Tests/{EditMode,PlayMode}
    Animals, Visitors, Staff, Economy, Progression, Save   (placeholders, .gitkeep only)
```

## Assemblies and dependency direction
```
Core  <-  Data
Core  <-  World
Core  <-  Input  (+ Unity.InputSystem)
Core, Input (+ ugui)  <-  UI
Platform (no deps)
Gameplay -> Core, Data, World, Platform, Input, UI
Editor   -> Core, Data, Input, UI, Gameplay
Tests.EditMode -> Core, Data, Input, UI;  Tests.PlayMode -> Core, Gameplay
```
Arrows point toward the dependency (`A <- B` = B depends on A). Nothing depends on Gameplay except Editor/PlayMode tests. Future feature assemblies (Animals, Economy, ...) should reference Core/Data/World and be referenced by Gameplay. Core has `noEngineReferences`.

## Bootstrap flow
1. `Bootstrap` scene (build index 0): a black camera and a `GameManager` with a `GameConfig`.
2. `GameManager.Awake`: creates `EventBus`, `GameStateMachine` (Boot), `GameClock`; applies `RuntimeDeviceSettings` (target FPS, screen sleep); `DontDestroyOnLoad`.
3. `Start`: Boot -> Loading, async-loads `Zoo`, then Loading -> Playing.
4. `ZooSceneBinder.Awake` (Zoo): gets the context from `GameManager.Instance` once and binds the HUD and pointer UI blocker.
Pressing Play in the Zoo scene in the Editor redirects through Bootstrap.

## GameManager
Owns services, the single per-frame `Clock.Tick(unscaledDeltaTime)` (0 unless Playing), scene boot, and pause-on-background (`GameConfig.pauseWhenAppBackgrounded`). It implements `IGameContext`; other code should depend on that interface, receive it through a binder, and never poll `GameManager.Instance`. No gameplay belongs here.

## GameState
`Boot -> Loading -> Playing <-> Paused`; `Playing/Paused -> Loading` (scene reloads). Invalid transitions return false and change nothing. Changes publish `GameStateChanged(previous, current)`.

## GameClock
`SimulationSpeed`: Paused, X1, X2, X3 (multipliers from `GameConfig`, default 0/1/2/3). `Tick(realDelta)` clamps the delta (`maxRealDeltaSeconds`) then sets `DeltaTime` = real × multiplier and accumulates `ElapsedTime`. `Time.timeScale` is never changed, so UI, animation and physics are unaffected. Simulation systems read `Clock.DeltaTime`; they do not scale time themselves. Speed (clock) and GameState Paused (game) are independent: while the game is Paused the clock receives 0 delta and keeps its selected speed.

## Events
`EventBus` is an instance (on the context), typed by `struct : IGameEvent`. Publish is allocation-free. Add new events as readonly structs in the owning assembly (e.g. `MoneyChanged` in Economy, later), subscribe in `OnEnable`, unsubscribe in `OnDisable`. M0 events: `GameStateChanged`, `SimulationSpeedChanged`.

## Input foundation
- `PointerInputSource` (one per scene) is the only component polling touch (`EnhancedTouch`); mouse is polled only in Editor/Standalone as a dev convenience. It emits raw `PointerSample`s (Began/Moved/Ended/Canceled) — no gestures yet.
- `PointerOwnershipTracker`: first claim wins per pointer id, owners `Ui, Camera, World, Placement`. On Began, the source claims `Ui` if the `IPointerUiBlocker` (EventSystem) reports the pointer over UI. Systems must `TryClaim` before acting and check ownership; claims auto-release on Ended/Canceled.
- UI uses `InputSystemUIInputModule`. Legacy Input Manager is not used (project is Input System-only).
- M1 builds tap/drag/pan/pinch on `PointerChanged`.

## Mobile display assumptions
Landscape (left/right auto-rotate), no portrait. Canvas Scaler: Scale With Screen Size, 1920×1080 reference, match 0.5. HUD lives under `SafeAreaFitter` (iOS notch/home indicator, Android cutouts; Android "render outside safe area" enabled). Touch targets are ≥ 110 reference px tall. Test with the Device Simulator.

## iOS
Unity generates the Xcode project; signing, team and provisioning are set in Xcode / Unity iOS settings, not in the repo. Bundle ID currently a placeholder `com.example.mobilezoo` (Project Settings > Player > iOS > Other Settings). Min iOS 15, IL2CPP. Build requires macOS with Xcode.

## Android
Package name is the same placeholder (Player > Android > Other Settings). Min API 26, IL2CPP, ARM64. Keystore/passwords are configured per developer in Player > Publishing Settings and must not be committed. Requires the Android Build Support module (SDK/NDK/JDK).

## Build profiles
`Assets/Settings/Build Profiles/` has iOS and Android profiles (use global scene list: Bootstrap, Zoo). No gameplay code branches on platform or profile. A Development/Editor profile (Standalone or the Editor's default) is created manually, see below.

## Integrating future milestones
- Add systems as plain C# classes where possible, ticked by one owner reading `Clock.DeltaTime`; avoid per-object `Update` for populations.
- Expose configuration as ScriptableObjects in `Data` (or the feature's own assembly) and reference them from `GameConfig` or a binder.
- Wire scene objects in `ZooSceneBinder` (or a sibling binder) from `IGameContext`.
- Consume pointers via `PointerInputSource`, always through ownership claims.
- Cross-system communication goes through the `EventBus`.

## Manual Editor steps
- Open the project in Unity 6.6; if scenes are missing run ZooGame > M0 > Create Or Update Project Assets (it overwrites the two M0 scenes).
- Replace placeholder bundle/package IDs; set iOS signing team and Android keystore locally.
- Create a Development build profile: File > Build Profiles > Add Build Profile (platform: your desktop/current platform), name it "Development", tick Development Build.
- Install iOS/Android build support modules via Unity Hub.
- Run PlayMode tests from Window > General > Test Runner.
