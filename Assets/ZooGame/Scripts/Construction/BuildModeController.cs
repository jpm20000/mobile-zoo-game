using System;
using UnityEngine;
using UnityEngine.InputSystem;
using ZooGame.Data;
using ZooGame.Input;
using ZooGame.Placement;
using ZooGame.World;

namespace ZooGame.Construction
{
    /// <summary>
    /// The single input front door for world construction. It owns the pointer claim and the tap subscription and
    /// routes them to the active <see cref="IBuildTool"/> (object placement, path, fence, demolition), so every mode
    /// shares the same rules: UI-owned touches never arrive, a claimed finger never pans the camera, and only one mode
    /// is active at a time. A new construction system adds an <see cref="IBuildTool"/> and a <see cref="BuildMode"/>,
    /// not another input path.
    ///
    /// Camera: while a path/fence/demolish tool is active a one-finger press draws. Two fingers always move and zoom
    /// the camera: a second finger aborts the stroke (see <see cref="StrokeToolBase.AbortStroke"/>) and both fingers
    /// go to the camera's pinch/pan. Turn <see cref="PanMode"/> on to give one-finger drag to the camera as well (the
    /// tool is kept). Object placement keeps its own finer rule (only a press on the ghost is claimed).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BuildModeController : MonoBehaviour, IPointerClaimant
    {
        PointerInputSource _input;
        PlacementController _placement;
        ConstructionCatalog _catalog;
        PathTool _path;
        FenceTool _fence;
        DemolishTool _demolish;
        readonly System.Collections.Generic.List<IBuildTool> _extraTools = new System.Collections.Generic.List<IBuildTool>(2);
        IBuildTool _active;
        BuildMode _mode;
        bool _panMode;
        bool _subscribed;
        Vector2 _strokeScreen; // last screen position of the pointer a stroke tool is following

        public BuildMode Mode => _mode;
        public IBuildTool Active => _active;
        public PathTool Path => _path;
        public FenceTool Fence => _fence;
        public DemolishTool Demolish => _demolish;
        public ConstructionCatalog Catalog => _catalog;
        public bool IsBound => _placement != null;

        /// <summary>When true the camera pans and zooms freely even though a construction tool is selected.</summary>
        public bool PanMode
        {
            get => _panMode;
            set
            {
                if (_panMode == value) return;
                _panMode = value;
                Changed?.Invoke();
            }
        }

        /// <summary>Raised when the mode, tool selection, pan toggle or pending preview changes.</summary>
        public event Action Changed;

        InputOwner IPointerClaimant.Owner => _active.Owner;

        public void Bind(PointerInputSource input, BuildContext context, PlacementController placement, ConstructionCatalog catalog)
        {
            Unbind();
            _input = input;
            _placement = placement;
            _catalog = catalog;
            _path = new PathTool(context) { Definition = catalog.DefaultPath };
            _fence = new FenceTool(context) { Definition = catalog.FirstOfKind(EdgeKind.Fence) };
            _demolish = new DemolishTool(context);
            _active = _placement;
            _mode = BuildMode.None;
            _placement.Session.Changed += OnPlacementChanged;
            if (isActiveAndEnabled) Subscribe();
        }

        void Unbind()
        {
            if (_placement == null) return;
            Unsubscribe();
            _placement.Session.Changed -= OnPlacementChanged;
            _placement = null;
        }

        void OnEnable() => Subscribe();
        void OnDisable() => Unsubscribe();
        void OnDestroy() => Unbind();

        void Subscribe()
        {
            if (_input == null || _subscribed) return;
            _subscribed = true;
            _input.AddClaimant(this);
            _input.PointerChanged += OnPointerChanged;
            _input.Gestures.Tapped += OnTapped;
        }

        void Unsubscribe()
        {
            if (_input == null || !_subscribed) return;
            _subscribed = false;
            _input.RemoveClaimant(this);
            _input.PointerChanged -= OnPointerChanged;
            _input.Gestures.Tapped -= OnTapped;
        }

        // ---- Mode control ----------------------------------------------------------------------------------

        public void EnterPath(PathDefinition definition = null)
        {
            if (definition != null) _path.Definition = definition;
            SwitchTo(BuildMode.PathPlacement);
        }

        public void EnterFence(FenceDefinition definition = null)
        {
            if (definition != null) _fence.Definition = definition;
            SwitchTo(BuildMode.FencePlacement);
        }

        public void EnterDemolish() => SwitchTo(BuildMode.Demolition);

        /// <summary>Leaves any construction mode (and cancels object placement). Pending previews are discarded.</summary>
        public void ExitMode() => SwitchTo(BuildMode.None);

        /// <summary>Builds the pending preview of the active tool.</summary>
        public bool Confirm()
        {
            bool ok = _active.Confirm();
            if (!LeaveIfFinished()) Changed?.Invoke();
            return ok;
        }

        /// <summary>Registers an extra tool (for example animal placement) so it can be entered by its <see cref="BuildMode"/>.</summary>
        public void RegisterTool(IBuildTool tool)
        {
            if (tool != null && !_extraTools.Contains(tool)) _extraTools.Add(tool);
        }

        /// <summary>Enters a registered mode. The tool is prepared by its owner first (for example by choosing what to place).</summary>
        public void Enter(BuildMode mode) => SwitchTo(mode);

        // A registered tool is a one-shot like object placement: once nothing is pending (placed or cancelled), the mode ends.
        bool LeaveIfFinished()
        {
            if (_mode != BuildMode.AnimalPlacement || _active.HasPending) return false;
            SwitchTo(BuildMode.None);
            return true;
        }

        /// <summary>Discards the pending preview; with nothing pending, leaves a construction mode.</summary>
        public void Cancel()
        {
            if (_active.HasPending) _active.Cancel();
            else if (IsConstructionMode(_mode)) { SwitchTo(BuildMode.None); return; }
            if (!LeaveIfFinished()) Changed?.Invoke();
        }

        static bool IsConstructionMode(BuildMode m) =>
            m == BuildMode.PathPlacement || m == BuildMode.FencePlacement || m == BuildMode.Demolition;

        void SwitchTo(BuildMode mode)
        {
            if (!IsBound) return;
            var next = ToolFor(mode);
            if (mode == _mode && ReferenceEquals(next, _active) && mode != BuildMode.None)
            {
                Changed?.Invoke(); // same tool, e.g. switching fence definition
                return;
            }

            _active.Exit();
            // Entering a construction mode also drops any object selection/placement.
            if (!ReferenceEquals(_active, _placement)) _placement.Exit();
            _active = next;
            _mode = mode;
            if (mode == BuildMode.None || mode == BuildMode.ObjectPlacement) _active = _placement;
            _active.Enter();
            Changed?.Invoke();
        }

        IBuildTool ToolFor(BuildMode mode)
        {
            switch (mode)
            {
                case BuildMode.PathPlacement: return _path;
                case BuildMode.FencePlacement: return _fence;
                case BuildMode.Demolition: return _demolish;
                default:
                    for (int i = 0; i < _extraTools.Count; i++)
                        if (_extraTools[i].Mode == mode) return _extraTools[i];
                    return _placement;
            }
        }

        /// <summary>Object placement can begin from anywhere (palette, shortcut keys); keep the mode in step with it.</summary>
        void OnPlacementChanged()
        {
            bool placing = _placement.Session.IsPlacing;
            if (placing && _mode != BuildMode.ObjectPlacement)
            {
                if (!ReferenceEquals(_active, _placement)) _active.Exit();
                _active = _placement;
                _mode = BuildMode.ObjectPlacement;
                Changed?.Invoke();
            }
            else if (!placing && _mode == BuildMode.ObjectPlacement)
            {
                _mode = BuildMode.None;
                Changed?.Invoke();
            }
        }

        // ---- Input routing ---------------------------------------------------------------------------------

        bool IPointerClaimant.WantsPointer(in PointerSample s)
        {
            if (!IsBound) return false;
            if (_panMode && _active is StrokeToolBase) return false;

            // Two fingers always move/zoom the camera: a second finger turns a stroke in progress into a camera gesture.
            if (_active is StrokeToolBase stroke)
            {
                if (_input.Gestures.IsPinching) return false; // a third finger never starts drawing mid-gesture
                if (stroke.IsStroking && s.PointerId != stroke.StrokePointer)
                {
                    int first = stroke.StrokePointer;
                    stroke.AbortStroke();
                    _input.ReturnToGestures(first, _strokeScreen);
                    Changed?.Invoke();
                    return false;
                }
            }
            bool wants = _active.WantsPointer(s);
            if (wants && !ReferenceEquals(_active, _placement)) Changed?.Invoke();
            return wants;
        }

        void OnPointerChanged(PointerSample s)
        {
            if (!IsBound || _input.Ownership.GetOwner(s.PointerId) != _active.Owner) return;
            _strokeScreen = s.ScreenPosition;
            _active.OnPointer(s);
            if (s.Phase != PointerPhase.Moved) Changed?.Invoke();
        }

        void OnTapped(int pointerId, Vector2 position)
        {
            if (IsBound) _active.OnTapped(pointerId, position);
        }

#if UNITY_EDITOR || UNITY_STANDALONE
        // Editor/desktop shortcuts: P path, F fence, G gate, X demolish, Enter confirm, Esc cancel / leave mode.
        // (Object placement keys live on PlacementController; Enter and Esc are harmless there in these modes.)
        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || !IsBound) return;
            if (kb.pKey.wasPressedThisFrame) EnterPath();
            if (kb.fKey.wasPressedThisFrame) EnterFence(_catalog.FirstOfKind(EdgeKind.Fence));
            if (kb.gKey.wasPressedThisFrame) EnterFence(_catalog.FirstOfKind(EdgeKind.Gate));
            if (kb.xKey.wasPressedThisFrame) EnterDemolish();
            if (!IsConstructionMode(_mode)) return;
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) Confirm();
            if (kb.escapeKey.wasPressedThisFrame) Cancel();
        }
#endif
    }
}
