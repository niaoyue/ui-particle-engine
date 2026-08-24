using UnityEngine;
using UnityEngine.EventSystems;

namespace UIParticleEngine.Samples
{
    /// <summary>
    /// Demo: attach to a fullscreen raycast-target Graphic (e.g. an invisible Image).
    /// Each click moves the emitter to the pointer and fires a burst.
    /// </summary>
    public class UIParticleClickBurst : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private UIParticleEmitter _Emitter;
        [SerializeField, Min(1)] private int _Count = 30;

        private void Awake()
        {
            // The emitter teleports to each click — World space keeps particles from
            // previous bursts in place instead of dragging them along.
            if (_Emitter != null)
                _Emitter.Main.SimulationSpace = UIParticleEmitter.SimulationSpace.World;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_Emitter == null)
                return;

            var emitterRect = (RectTransform)_Emitter.transform;
            var parentRect = emitterRect.parent as RectTransform;
            if (parentRect == null)
                return;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parentRect, eventData.position, eventData.pressEventCamera, out Vector2 local))
            {
                emitterRect.localPosition = local;
                _Emitter.Emit(_Count);
            }
        }
    }
}
