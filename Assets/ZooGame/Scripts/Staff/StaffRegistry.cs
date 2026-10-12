using System;
using System.Collections.Generic;

namespace ZooGame.Staff
{
    public sealed class StaffRegistry : IStaffRegistry
    {
        readonly Dictionary<string, StaffInstance> _byId = new Dictionary<string, StaffInstance>();
        readonly List<StaffInstance> _all = new List<StaffInstance>(16);

        public int Count => _all.Count;
        public IReadOnlyList<StaffInstance> All => _all;

        public event Action<StaffInstance> Registered;
        public event Action<StaffInstance> Unregistered;

        public bool Register(StaffInstance staff)
        {
            if (staff == null || string.IsNullOrEmpty(staff.StaffId) || _byId.ContainsKey(staff.StaffId)) return false;
            _byId.Add(staff.StaffId, staff);
            _all.Add(staff);
            Registered?.Invoke(staff);
            return true;
        }

        public bool Unregister(string staffId)
        {
            if (staffId == null || !_byId.TryGetValue(staffId, out var staff)) return false;
            _byId.Remove(staffId);
            _all.Remove(staff); // keeps hire order, which the UI shows
            Unregistered?.Invoke(staff);
            return true;
        }

        public bool TryGet(string staffId, out StaffInstance staff)
        {
            if (staffId == null) { staff = null; return false; }
            return _byId.TryGetValue(staffId, out staff);
        }

        public bool Contains(string staffId) => staffId != null && _byId.ContainsKey(staffId);

        public void CopyTo(List<StaffInstance> results)
        {
            for (int i = 0; i < _all.Count; i++) results.Add(_all[i]);
        }

        public int CountOf(StaffRole role)
        {
            int n = 0;
            for (int i = 0; i < _all.Count; i++)
                if (_all[i].Role == role) n++;
            return n;
        }
    }
}
