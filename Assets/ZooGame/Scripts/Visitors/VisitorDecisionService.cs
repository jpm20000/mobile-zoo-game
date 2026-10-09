using System;
using System.Collections.Generic;
using UnityEngine;
using ZooGame.World;

namespace ZooGame.Visitors
{
    /// <summary>
    /// What a visitor's body can do: walk a list of cells, or stop. Implemented by the scene controller; tests use a
    /// fake. The decision service never touches a Transform.
    /// </summary>
    public interface IVisitorAgent
    {
        VisitorInstance Instance { get; }

        /// <summary>Walk through these cells in order (they are copied). The agent reports arrival by calling <see cref="VisitorDecisionService.OnArrived"/>.</summary>
        void Follow(IReadOnlyList<GridCoord> route);

        void Stop();
    }

    /// <summary>
    /// The only place a visitor's <see cref="VisitorState"/> changes. It chooses what to do next (leave, satisfy the most
    /// urgent need with a reachable facility, or visit a reachable enclosure), asks the navigation service for a route
    /// only when the destination changes, applies the result of viewing / facility use / resting, and handles routes
    /// that stop being valid. It does not tick needs (that is the simulation service) and does not move anything.
    ///
    /// Flow: Entering, then ChoosingDestination; <see cref="Decide"/> picks Exiting, SeekingFood / SeekingDrink /
    /// SeekingToilet (or Walking to a bench), or Walking to an enclosure; arrival moves to ViewingAnimal / Resting or
    /// applies the facility and decides again; leaving ends at the exit (or at once if the exit is unreachable).
    /// </summary>
    public sealed class VisitorDecisionService : IDisposable
    {
        const int MaxDecisionDepth = 4;

        readonly VisitorConfig _config;
        readonly IVisitorRegistry _registry;
        readonly VisitorNavigationService _navigation;
        readonly VisitorDestinationService _destinations;
        readonly System.Random _rng;
        readonly Dictionary<string, IVisitorAgent> _agents = new Dictionary<string, IVisitorAgent>();

        // Scratch (reused, so decisions allocate nothing in steady state).
        readonly List<GridCoord> _route = new List<GridCoord>(64);
        readonly List<VisitorDestination> _candidates = new List<VisitorDestination>(16);
        readonly List<int> _enclosureIds = new List<int>(8);
        readonly List<float> _weights = new List<float>(8);
        readonly List<VisitorInstance> _snapshot = new List<VisitorInstance>(32);

        bool _routesDirty;
        int _depth;

        public VisitorDecisionService(VisitorConfig config, IVisitorRegistry registry, VisitorNavigationService navigation,
            VisitorDestinationService destinations, System.Random rng = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _destinations = destinations ?? throw new ArgumentNullException(nameof(destinations));
            _rng = rng ?? new System.Random();
            _navigation.GraphChanged += MarkRoutesDirty;
            _destinations.Changed += MarkRoutesDirty;
        }

        /// <summary>Raised after a visitor's state changed.</summary>
        public event Action<VisitorInstance> StateChanged;

        /// <summary>Raised when a visitor has finished leaving; the owner despawns and unregisters them.</summary>
        public event Action<VisitorInstance> Left;

        /// <summary>Visitors who left without reaching an exit (it was unreachable); diagnostics and tests.</summary>
        public int ForcedLeaveCount { get; private set; }

        public VisitorConfig Config => _config;

        public void Dispose()
        {
            _navigation.GraphChanged -= MarkRoutesDirty;
            _destinations.Changed -= MarkRoutesDirty;
        }

        void MarkRoutesDirty() => _routesDirty = true;

        // ---- Agents ----------------------------------------------------------------------------------------

        public void Attach(IVisitorAgent agent)
        {
            if (agent?.Instance != null) _agents[agent.Instance.VisitorId] = agent;
        }

        public void Detach(string visitorId)
        {
            if (visitorId != null) _agents.Remove(visitorId);
        }

        IVisitorAgent AgentOf(VisitorInstance v) => _agents.TryGetValue(v.VisitorId, out var a) ? a : null;

        GridCoord CellOf(VisitorInstance v) => _navigation.Grid.WorldToGrid(v.Position);

        // ---- Entering --------------------------------------------------------------------------------------

        /// <summary>
        /// Starts a new visitor at the entrance: Entering, walking onto the entrance's path cell. Returns false (and
        /// changes nothing) when the entrance has no path to step onto.
        /// </summary>
        public bool BeginEntering(VisitorInstance v, VisitorDestination entrance)
        {
            if (v == null || entrance == null || !entrance.IsPathAccessible) return false;
            v.State = VisitorState.Entering;
            v.TargetDestinationId = entrance.Id;
            _route.Clear();
            _route.Add(entrance.InteractionPoint);
            AgentOf(v)?.Follow(_route);
            StateChanged?.Invoke(v);
            return true;
        }

        // ---- Arrival ---------------------------------------------------------------------------------------

        /// <summary>The visitor reached the end of its route.</summary>
        public void OnArrived(VisitorInstance v)
        {
            if (v == null || !_registry.Contains(v.VisitorId)) return;
            _destinations.TryGet(v.TargetDestinationId, out var dest);

            switch (v.State)
            {
                case VisitorState.Entering:
                    Decide(v);
                    break;
                case VisitorState.Walking:
                    if (dest == null || !dest.Enabled) Decide(v);
                    else if (dest.Kind == VisitorDestinationKind.EnclosureViewing) BeginViewing(v, dest);
                    else if (dest.Kind == VisitorDestinationKind.Bench) BeginResting(v);
                    else Decide(v);
                    break;
                case VisitorState.SeekingFood:
                    UseFacility(v, dest, VisitorDestinationKind.FoodStall);
                    break;
                case VisitorState.SeekingDrink:
                    UseFacility(v, dest, VisitorDestinationKind.DrinkStall);
                    break;
                case VisitorState.SeekingToilet:
                    UseFacility(v, dest, VisitorDestinationKind.Toilet);
                    break;
                case VisitorState.Exiting:
                    Leave(v);
                    break;
                default:
                    break; // viewing / resting / choosing: an arrival is not expected
            }
        }

        // ---- Scheduled review (called by the simulation service each tick) ---------------------------------

        /// <summary>
        /// Called once per visitor per simulation tick, after needs and happiness were updated. Finishes viewing /
        /// resting when their time is up, makes a leaving visitor head for the exit, and lets an urgent need interrupt a
        /// walk to an attraction (only when a facility for it is reachable, so a visitor never dithers).
        /// </summary>
        public void OnTick(VisitorInstance v, float simulatedMinutes, VisitorNeed urgentNeed, bool exitRequired)
        {
            if (v == null || v.State == VisitorState.Exiting) return;
            if (exitRequired)
            {
                GoExit(v);
                return;
            }

            switch (v.State)
            {
                case VisitorState.ViewingAnimal:
                    v.ActivityTimeLeft -= simulatedMinutes;
                    if (v.ActivityTimeLeft <= 0f) CompleteViewing(v);
                    break;
                case VisitorState.Resting:
                    v.ActivityTimeLeft -= simulatedMinutes;
                    if (v.ActivityTimeLeft <= 0f) CompleteResting(v);
                    break;
                case VisitorState.Walking:
                    if (urgentNeed != VisitorNeed.None && WalkingToAttraction(v) && HasReachableFacilityForUrgentNeed(v)) Decide(v);
                    break;
                case VisitorState.ChoosingDestination:
                    Decide(v);
                    break;
                default:
                    break;
            }
        }

        bool WalkingToAttraction(VisitorInstance v) =>
            !_destinations.TryGet(v.TargetDestinationId, out var d) || d.Kind == VisitorDestinationKind.EnclosureViewing;

        bool HasReachableFacilityForUrgentNeed(VisitorInstance v)
        {
            var from = CellOf(v);
            int excluded = 0;
            VisitorNeed need;
            while ((need = VisitorMath.MostUrgent(v, _config, excluded)) != VisitorNeed.None)
            {
                _candidates.Clear();
                if (_destinations.GetReachable(KindFor(need), from, _candidates) > 0) return true;
                excluded |= VisitorMath.Mask(need);
            }
            return false;
        }

        static VisitorDestinationKind KindFor(VisitorNeed need)
        {
            switch (need)
            {
                case VisitorNeed.Food: return VisitorDestinationKind.FoodStall;
                case VisitorNeed.Drink: return VisitorDestinationKind.DrinkStall;
                case VisitorNeed.Toilet: return VisitorDestinationKind.Toilet;
                default: return VisitorDestinationKind.Bench;
            }
        }

        // ---- Deciding --------------------------------------------------------------------------------------

        /// <summary>
        /// Chooses the visitor's next activity: leave if due; else the most urgent need that has a reachable facility;
        /// else a reachable enclosure, weighted by appeal; else leave (nothing meaningful is reachable).
        /// </summary>
        public void Decide(VisitorInstance v)
        {
            if (v == null) return;
            if (_depth >= MaxDecisionDepth)
            {
                // A pathological config (a facility that never satisfies its need) must not recurse forever; the next tick decides again.
                AgentOf(v)?.Stop();
                SetState(v, VisitorState.ChoosingDestination);
                return;
            }
            _depth++;
            try
            {
                AgentOf(v)?.Stop();
                v.TargetDestinationId = null;
                v.CurrentEnclosureId = VisitorInstance.NoEnclosure;
                v.ActivityTimeLeft = 0f;
                SetState(v, VisitorState.ChoosingDestination);

                if (VisitorMath.ShouldExit(v, _config)) { GoExit(v); return; }

                var from = CellOf(v);
                int excluded = 0;
                VisitorNeed need;
                while ((need = VisitorMath.MostUrgent(v, _config, excluded)) != VisitorNeed.None)
                {
                    if (TryGoToFacility(v, need, from)) return;
                    excluded |= VisitorMath.Mask(need); // nothing reachable for it: carry on as normal
                }

                if (TryGoToAttraction(v, from)) return;
                GoExit(v); // no meaningful reachable destination
            }
            finally
            {
                _depth--;
            }
        }

        bool TryGoToFacility(VisitorInstance v, VisitorNeed need, GridCoord from)
        {
            _candidates.Clear();
            if (_destinations.GetReachable(KindFor(need), from, _candidates) == 0) return false;
            var state = need == VisitorNeed.Food ? VisitorState.SeekingFood
                : need == VisitorNeed.Drink ? VisitorState.SeekingDrink
                : need == VisitorNeed.Toilet ? VisitorState.SeekingToilet
                : VisitorState.Walking;

            while (_candidates.Count > 0)
            {
                int best = 0;
                int bestDist = int.MaxValue;
                for (int i = 0; i < _candidates.Count; i++)
                {
                    int dist = Manhattan(from, _candidates[i].InteractionPoint);
                    if (dist < bestDist) { bestDist = dist; best = i; }
                }
                var chosen = _candidates[best];
                _candidates.RemoveAt(best);
                if (StartRoute(v, chosen, state, from)) return true;
            }
            return false;
        }

        bool TryGoToAttraction(VisitorInstance v, GridCoord from)
        {
            _enclosureIds.Clear();
            _destinations.GetReachableEnclosures(from, _enclosureIds);
            while (_enclosureIds.Count > 0)
            {
                _weights.Clear();
                for (int i = 0; i < _enclosureIds.Count; i++)
                {
                    int id = _enclosureIds[i];
                    _weights.Add(VisitorMath.AttractionWeight(_destinations.TotalAnimalAppeal(id), v.RecentlyViewed(id),
                        _config.RecentViewingMultiplier));
                }
                int pick = VisitorMath.PickWeighted(_weights, _weights.Count, _rng.NextDouble());
                if (pick < 0) pick = 0; // every weight is zero (multiplier 0 and all recent): still go somewhere
                int enclosureId = _enclosureIds[pick];
                _enclosureIds.RemoveAt(pick);

                _candidates.Clear();
                _destinations.GetReachableViewpoints(enclosureId, from, _candidates);
                while (_candidates.Count > 0)
                {
                    int i = _rng.Next(_candidates.Count);
                    var chosen = _candidates[i];
                    _candidates.RemoveAt(i);
                    if (StartRoute(v, chosen, VisitorState.Walking, from)) return true;
                }
            }
            return false;
        }

        /// <summary>Routes to the destination and, if a route exists, commits state, target and the agent's walk. An empty route means the visitor is already there.</summary>
        bool StartRoute(VisitorInstance v, VisitorDestination dest, VisitorState state, GridCoord from)
        {
            if (!_navigation.TryFindRoute(from, dest.InteractionCells, out _, _route)) return false;
            v.TargetDestinationId = dest.Id;
            SetState(v, state);
            if (_route.Count == 0) OnArrived(v);
            else AgentOf(v)?.Follow(_route);
            return true;
        }

        void GoExit(VisitorInstance v)
        {
            AgentOf(v)?.Stop();
            v.CurrentEnclosureId = VisitorInstance.NoEnclosure;
            v.ActivityTimeLeft = 0f;
            v.TargetDestinationId = null;
            SetState(v, VisitorState.Exiting);

            var from = CellOf(v);
            _candidates.Clear();
            _destinations.GetReachable(VisitorDestinationKind.Exit, from, _candidates);
            while (_candidates.Count > 0)
            {
                int best = 0;
                int bestDist = int.MaxValue;
                for (int i = 0; i < _candidates.Count; i++)
                {
                    int dist = Manhattan(from, _candidates[i].InteractionPoint);
                    if (dist < bestDist) { bestDist = dist; best = i; }
                }
                var chosen = _candidates[best];
                _candidates.RemoveAt(best);
                if (StartRoute(v, chosen, VisitorState.Exiting, from)) return;
            }

            ForcedLeaveCount++; // no way out: the visitor leaves where they stand
            Leave(v);
        }

        void Leave(VisitorInstance v)
        {
            AgentOf(v)?.Stop();
            v.TargetDestinationId = null;
            Left?.Invoke(v);
            Detach(v.VisitorId);
        }

        // ---- Activities ------------------------------------------------------------------------------------

        void BeginViewing(VisitorInstance v, VisitorDestination dest)
        {
            AgentOf(v)?.Stop();
            v.CurrentEnclosureId = dest.EnclosureId;
            v.ActivityTimeLeft = _config.ViewDurationMinutes;
            SetState(v, VisitorState.ViewingAnimal);
        }

        void CompleteViewing(VisitorInstance v)
        {
            int enclosureId = v.CurrentEnclosureId;
            float appeal = _destinations.TotalAnimalAppeal(enclosureId);
            float score = VisitorMath.ViewingScore(appeal, _config.AppealForMaxViewingScore);
            v.VisitSatisfaction = VisitorMath.ViewingSatisfaction(v.VisitSatisfaction, score, _config.ViewingSatisfactionAdjustment);
            v.RememberViewed(enclosureId);
            VisitorMath.RecalculateHappiness(v);
            Decide(v);
        }

        void BeginResting(VisitorInstance v)
        {
            AgentOf(v)?.Stop();
            v.ActivityTimeLeft = _config.RestDurationMinutes;
            SetState(v, VisitorState.Resting);
        }

        void CompleteResting(VisitorInstance v)
        {
            v.Energy += _config.BenchEnergyGain;
            v.VisitSatisfaction += _config.FacilitySatisfactionBonus;
            VisitorMath.RecalculateHappiness(v);
            Decide(v);
        }

        void UseFacility(VisitorInstance v, VisitorDestination dest, VisitorDestinationKind expected)
        {
            if (dest == null || !dest.Enabled || dest.Kind != expected)
            {
                Decide(v);
                return;
            }
            switch (expected)
            {
                case VisitorDestinationKind.FoodStall: v.Hunger -= _config.FoodHungerReduction; break;
                case VisitorDestinationKind.DrinkStall: v.Thirst -= _config.DrinkThirstReduction; break;
                case VisitorDestinationKind.Toilet: v.ToiletNeed = _config.ToiletNeedAfterUse; break;
            }
            v.VisitSatisfaction += _config.FacilitySatisfactionBonus;
            VisitorMath.RecalculateHappiness(v);
            Decide(v);
        }

        // ---- Network changes -------------------------------------------------------------------------------

        /// <summary>
        /// Re-validates routes after the zoo changed (paths, fences, placed objects, enclosures). Cheap when nothing
        /// changed. A visitor whose destination is gone or unreachable drops its route and decides again; one with no
        /// useful destination left exits. Call once per frame from the driver.
        /// </summary>
        public void Process()
        {
            if (!_routesDirty) return;
            _routesDirty = false;

            _snapshot.Clear();
            _registry.CopyTo(_snapshot);
            for (int i = 0; i < _snapshot.Count; i++)
            {
                var v = _snapshot[i];
                if (!_registry.Contains(v.VisitorId)) continue; // left during this pass
                switch (v.State)
                {
                    case VisitorState.Entering:
                    case VisitorState.Walking:
                    case VisitorState.SeekingFood:
                    case VisitorState.SeekingDrink:
                    case VisitorState.SeekingToilet:
                    case VisitorState.Exiting:
                        Revalidate(v);
                        break;
                    case VisitorState.ViewingAnimal:
                    case VisitorState.Resting:
                        if (!_navigation.IsWalkable(CellOf(v))) Decide(v); // the ground went from under them
                        break;
                    default:
                        break;
                }
            }
        }

        void Revalidate(VisitorInstance v)
        {
            if (v.State == VisitorState.Entering)
            {
                // Still stepping out of the entrance (standing off the network): re-aim at its path cell, or give up.
                if (_destinations.TryGet(v.TargetDestinationId, out var entrance) && entrance.Enabled && entrance.IsPathAccessible)
                {
                    _route.Clear();
                    _route.Add(entrance.InteractionPoint);
                    AgentOf(v)?.Follow(_route);
                }
                else Decide(v);
                return;
            }

            var from = CellOf(v);
            if (_destinations.TryGet(v.TargetDestinationId, out var dest) && _destinations.IsReachable(dest, from)
                && _navigation.TryFindRoute(from, dest.InteractionCells, out _, _route))
            {
                if (_route.Count == 0) OnArrived(v);
                else AgentOf(v)?.Follow(_route);
                return;
            }

            // Unreachable or gone: clear the route, then choose something else (or leave).
            AgentOf(v)?.Stop();
            if (v.State == VisitorState.Exiting) GoExit(v);
            else Decide(v);
        }

        // ---- Helpers ---------------------------------------------------------------------------------------

        void SetState(VisitorInstance v, VisitorState state)
        {
            if (v.State == state) return;
            v.State = state;
            StateChanged?.Invoke(v);
        }

        static int Manhattan(GridCoord a, GridCoord b) => Mathf.Abs(a.X - b.X) + Mathf.Abs(a.Z - b.Z);
    }
}
