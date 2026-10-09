using System;
using System.Collections.Generic;

namespace ZooGame.Visitors
{
    public sealed class VisitorRegistry : IVisitorRegistry
    {
        readonly Dictionary<string, VisitorInstance> _byId = new Dictionary<string, VisitorInstance>();
        readonly List<VisitorInstance> _all = new List<VisitorInstance>(32);

        public int Count => _all.Count;
        public IReadOnlyList<VisitorInstance> All => _all;

        public event Action<VisitorInstance> Registered;
        public event Action<VisitorInstance> Unregistered;

        public bool Register(VisitorInstance visitor)
        {
            if (visitor == null || string.IsNullOrEmpty(visitor.VisitorId) || _byId.ContainsKey(visitor.VisitorId)) return false;
            _byId.Add(visitor.VisitorId, visitor);
            _all.Add(visitor);
            Registered?.Invoke(visitor);
            return true;
        }

        public bool Unregister(string visitorId)
        {
            if (visitorId == null || !_byId.TryGetValue(visitorId, out var visitor)) return false;
            _byId.Remove(visitorId);
            int i = _all.IndexOf(visitor);
            if (i >= 0)
            {
                // Swap-remove: order does not matter and this avoids shifting the list.
                int last = _all.Count - 1;
                _all[i] = _all[last];
                _all.RemoveAt(last);
            }
            Unregistered?.Invoke(visitor);
            return true;
        }

        public bool TryGet(string visitorId, out VisitorInstance visitor)
        {
            if (visitorId == null) { visitor = null; return false; }
            return _byId.TryGetValue(visitorId, out visitor);
        }

        public bool Contains(string visitorId) => visitorId != null && _byId.ContainsKey(visitorId);

        public void CopyTo(List<VisitorInstance> results)
        {
            for (int i = 0; i < _all.Count; i++) results.Add(_all[i]);
        }
    }
}
