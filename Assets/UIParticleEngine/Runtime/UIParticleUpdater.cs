using System.Collections.Generic;
using UnityEngine;

namespace UIParticleEngine
{
    /// <summary>
    /// Drives all active <see cref="UIParticleEmitter"/> instances from a single hidden
    /// MonoBehaviour, instead of paying the per-component Update() dispatch cost.
    /// </summary>
    internal static class UIParticleUpdater
    {
        private static readonly List<UIParticleEmitter> _emitters = new List<UIParticleEmitter>();
        private static UpdaterBehaviour _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _emitters.Clear();
            _instance = null;
        }

        internal static int RegisteredCount => _emitters.Count;
        internal static bool DriverAlive => _instance != null;

        public static void Register(UIParticleEmitter emitter)
        {
            if (!_emitters.Contains(emitter))
                _emitters.Add(emitter);
            EnsureInstance();
        }

        public static void Unregister(UIParticleEmitter emitter)
        {
            _emitters.Remove(emitter);
        }

        private static void EnsureInstance()
        {
            if (_instance != null || !Application.isPlaying)
                return;

            var go = new GameObject("[UIParticleUpdater]")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<UpdaterBehaviour>();
        }

        // Runs after everything else in LateUpdate so attractors (and gameplay Emit
        // calls) land before the single per-frame mesh flush.
        [DefaultExecutionOrder(32000)]
        private sealed class UpdaterBehaviour : MonoBehaviour
        {
            private void Update()
            {
                float dt = Time.deltaTime;
                float unscaledDt = Time.unscaledDeltaTime;

                for (int i = _emitters.Count - 1; i >= 0; i--)
                    _emitters[i].Advance(dt, unscaledDt);
            }

            private void LateUpdate()
            {
                // One coalesced vertex build + SetMesh per emitter per frame, no matter
                // how many times simulation, Emit or attractors dirtied it.
                for (int i = _emitters.Count - 1; i >= 0; i--)
                    _emitters[i].FlushMesh();
            }
        }
    }
}
