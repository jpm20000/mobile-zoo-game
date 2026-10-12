using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ZooGame.Data;
using ZooGame.Placement;
using ZooGame.Staff;
using ZooGame.Tests.EditMode.Visitors;
using ZooGame.World;

namespace ZooGame.Tests.EditMode.Staff
{
    /// <summary>A stand-in for the scene body: records what it was told and "walks" instantly when asked to arrive.</summary>
    public sealed class FakeStaffAgent : IStaffAgent
    {
        readonly StaffWorkService _work;
        readonly ZooGrid _grid;

        public FakeStaffAgent(StaffInstance instance, StaffWorkService work, ZooGrid grid)
        {
            Instance = instance;
            _work = work;
            _grid = grid;
        }

        public StaffInstance Instance { get; }
        public readonly List<GridCoord> Route = new List<GridCoord>();
        public int FollowCalls;
        public int StopCalls;

        public bool HasRoute => Route.Count > 0;
        public GridCoord Destination => Route[Route.Count - 1];

        public void Follow(IReadOnlyList<GridCoord> route)
        {
            FollowCalls++;
            Route.Clear();
            for (int i = 0; i < route.Count; i++) Route.Add(route[i]);
        }

        public void Stop()
        {
            StopCalls++;
            Route.Clear();
        }

        /// <summary>Teleports to the end of the current route and reports arrival, as the controller would.</summary>
        public void Arrive()
        {
            if (Route.Count > 0) Instance.Position = _grid.GridToWorld(Route[Route.Count - 1]);
            Route.Clear();
            _work.OnArrived(Instance);
        }
    }

    /// <summary>
    /// The M6 visitor fixture (real grid, construction, placement, navigation and destinations, path along z = 6,
    /// facilities placed under it at z = 5) plus the real staff services and fake agents.
    /// </summary>
    public sealed class StaffFixture
    {
        public readonly VisitorFixture V = new VisitorFixture();
        public readonly StaffConfig Config = StaffConfig.CreateDefault();
        public readonly StaffRegistry Staff = new StaffRegistry();
        public readonly StaffFacilityRegistry Facilities;
        public readonly StaffAssignmentService Assignments;
        public readonly StaffTaskPool Tasks;
        public readonly StaffWorkService Work;
        public readonly Dictionary<string, FakeStaffAgent> Agents = new Dictionary<string, FakeStaffAgent>();

        public StaffFixture()
        {
            Facilities = new StaffFacilityRegistry(V.Placement);
            Assignments = new StaffAssignmentService(Staff, Facilities);
            Tasks = new StaffTaskPool(Staff);
            Work = new StaffWorkService(Staff, Facilities, Assignments, Tasks, V.Nav, V.Destinations, V.Animals.Registry);
        }

        public PlacementMap Placement => V.Placement;
        public ZooGrid Grid => V.Grid;

        public static GridCoord C(int x, int z) => VisitorFixture.C(x, z);

        // ---- Layout ----

        public PlacedObject Stall(int x, int z, VisitorFacilityKind kind = VisitorFacilityKind.FoodStall) => V.Place(kind, x, z);

        public static string IdOf(PlacedObject o) => VisitorFixture.FacilityId(o);

        public StaffFacility FacilityOf(PlacedObject o)
        {
            Assert.IsTrue(Facilities.TryGet(IdOf(o), out var f), "no staff facility for " + IdOf(o));
            return f;
        }

        public VisitorDestinationAccess Destination(PlacedObject o) => new VisitorDestinationAccess(V, o);

        // ---- Staff ----

        /// <summary>Registers a member standing on a cell, with an agent attached.</summary>
        public StaffInstance Add(StaffRole role, int x, int z)
        {
            var s = StaffInstance.CreateNew(role, role + "-" + Staff.Count, Grid.GridToWorld(C(x, z)));
            Assert.IsTrue(Staff.Register(s));
            var agent = new FakeStaffAgent(s, Work, Grid);
            Agents[s.StaffId] = agent;
            Work.Attach(agent);
            return s;
        }

        public FakeStaffAgent AgentOf(StaffInstance s) => Agents[s.StaffId];

        /// <summary>Processes pending changes and, if the member is walking, completes the walk (repeatedly until they stop).</summary>
        public void Settle(StaffInstance s)
        {
            Work.Process();
            for (int guard = 0; guard < 8 && AgentOf(s).HasRoute; guard++)
            {
                AgentOf(s).Arrive();
                Work.Process();
            }
        }

        public void Dispose()
        {
            Work.Dispose();
            Tasks.Dispose();
            Assignments.Dispose();
            Facilities.Dispose();
            Object.DestroyImmediate(Config);
            V.Dispose();
        }
    }

    /// <summary>Convenience lookups of a facility's visitor-side destination.</summary>
    public readonly struct VisitorDestinationAccess
    {
        readonly VisitorFixture _v;
        readonly PlacedObject _obj;

        public VisitorDestinationAccess(VisitorFixture v, PlacedObject obj)
        {
            _v = v;
            _obj = obj;
        }

        public bool Exists => _v.Destinations.TryGet(VisitorFixture.FacilityId(_obj), out _);

        /// <summary>Usable by visitors right now.</summary>
        public bool Enabled
        {
            get
            {
                Assert.IsTrue(_v.Destinations.TryGet(VisitorFixture.FacilityId(_obj), out var d));
                return d.Enabled;
            }
        }
    }
}
