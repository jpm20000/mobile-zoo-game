using UnityEngine;

namespace ZooGame.Animals
{
    /// <summary>
    /// Marks an animal as pickable and holds the pick shape: a point above its feet and a world-space radius.
    /// Selection works by projecting these to the screen (see <see cref="AnimalSelectionController"/>), so no colliders
    /// or physics queries are needed. An optional marker object is shown while the animal is selected.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AnimalSelectable : MonoBehaviour
    {
        [SerializeField, Min(0.05f)] float pickRadius = 0.6f;
        [SerializeField, Min(0f)] float pickHeight = 0.4f;
        [Tooltip("Optional child shown while selected.")]
        [SerializeField] GameObject selectionMarker;

        public AnimalController Controller { get; internal set; }
        public float PickRadius => pickRadius;
        public Vector3 PickPoint => transform.position + Vector3.up * pickHeight;

        public void SetSelected(bool selected)
        {
            if (selectionMarker != null) selectionMarker.SetActive(selected);
        }
    }
}
