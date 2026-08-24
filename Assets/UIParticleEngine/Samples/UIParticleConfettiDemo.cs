using UnityEngine;
using UnityEngine.UI;

namespace UIParticleEngine.Samples
{
    /// <summary>
    /// Button-triggered confetti: corner emitters fire angled cones of tumbling
    /// debris pieces (a 3x3 flipbook with a random start frame per particle).
    /// </summary>
    public class UIParticleConfettiDemo : MonoBehaviour
    {
        [SerializeField] private Button _Button;
        [SerializeField] private UIParticleEmitter[] _Emitters;

        private void Awake()
        {
            if (_Button != null)
                _Button.onClick.AddListener(Fire);
        }

        public void Fire()
        {
            if (_Emitters == null)
                return;
            foreach (UIParticleEmitter emitter in _Emitters)
            {
                if (emitter == null)
                    continue;
                emitter.Stop(); // restart the (non-looping) cycle; alive pieces keep falling
                emitter.Play();
            }
        }
    }
}
