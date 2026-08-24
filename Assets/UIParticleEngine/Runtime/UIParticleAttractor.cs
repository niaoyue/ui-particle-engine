using System;
using UnityEngine;
using UnityEngine.Events;

namespace UIParticleEngine
{
    /// <summary>
    /// Pulls an emitter's particles toward this transform (coin-to-wallet, XP pickup...).
    /// Particles reaching <see cref="DestinationRadius"/> are consumed and fire
    /// <see cref="OnAttracted"/> once each.
    /// </summary>
    [AddComponentMenu("UI/Effects/UI Particle Attractor")]
    public class UIParticleAttractor : MonoBehaviour
    {
        public enum MovementMode
        {
            /// <summary>Constant pull toward the target.</summary>
            Linear,
            /// <summary>Pull eases in, giving particles an arcing gather.</summary>
            Smooth
        }

        [SerializeField] private UIParticleEmitter _Emitter;
        [Tooltip("Particles inside this radius (local units) are consumed.")]
        [SerializeField, Min(0f)] private float _DestinationRadius = 24f;
        [Tooltip("Seconds after spawn before a particle starts being attracted.")]
        [SerializeField, Min(0f)] private float _Delay = 0.3f;
        [Tooltip("Peak pull speed in local units per second.")]
        [SerializeField, Min(0f)] private float _MaxSpeed = 1500f;
        [SerializeField] private MovementMode _Movement = MovementMode.Smooth;
        [Tooltip("Invoked once per particle that reaches the attractor.")]
        [SerializeField] private UnityEvent _OnAttracted = new UnityEvent();

        public UIParticleEmitter Emitter
        {
            get => _Emitter;
            set => _Emitter = value;
        }

        public float DestinationRadius
        {
            get => _DestinationRadius;
            set => _DestinationRadius = Mathf.Max(0f, value);
        }

        public float Delay
        {
            get => _Delay;
            set => _Delay = Mathf.Max(0f, value);
        }

        public float MaxSpeed
        {
            get => _MaxSpeed;
            set => _MaxSpeed = Mathf.Max(0f, value);
        }

        public MovementMode Movement
        {
            get => _Movement;
            set => _Movement = value;
        }

        public UnityEvent OnAttracted => _OnAttracted;

        // Cached so LateUpdate does not allocate a delegate per frame (zero-GC contract).
        private Action _invokeAttracted;

        // LateUpdate: runs after UIParticleUpdater's simulation step for this frame,
        // so attraction sees final positions and re-pushes the mesh once when it acts.
        private void LateUpdate()
        {
            if (_Emitter == null || _Emitter.ParticleCount == 0 || _Emitter.IsPaused)
                return;

            float dt = (_Emitter.Main.UseUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime)
                       * _Emitter.Main.SimulationSpeed;
            if (dt <= 0f)
                return;

            _invokeAttracted ??= InvokeAttracted;
            Vector2 localTarget = _Emitter.rectTransform.InverseTransformPoint(transform.position);
            _Emitter.AttractParticles(localTarget, dt, _Delay, _MaxSpeed, _DestinationRadius,
                _Movement, _invokeAttracted);
        }

        private void InvokeAttracted()
        {
            _OnAttracted.Invoke();
        }
    }
}
