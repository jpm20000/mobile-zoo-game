using UnityEngine.EventSystems;
using ZooGame.Input;

namespace ZooGame.UI
{
    /// <summary>Reports whether a pointer is over uGUI so world/camera input can yield to UI.</summary>
    public sealed class EventSystemPointerBlocker : IPointerUiBlocker
    {
        public bool IsOverUi(int pointerId)
        {
            var es = EventSystem.current;
            return es != null && es.IsPointerOverGameObject(pointerId);
        }
    }
}
