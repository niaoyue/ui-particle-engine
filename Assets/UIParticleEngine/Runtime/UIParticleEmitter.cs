using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace UIParticleEngine
{
    /// <summary>
    /// Lightweight UI particle emitter mirroring Unity's ParticleSystem (Shuriken) module
    /// layout and naming, but simulating in plain C# (zero per-frame GC) and rendering
    /// as quads through the standard uGUI pipeline — so particles batch with the canvas
    /// and respect Mask / RectMask2D.
    /// </summary>
    [AddComponentMenu("UI/Effects/UI Particle Emitter")]
    [RequireComponent(typeof(CanvasRenderer))]
    public class UIParticleEmitter : MaskableGraphic
    {
        // 2D/UI adaptation of ParticleSystemShapeType (Point is a UI extra: radial in all directions).
        public enum ShapeType
        {
            Cone,
            Circle,
            Rect,
            Point
        }

        public enum SimulationSpace
        {
            Local,
            World
        }

        public enum StopBehavior
        {
            StopEmitting,
            StopEmittingAndClear
        }

        [Serializable]
        public class MainModule
        {
            [Min(0.05f)] public float Duration = 5f;
            public bool Looping = true;
            [Tooltip("Looping systems only: simulate one full duration before the first visible frame.")]
            public bool Prewarm = false;
            [Min(0f)] public float StartDelay = 0f;
            [Tooltip("Seconds.")] public RangeF StartLifetime = new RangeF(0.6f, 1.2f);
            [Tooltip("Local units per second.")] public RangeF StartSpeed = new RangeF(60f, 140f);
            [Tooltip("Local units.")] public RangeF StartSize = new RangeF(8f, 16f);
            [Tooltip("Degrees.")] public RangeF StartRotation = new RangeF(0f, 0f);
            [Tooltip("Each particle starts with a random color between these two.")]
            public Color StartColorA = Color.white;
            public Color StartColorB = Color.white;
            [Tooltip("Multiplies Physics2D.gravity, scaled x100 into UI pixels (1 ≈ -981 px/s²).")]
            public float GravityModifier = 0f;
            public SimulationSpace SimulationSpace = SimulationSpace.Local;
            [Min(0f)] public float SimulationSpeed = 1f;
            [Tooltip("Keep simulating while Time.timeScale is 0 (pause menus etc.).")]
            public bool UseUnscaledTime = false;
            public bool PlayOnAwake = true;
            [Min(1)] public int MaxParticles = 256;
            public bool AutoRandomSeed = true;
            public uint RandomSeed = 0;
        }

        [Serializable]
        public class EmissionModule
        {
            public bool Enabled = true;
            [Min(0f)] public float RateOverTime = 20f;
            [Tooltip("Particles per local unit moved. Only meaningful with World simulation space.")]
            [Min(0f)] public float RateOverDistance = 0f;
            public Burst[] Bursts = Array.Empty<Burst>();
        }

        [Serializable]
        public struct Burst
        {
            [Tooltip("Seconds into the emission cycle.")]
            [Min(0f)] public float Time;
            public RangeF Count;
            [Tooltip("How many times the burst repeats (min 1).")]
            [Min(1)] public int Cycles;
            [Tooltip("Seconds between repeats.")]
            [Min(0.01f)] public float Interval;
            [Tooltip("Chance each repeat fires. 0 never fires (Shuriken semantics).")]
            [Range(0f, 1f)] public float Probability;

            public Burst(float time, RangeF count, int cycles = 1, float interval = 0.01f, float probability = 1f)
            {
                this.Time = time;
                this.Count = count;
                this.Cycles = cycles;
                this.Interval = interval;
                this.Probability = probability;
            }
        }

        [Serializable]
        public class ShapeModule
        {
            public bool Enabled = true;
            public ShapeType ShapeType = ShapeType.Cone;
            [Tooltip("Circle radius / cone base half-width, in local units.")]
            [Min(0f)] public float Radius = 25f;
            [Tooltip("1 = emit from the whole area, 0 = edge only (Circle).")]
            [Range(0f, 1f)] public float RadiusThickness = 1f;
            [Tooltip("Cone half-angle in degrees.")]
            [Range(0f, 90f)] public float Angle = 25f;
            [Tooltip("Circle arc in degrees, starting at +X.")]
            [Range(0f, 360f)] public float Arc = 360f;
            [Tooltip("Rotates the shape / emission direction (0 = up).")]
            public float Rotation = 0f;
        }

        [Serializable]
        public class VelocityOverLifetimeModule
        {
            public bool Enabled = false;
            [Tooltip("Constant extra velocity in local units per second.")]
            public Vector2 Linear = Vector2.zero;
        }

        [Serializable]
        public class LimitVelocityOverLifetimeModule
        {
            public bool Enabled = false;
            [Tooltip("Linear damping per second.")]
            [Min(0f)] public float Drag = 1f;
        }

        [Serializable]
        public class ColorOverLifetimeModule
        {
            public bool Enabled = false;
            public Gradient Color = DefaultFadeGradient();
        }

        [Serializable]
        public class SizeOverLifetimeModule
        {
            public bool Enabled = false;
            public AnimationCurve Size = AnimationCurve.Constant(0f, 1f, 1f);
        }

        [Serializable]
        public class RotationOverLifetimeModule
        {
            public bool Enabled = false;
            [Tooltip("Degrees per second, random between min/max per particle.")]
            public RangeF AngularVelocity = new RangeF(-45f, 45f);
        }

        [Serializable]
        public class TextureSheetAnimationModule
        {
            public bool Enabled = false;
            [Min(1)] public int NumTilesX = 1;
            [Min(1)] public int NumTilesY = 1;
            [Tooltip("How many times the sheet plays over the particle's lifetime.")]
            [Min(1)] public int CycleCount = 1;
            public bool RandomStartFrame = false;
            [Tooltip("Sheet playback over lifetime: 0 = first frame, 1 = last frame.")]
            public AnimationCurve FrameOverTime = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        }

        [Serializable]
        public class TrailModule
        {
            public bool Enabled = false;
            [Tooltip("Fraction of particles that get a trail.")]
            [Range(0f, 1f)] public float Ratio = 1f;
            [Tooltip("Trail point lifetime as a multiplier of the particle's lifetime.")]
            public RangeF Lifetime = new RangeF(0.25f, 0.25f);
            [Tooltip("A new trail point is recorded after the particle moves this far (local units).")]
            [Min(0.01f)] public float MinVertexDistance = 4f;
            [Tooltip("Max stored points per trail (fixed zero-alloc buffer; oldest points drop first).")]
            [Range(2, 32)] public int MaxPoints = 12;
            [Tooltip("Multiply trail width by the particle's size.")]
            public bool SizeAffectsWidth = true;
            [Tooltip("Tint the trail with the particle's current color.")]
            public bool InheritParticleColor = true;
            [Tooltip("Width along the trail: 0 = head, 1 = tail.")]
            public AnimationCurve WidthOverTrail = AnimationCurve.Constant(0f, 1f, 1f);
            [Tooltip("Color along the trail: 0 = head, 1 = tail.")]
            public Gradient ColorOverTrail = DefaultFadeGradient();
        }

        private struct Particle
        {
            public Vector2 Position;
            public Vector2 Velocity;
            public float Rotation;
            public float AngularVelocity;
            public float Size;
            public float Lifetime;
            public float Age;
            public Color32 StartColor;
            public float StartFrame;  // texture sheet start offset, normalized 0..1
            public bool HasTrail;
            public short TrailHead;   // ring index of the oldest stored point
            public short TrailCount;
            public float TrailLifetime;
        }

        private struct TrailPoint
        {
            public Vector2 Position;
            public float Time;
        }

        [SerializeField] private MainModule _Main = new MainModule();
        [SerializeField] private EmissionModule _Emission = new EmissionModule();
        [SerializeField] private ShapeModule _Shape = new ShapeModule();
        [SerializeField] private VelocityOverLifetimeModule _VelocityOverLifetime = new VelocityOverLifetimeModule();
        [SerializeField] private LimitVelocityOverLifetimeModule _LimitVelocityOverLifetime = new LimitVelocityOverLifetimeModule();
        [SerializeField] private ColorOverLifetimeModule _ColorOverLifetime = new ColorOverLifetimeModule();
        [SerializeField] private SizeOverLifetimeModule _SizeOverLifetime = new SizeOverLifetimeModule();
        [SerializeField] private RotationOverLifetimeModule _RotationOverLifetime = new RotationOverLifetimeModule();
        [SerializeField] private TextureSheetAnimationModule _TextureSheetAnimation = new TextureSheetAnimationModule();
        [SerializeField] private TrailModule _Trails = new TrailModule();
        [SerializeField] private Sprite _Sprite;

        /// <summary>
        /// Compact vertex written straight into the Mesh vertex buffer (24 bytes vs the
        /// ~90-byte UIVertex the uGUI rebuild pipeline copies around). UI/Default only
        /// needs position, color and uv0.
        /// </summary>
        private struct ParticleVertex
        {
            public Vector3 Position;
            public Color32 Color;
            public Vector2 Uv;
        }

        private static readonly VertexAttributeDescriptor[] _vertexLayout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2)
        };

        private const MeshUpdateFlags MeshFlags = MeshUpdateFlags.DontValidateIndices
            | MeshUpdateFlags.DontNotifyMeshUsers
            | MeshUpdateFlags.DontRecalculateBounds
            | MeshUpdateFlags.DontResetBoneBounds;

        // Canvas rendering does not frustum-cull UI meshes, so generous static bounds are fine.
        private static readonly Bounds _meshBounds = new Bounds(Vector3.zero, new Vector3(1e5f, 1e5f, 1f));

        // Scratch buffers for trail ribbon building (grow-only, shared across emitters).
        private static Vector2[] _trailPos = new Vector2[33];
        private static Vector2[] _trailPerp = new Vector2[33];
        private static float[] _trailHalfWidth = new float[33];
        private static Color32[] _trailColor = new Color32[33];

        // Per-build lookup tables for curves/gradients (shared: main thread only, filled
        // at the top of each mesh build). Replaces per-particle/per-trail-point extern
        // Evaluate calls with array reads — the dominant CPU cost in trail-heavy scenes.
        private const int LutSize = 64;
        private static readonly float[] _sizeLut = new float[LutSize];
        private static readonly float[] _frameLut = new float[LutSize];
        private static readonly float[] _widthLut = new float[LutSize];
        private static readonly Color[] _colorLut = new Color[LutSize];
        private static readonly Color[] _trailColorLut = new Color[LutSize];

        private static int LutIndex(float t)
        {
            int i = (int)(t * (LutSize - 1) + 0.5f);
            return i < 0 ? 0 : (i >= LutSize ? LutSize - 1 : i);
        }

        private static void BakeCurve(AnimationCurve curve, float[] lut)
        {
            const float step = 1f / (LutSize - 1);
            for (int i = 0; i < LutSize; i++)
                lut[i] = curve.Evaluate(i * step);
        }

        private static void BakeGradient(Gradient gradient, Color[] lut)
        {
            const float step = 1f / (LutSize - 1);
            for (int i = 0; i < LutSize; i++)
                lut[i] = gradient.Evaluate(i * step);
        }

        private Particle[] _particles;
        private int _count;
        private TrailPoint[] _trailPoints;      // maxParticles * pointsPerParticle, slot-indexed
        private int _trailPointsPerParticle;
        private float _simTime;
        // NonSerialized: Unity's domain-reload hot-swap serializes even private fields;
        // a Mesh[] would come back as a LENGTH-0 array (HideAndDontSave refs die) while
        // ints keep stale values — this state must always start fresh after a reload.
        [NonSerialized] private Mesh[] _meshes; // double buffer: never mutate the mesh the render thread holds
        [NonSerialized] private int _meshFlip;
        private int _lastFlipFrame = -1;        // flip at most once per frame so re-pushes reuse this frame's mesh
        private bool _meshDirty;
        private NativeArray<ParticleVertex> _vertexBuffer;
        private int _quadCapacity;
        private int _quadCount;
        private int _trailedCount;              // alive particles that own a trail (sizes the vertex buffer)
        private Sprite _cachedUVSprite;
        private Vector4 _cachedUV = new Vector4(0f, 0f, 1f, 1f);
        private XorShift32 _rng;
        private bool _rngInitialized;
        private bool _isEmitting;
        private bool _isPaused;
        private float _playbackTime;   // seconds since Play(), includes startDelay
        private float _cycleTime;      // seconds into the current emission cycle
        private bool _delayDone;
        private float _emitAccumulator;
        private float _distanceEmitAccumulator;
        private float _frameMoveDistance;
        private bool _wasAliveLastFrame;
        private Vector3 _prevWorldPos;
        private bool _hasPrevWorldPos;

        protected UIParticleEmitter()
        {
            useLegacyMeshGeneration = false;
        }

        // ---------- ParticleSystem-style module accessors ----------

        // Note: modules are serialized class instances. Undo, prefab revert and Preset
        // application re-deserialize the component and REPLACE these instances — editor
        // tooling must re-fetch a module after such operations rather than caching it.

        /// <summary>Duration, looping, start values, gravity, space, seed — mirrors ParticleSystem.main.</summary>
        public MainModule Main => _Main;
        /// <summary>Rate over time/distance and bursts — mirrors ParticleSystem.emission.</summary>
        public EmissionModule Emission => _Emission;
        /// <summary>Emission shape (Cone/Circle/Rect/Point) — mirrors ParticleSystem.shape.</summary>
        public ShapeModule Shape => _Shape;
        /// <summary>Constant extra velocity — subset of ParticleSystem.velocityOverLifetime.</summary>
        public VelocityOverLifetimeModule VelocityOverLifetime => _VelocityOverLifetime;
        /// <summary>Linear drag — subset of ParticleSystem.limitVelocityOverLifetime.</summary>
        public LimitVelocityOverLifetimeModule LimitVelocityOverLifetime => _LimitVelocityOverLifetime;
        /// <summary>Gradient tint over each particle's lifetime.</summary>
        public ColorOverLifetimeModule ColorOverLifetime => _ColorOverLifetime;
        /// <summary>Size multiplier curve over each particle's lifetime.</summary>
        public SizeOverLifetimeModule SizeOverLifetime => _SizeOverLifetime;
        /// <summary>Angular velocity applied over each particle's lifetime.</summary>
        public RotationOverLifetimeModule RotationOverLifetime => _RotationOverLifetime;
        /// <summary>Grid flipbook playback over each particle's lifetime.</summary>
        public TextureSheetAnimationModule TextureSheetAnimation => _TextureSheetAnimation;
        /// <summary>Ribbon trails behind particles — mirrors ParticleSystem.trails.</summary>
        public TrailModule Trails => _Trails;

        public Sprite Sprite
        {
            get => _Sprite;
            set
            {
                if (_Sprite == value)
                    return;
                _Sprite = value;
                SetMaterialDirty();
                // UVs are baked into the vertex buffer, so the mesh must be re-pushed
                // too — otherwise a paused/stopped system keeps the old atlas region.
                SetVerticesDirty();
            }
        }

        /// <summary>True while emitting and not paused.</summary>
        public bool IsPlaying => _isEmitting && !_isPaused;
        /// <summary>True while paused via <see cref="Pause"/>.</summary>
        public bool IsPaused => _isPaused;
        /// <summary>True when not emitting (alive particles may still be finishing).</summary>
        public bool IsStopped => !_isEmitting;
        /// <summary>Number of currently alive particles.</summary>
        public int ParticleCount => _count;
        /// <summary>Seconds into the current emission cycle.</summary>
        public float Time => _cycleTime;

        public override Texture mainTexture
        {
            get
            {
                if (_Sprite != null && _Sprite.texture != null)
                    return _Sprite.texture;
                return s_WhiteTexture != null ? (Texture)s_WhiteTexture : Texture2D.whiteTexture;
            }
        }

        // ---------- ParticleSystem-style playback API ----------

        /// <summary>Starts the system, or resumes it when paused. No-op if already playing.</summary>
        public void Play()
        {
            if (_isPaused)
            {
                _isPaused = false;
                return;
            }
            if (_isEmitting)
                return;

            InitRng();
            _isEmitting = true;
            _isPaused = false;
            _playbackTime = 0f;
            _cycleTime = 0f;
            _delayDone = false;
            _emitAccumulator = 0f;
            _distanceEmitAccumulator = 0f;
            _frameMoveDistance = 0f;
            if (_count == 0)
                _simTime = 0f; // keep trail timestamps precise across very long sessions

            if (_Main.Prewarm && _Main.Looping)
            {
                // Prewarm yields a steady-state field; startDelay is orthogonal to it
                // (Shuriken semantics) — skip the delay for the prewarm simulation.
                _playbackTime = _Main.StartDelay;

                const int prewarmSteps = 32;
                float step = Mathf.Max(0.05f, _Main.Duration) / prewarmSteps;
                for (int i = 0; i < prewarmSteps; i++)
                    SimulateStep(step);

                // Show the prewarmed field this frame — even if the system is paused
                // right after Play(), the visuals must not stay blank.
                if (isActiveAndEnabled)
                {
                    _meshDirty = true;
                    FlushMesh();
                    _wasAliveLastFrame = _count > 0;
                }
            }
        }

        /// <summary>Pauses the simulation; Play() resumes it.</summary>
        public void Pause()
        {
            if (_isEmitting || _count > 0)
                _isPaused = true;
        }

        /// <summary>Stops emitting. Alive particles finish naturally unless clearing.</summary>
        public void Stop(StopBehavior behavior = StopBehavior.StopEmitting)
        {
            _isEmitting = false;
            _isPaused = false;
            if (behavior == StopBehavior.StopEmittingAndClear)
                Clear();
        }

        /// <summary>Removes all alive particles immediately.</summary>
        public void Clear()
        {
            _count = 0;
            _trailedCount = 0;
            if (canvasRenderer != null)
            {
                _meshDirty = true;
                FlushMesh();
            }
        }

        /// <summary>Emits count particles right now, regardless of play state.</summary>
        public void Emit(int count)
        {
            if (count <= 0)
                return;
            if (!_rngInitialized)
                InitRng();
            SpawnParticles(count);
            // Render immediately so Emit shows up even while paused/stopped, and make
            // sure the frame after their death still gets one clearing push.
            if (isActiveAndEnabled)
            {
                _meshDirty = true;
                FlushMesh();
                _wasAliveLastFrame = _count > 0;
            }
        }

        /// <summary>True while the system is emitting or particles are still alive.</summary>
        public bool IsAlive() => _isEmitting || _count > 0;

        // ---------- lifecycle ----------

        protected override void OnEnable()
        {
            base.OnEnable();
            _hasPrevWorldPos = false;
            if (Application.isPlaying)
            {
                UIParticleUpdater.Register(this);
                if (_Main.PlayOnAwake)
                    Play();
            }
        }

        protected override void OnDisable()
        {
            UIParticleUpdater.Unregister(this);
            _count = 0;
            _isEmitting = false;
            _isPaused = false;
            _wasAliveLastFrame = false;
            _trailedCount = 0;
            base.OnDisable();
#if UNITY_EDITOR
            // Domain reload calls OnDisable, not OnDestroy — free native memory here in
            // the editor (edit-mode previews would otherwise leak on every recompile).
            // Players keep the buffers across disable/enable: pooled UI effects toggle
            // constantly and rebuilding multi-MB buffers per toggle is the worse trade.
            ReleaseMeshResources();
#endif
        }

        protected override void OnDestroy()
        {
            ReleaseMeshResources();
            base.OnDestroy();
        }

        /// <summary>
        /// Frees the native vertex buffer and the double-buffered meshes. Called from
        /// OnDisable (OnDestroy alone is NOT called on domain reload, which would leak
        /// the Allocator.Persistent buffer after edit-mode previews) — everything is
        /// rebuilt on demand by EnsureMeshCapacity after a re-enable.
        /// </summary>
        private void ReleaseMeshResources()
        {
            if (_vertexBuffer.IsCreated)
                _vertexBuffer.Dispose();
            if (_meshes != null)
            {
                for (int i = 0; i < _meshes.Length; i++)
                {
                    if (_meshes[i] != null)
                        DestroyImmediate(_meshes[i]);
                }
                _meshes = null;
            }
            _quadCapacity = 0;
            _quadCount = 0;
        }

        // ---------- simulation ----------

        /// <summary>
        /// Advances one frame's simulation and marks the mesh dirty. Called by
        /// <see cref="UIParticleUpdater"/> in Update; the updater flushes the mesh once
        /// per frame in late LateUpdate, so Emit/attractor activity in the same frame
        /// coalesces into a single vertex build + SetMesh.
        /// </summary>
        // A hitch frame would otherwise move fast particles through one huge step,
        // teleporting them and drawing their trails as long straight spikes. Frame
        // updates are clamped (dropping the excess, like Time.maximumDeltaTime) and
        // subdivided into steps of at most MaxStepDeltaTime.
        private const float MaxStepDeltaTime = 1f / 30f;
        private const float MaxFrameDeltaTime = 4f / 30f;

        internal void Advance(float scaledDt, float unscaledDt)
        {
            if (_isPaused)
                return;
            float dt = (_Main.UseUnscaledTime ? unscaledDt : scaledDt) * _Main.SimulationSpeed;
            Step(Mathf.Min(dt, MaxFrameDeltaTime));
        }

        /// <summary>
        /// Manually advances the system by deltaTime seconds and renders the result —
        /// works outside play mode too (used by the inspector preview). Note: unlike
        /// ParticleSystem.Simulate, this advances incrementally by deltaTime; it does
        /// NOT restart and fast-forward.
        /// </summary>
        public void Simulate(float deltaTime)
        {
            Step(deltaTime);
            FlushMesh(); // manual calls render immediately, no updater involved
        }

        private void Step(float dt)
        {
            if (dt <= 0f)
                return;

            CompensateWorldMovement();
            // Subdivide large deltas so integration (and trail recording) stays smooth;
            // manual Simulate(dt) callers still advance by the full requested time.
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / MaxStepDeltaTime));
            float stepDt = dt / steps;
            for (int i = 0; i < steps; i++)
                SimulateStep(stepDt);

            bool alive = _count > 0;
            if (alive || _wasAliveLastFrame)
                _meshDirty = true;
            _wasAliveLastFrame = alive;
        }

        /// <summary>Builds and pushes the mesh if anything changed since the last push.</summary>
        internal void FlushMesh()
        {
            if (!_meshDirty)
                return;
            if (canvasRenderer.cull)
                return; // stays dirty; pushed on the frame culling ends
            // Scripts may have MOVED the emitter after Advance ran (per-shell teleports,
            // click bursts). World-space particles are stored emitter-local, so pushing
            // now without re-compensating would render every alive particle offset by
            // that movement for one frame — a visible whole-effect jump.
            CompensateWorldMovement();
            _meshDirty = false;
            UpdateMesh();
        }

        private void SimulateStep(float dt)
        {
            _simTime += dt;

            if (_isEmitting)
                UpdateEmission(dt);

            Vector2 gravityDt = Physics2D.gravity * (_Main.GravityModifier * 100f) * dt;
            bool hasGravity = _Main.GravityModifier != 0f;
            float dragFactor = _LimitVelocityOverLifetime.Enabled
                ? Mathf.Clamp01(1f - _LimitVelocityOverLifetime.Drag * dt)
                : 1f;
            Vector2 linearVelocity = _VelocityOverLifetime.Enabled ? _VelocityOverLifetime.Linear : Vector2.zero;
            bool hasAngular = _RotationOverLifetime.Enabled;
            bool trailsOn = _Trails.Enabled && _trailPoints != null;
            // Keep trail slot regions paired with their particles even while the module
            // is toggled off — otherwise re-enabling trails renders dead particles' data.
            bool copyTrails = _trailPoints != null;
            int trailCap = _trailPointsPerParticle;

            for (int i = _count - 1; i >= 0; i--)
            {
                ref Particle p = ref _particles[i];
                p.Age += dt;
                if (p.Age >= p.Lifetime)
                {
                    if (p.HasTrail)
                        _trailedCount--;
                    _particles[i] = _particles[--_count];
                    // The swapped-in particle's trail lives in the last slot's buffer region.
                    if (copyTrails && i != _count)
                        Array.Copy(_trailPoints, _count * trailCap, _trailPoints, i * trailCap, trailCap);
                    continue;
                }

                if (hasGravity)
                    p.Velocity += gravityDt;
                if (dragFactor != 1f)
                    p.Velocity *= dragFactor;
                p.Position += (p.Velocity + linearVelocity) * dt;
                if (hasAngular)
                    p.Rotation += p.AngularVelocity * dt;

                if (trailsOn && p.HasTrail)
                    UpdateTrail(ref p, i * trailCap, trailCap);
            }
        }

        private void UpdateTrail(ref Particle p, int baseIndex, int capacity)
        {
            // Expire points older than the trail lifetime.
            while (p.TrailCount > 0)
            {
                if (_simTime - _trailPoints[baseIndex + p.TrailHead].Time <= p.TrailLifetime)
                    break;
                p.TrailHead = (short)((p.TrailHead + 1) % capacity);
                p.TrailCount--;
            }

            // Record a new point once the particle moved far enough from the last one.
            if (p.TrailCount > 0)
            {
                int newest = baseIndex + (p.TrailHead + p.TrailCount - 1) % capacity;
                float minDist = _Trails.MinVertexDistance;
                if ((p.Position - _trailPoints[newest].Position).sqrMagnitude < minDist * minDist)
                    return;
            }

            int index;
            if (p.TrailCount == capacity)
            {
                index = baseIndex + p.TrailHead; // overwrite the oldest slot; it becomes the newest
                p.TrailHead = (short)((p.TrailHead + 1) % capacity);
            }
            else
            {
                index = baseIndex + (p.TrailHead + p.TrailCount) % capacity;
                p.TrailCount++;
            }
            _trailPoints[index].Position = p.Position;
            _trailPoints[index].Time = _simTime;
        }

        private void UpdateEmission(float dt)
        {
            _playbackTime += dt;

            float sinceDelay = _playbackTime - _Main.StartDelay;
            if (sinceDelay < 0f)
                return;

            float prevCycleTime;
            float burstWindowStart;
            if (!_delayDone)
            {
                _delayDone = true;
                prevCycleTime = 0f;
                burstWindowStart = -1f; // first frame: bursts at time 0 must fire (window is exclusive)
                _cycleTime = sinceDelay;
            }
            else
            {
                prevCycleTime = _cycleTime;
                burstWindowStart = prevCycleTime;
                _cycleTime += dt;
            }

            float duration = Mathf.Max(0.05f, _Main.Duration);

            if (_Emission.Enabled)
            {
                if (_Emission.RateOverTime > 0f)
                {
                    // Non-looping systems must not accumulate rate past the cycle end —
                    // a large dt would otherwise over-emit before the stop check runs.
                    float emitTo = _Main.Looping ? _cycleTime : Mathf.Min(_cycleTime, duration);
                    float window = emitTo - Mathf.Min(prevCycleTime, duration);
                    if (window > 0f)
                    {
                        _emitAccumulator += _Emission.RateOverTime * window;
                        int n = (int)_emitAccumulator;
                        if (n > 0)
                        {
                            _emitAccumulator -= n;
                            SpawnParticles(n);
                        }
                    }
                }

                if (_Emission.RateOverDistance > 0f && _frameMoveDistance > 0f)
                {
                    _distanceEmitAccumulator += _Emission.RateOverDistance * _frameMoveDistance;
                    _frameMoveDistance = 0f;
                    int n = (int)_distanceEmitAccumulator;
                    if (n > 0)
                    {
                        _distanceEmitAccumulator -= n;
                        SpawnParticles(n);
                    }
                }

                // Burst repeats scheduled past the cycle end never fire (Shuriken drops
                // them) — clamping also keeps this frame-timing independent.
                FireBursts(burstWindowStart, Mathf.Min(_cycleTime, duration));
            }

            // A single large dt can span several cycle starts (tiny duration, frame hitch,
            // high simulationSpeed) — wrap repeatedly so no cycle's bursts are dropped and
            // _cycleTime always lands back inside [0, duration).
            int wrapGuard = 0;
            while (_cycleTime >= duration)
            {
                if (!_Main.Looping)
                {
                    _isEmitting = false;
                    break;
                }

                _cycleTime -= duration;
                if (_Emission.Enabled)
                {
                    // Fully skipped cycles get their whole burst window; the final wrap
                    // only the part of the new cycle actually reached.
                    bool lastWrap = _cycleTime < duration;
                    FireBursts(-1f, lastWrap ? _cycleTime : duration);
                }

                if (++wrapGuard >= 8)
                {
                    // Pathological dt (e.g. 10s hitch on a 0.05s cycle): clamp instead of
                    // spinning — maxParticles would cap the spawns anyway.
                    _cycleTime %= duration;
                    break;
                }
            }
        }

        private void FireBursts(float fromExclusive, float toInclusive)
        {
            Burst[] bursts = _Emission.Bursts;
            if (bursts == null)
                return;

            for (int b = 0; b < bursts.Length; b++)
            {
                Burst burst = bursts[b];
                // Shuriken semantics: probability 0 never fires. (Inspector-added rows
                // are given proper defaults in OnValidate, not papered over here.)
                float probability = burst.Probability;
                if (probability <= 0f)
                    continue;
                int cycles = Mathf.Max(1, burst.Cycles);
                float interval = Mathf.Max(0.01f, burst.Interval);

                for (int k = 0; k < cycles; k++)
                {
                    float fireTime = burst.Time + k * interval;
                    if (fireTime <= fromExclusive || fireTime > toInclusive)
                        continue;
                    if (probability < 1f && _rng.Next01() > probability)
                        continue;
                    int count = Mathf.RoundToInt(_rng.Range(burst.Count));
                    if (count > 0)
                        SpawnParticles(count);
                }
            }
        }

        private void SpawnParticles(int count)
        {
            EnsureCapacity();

            for (int i = 0; i < count && _count < _Main.MaxParticles; i++)
            {
                ref Particle p = ref _particles[_count++];

                GetSpawnPoint(out Vector2 pos, out Vector2 dir);

                p.Position = pos;
                p.Velocity = dir * _rng.Range(_Main.StartSpeed);
                p.Rotation = _rng.Range(_Main.StartRotation);
                p.AngularVelocity = _RotationOverLifetime.Enabled
                    ? _rng.Range(_RotationOverLifetime.AngularVelocity)
                    : 0f;
                p.Size = Mathf.Max(0f, _rng.Range(_Main.StartSize));
                p.Lifetime = Mathf.Max(0.01f, _rng.Range(_Main.StartLifetime));
                p.Age = 0f;
                p.StartColor = Color.LerpUnclamped(_Main.StartColorA, _Main.StartColorB, _rng.Next01());
                p.StartFrame = _TextureSheetAnimation.Enabled && _TextureSheetAnimation.RandomStartFrame
                    ? _rng.Next01()
                    : 0f;

                p.HasTrail = _Trails.Enabled && (_Trails.Ratio >= 1f || _rng.Next01() < _Trails.Ratio);
                if (p.HasTrail)
                    _trailedCount++;
                p.TrailHead = 0;
                p.TrailCount = 0;
                p.TrailLifetime = p.HasTrail ? p.Lifetime * Mathf.Max(0f, _rng.Range(_Trails.Lifetime)) : 0f;
            }
        }

        private void GetSpawnPoint(out Vector2 pos, out Vector2 dir)
        {
            if (!_Shape.Enabled)
            {
                pos = Vector2.zero;
                dir = Vector2.up;
                return;
            }

            switch (_Shape.ShapeType)
            {
                case ShapeType.Circle:
                {
                    float theta = (_Shape.Rotation + _rng.Next01() * _Shape.Arc) * Mathf.Deg2Rad;
                    dir = new Vector2(Mathf.Cos(theta), Mathf.Sin(theta));
                    float inner = _Shape.Radius * (1f - _Shape.RadiusThickness);
                    float r = Mathf.Sqrt(Mathf.Lerp(inner * inner, _Shape.Radius * _Shape.Radius, _rng.Next01()));
                    pos = dir * r;
                    break;
                }
                case ShapeType.Rect:
                {
                    Rect rect = rectTransform.rect;
                    pos = new Vector2(_rng.Range(rect.xMin, rect.xMax), _rng.Range(rect.yMin, rect.yMax));
                    float up = (90f + _Shape.Rotation) * Mathf.Deg2Rad;
                    dir = new Vector2(Mathf.Cos(up), Mathf.Sin(up));
                    break;
                }
                case ShapeType.Point:
                {
                    float theta = _rng.Next01() * (Mathf.PI * 2f);
                    dir = new Vector2(Mathf.Cos(theta), Mathf.Sin(theta));
                    pos = Vector2.zero;
                    break;
                }
                default: // Cone
                {
                    float dirAngle = (90f + _Shape.Rotation + _rng.Range(-_Shape.Angle, _Shape.Angle)) * Mathf.Deg2Rad;
                    dir = new Vector2(Mathf.Cos(dirAngle), Mathf.Sin(dirAngle));
                    float baseAngle = (_Shape.Rotation) * Mathf.Deg2Rad;
                    Vector2 baseRight = new Vector2(Mathf.Cos(baseAngle), Mathf.Sin(baseAngle));
                    pos = baseRight * ((_rng.Next01() * 2f - 1f) * _Shape.Radius);
                    break;
                }
            }
        }

        /// <summary>
        /// Pulls particles toward a point in this emitter's local space; particles inside
        /// destinationRadius are consumed. Called by <see cref="UIParticleAttractor"/>.
        /// </summary>
        internal void AttractParticles(Vector2 localTarget, float dt, float delaySeconds, float maxSpeed,
            float destinationRadius, UIParticleAttractor.MovementMode movement, Action onAttracted)
        {
            float radiusSqr = destinationRadius * destinationRadius;
            bool copyTrails = _trailPoints != null;
            int trailCap = _trailPointsPerParticle;
            bool changed = false;
            int consumed = 0;

            for (int i = _count - 1; i >= 0; i--)
            {
                ref Particle p = ref _particles[i];
                if (p.Age < delaySeconds)
                    continue;

                if ((localTarget - p.Position).sqrMagnitude <= radiusSqr)
                {
                    if (p.HasTrail)
                        _trailedCount--;
                    _particles[i] = _particles[--_count];
                    if (copyTrails && i != _count)
                        Array.Copy(_trailPoints, _count * trailCap, _trailPoints, i * trailCap, trailCap);
                    changed = true;
                    consumed++;
                    continue;
                }

                // Attraction ramps in over the first half second past the delay; the
                // particle's own motion is damped away as the pull takes over.
                float strength = Mathf.Clamp01((p.Age - delaySeconds) * 2f);
                if (movement == UIParticleAttractor.MovementMode.Smooth)
                    strength *= strength;
                p.Position = Vector2.MoveTowards(p.Position, localTarget, maxSpeed * strength * dt);
                p.Velocity *= 1f - strength * Mathf.Min(1f, dt * 10f);
                changed = true;
            }

            if (changed)
                _meshDirty = true; // flushed once by the updater in late LateUpdate

            // Callbacks run after the loop: a handler is free to call Clear()/Emit() or
            // reconfigure modules without corrupting the swap-remove loop.
            if (onAttracted != null)
            {
                for (int i = 0; i < consumed; i++)
                    onAttracted.Invoke();
            }
        }

        private void CompensateWorldMovement()
        {
            if (_Main.SimulationSpace != SimulationSpace.World)
            {
                _hasPrevWorldPos = false;
                return;
            }

            // Track the emitter in ROOT CANVAS space, not world space. For Screen Space
            // Overlay canvases the WORLD coordinates of every element rescale whenever
            // the game view / screen size or the CanvasScaler factor changes — tracking
            // world positions would read that as a huge fake "movement" and scatter all
            // particles and trails. Canvas-local coordinates are stable across resizes.
            Canvas rootCanvas = canvas != null ? canvas.rootCanvas : null;
            Transform rootTransform = rootCanvas != null ? rootCanvas.transform : null;
            Vector3 trackedPos = rootTransform != null
                ? rootTransform.InverseTransformPoint(rectTransform.position)
                : rectTransform.position;

            if (_hasPrevWorldPos && trackedPos != _prevWorldPos)
            {
                Vector3 rootDelta = trackedPos - _prevWorldPos;
                Vector2 localDelta = rootTransform != null
                    ? (Vector2)rectTransform.InverseTransformVector(rootTransform.TransformVector(rootDelta))
                    : (Vector2)rectTransform.InverseTransformVector(rootDelta);
                _frameMoveDistance = localDelta.magnitude;
                for (int i = 0; i < _count; i++)
                    _particles[i].Position -= localDelta;

                if (_Trails.Enabled && _trailPoints != null)
                {
                    int cap = _trailPointsPerParticle;
                    for (int i = 0; i < _count; i++)
                    {
                        ref Particle p = ref _particles[i];
                        int baseIndex = i * cap;
                        for (int k = 0; k < p.TrailCount; k++)
                            _trailPoints[baseIndex + (p.TrailHead + k) % cap].Position -= localDelta;
                    }
                }
            }

            _prevWorldPos = trackedPos;
            _hasPrevWorldPos = true;
        }

        // ---------- rendering ----------

        /// <summary>
        /// Per-frame geometry goes straight to <see cref="CanvasRenderer.SetMesh"/> from
        /// <see cref="Advance"/> with a compact vertex layout, so a particle update never
        /// dirties the canvas or re-batches unrelated UI. When something else DOES dirty
        /// the vertices (Graphic.color setter, animated tint, sprite change), the rebuild
        /// lands here and re-pushes the current mesh so those changes are never stale.
        /// </summary>
        protected override void UpdateGeometry()
        {
            _meshDirty = false; // this push covers any pending simulation dirt too
            UpdateMesh();
        }

        private void UpdateMesh()
        {
            bool trailsOn = _Trails.Enabled && _trailPoints != null;
            // Vertex budget from the actual trailed population, not every particle —
            // at trails.Ratio 0.25 this is ~4x less vertex memory than the naive bound.
            int worstQuads = _count + (trailsOn ? _trailedCount * _trailPointsPerParticle : 0);
            if (worstQuads == 0)
            {
                // Push an empty submesh instead of CanvasRenderer.Clear(): Clear() also
                // wipes the renderer's material/texture, which would leave the next
                // Emit()/burst invisible until something re-dirtied the material.
                if (_meshes != null && _meshes.Length == 2 && _vertexBuffer.IsCreated)
                {
                    _quadCount = 0;
                    PushMesh();
                }
                else
                {
                    canvasRenderer.Clear();
                    // Clear() wiped the material/texture and the material-dirty flag is
                    // not necessarily set — restore so later geometry renders.
                    if (IsActive())
                        UpdateMaterial();
                }
                return;
            }

            EnsureMeshCapacity(worstQuads);

            if (!ReferenceEquals(_cachedUVSprite, _Sprite))
            {
                _cachedUVSprite = _Sprite;
                _cachedUV = _Sprite != null
                    ? UnityEngine.Sprites.DataUtility.GetOuterUV(_Sprite)
                    : new Vector4(0f, 0f, 1f, 1f);
            }
            Vector4 uv = _cachedUV;
            Color tint = color;

            // Curves and gradients are baked into small LUTs once per build: at particle
            // counts that matter this replaces tens of thousands of managed→native
            // Evaluate calls per frame with array reads (the dominant cost with trails).
            bool useSizeCurve = _SizeOverLifetime.Enabled;
            bool useColorGradient = _ColorOverLifetime.Enabled;
            if (useSizeCurve)
                BakeCurve(_SizeOverLifetime.Size, _sizeLut);
            if (useColorGradient)
                BakeGradient(_ColorOverLifetime.Color, _colorLut);

            bool useSheet = _TextureSheetAnimation.Enabled
                && (_TextureSheetAnimation.NumTilesX > 1 || _TextureSheetAnimation.NumTilesY > 1);
            int tilesX = 0, totalTiles = 0;
            float tileW = 0f, tileH = 0f, sheetCycles = 0f;
            bool randomStartFrame = false;
            int constLastFrame = 0;
            if (useSheet)
            {
                tilesX = _TextureSheetAnimation.NumTilesX;
                totalTiles = tilesX * _TextureSheetAnimation.NumTilesY;
                tileW = (uv.z - uv.x) / tilesX;
                tileH = (uv.w - uv.y) / _TextureSheetAnimation.NumTilesY;
                sheetCycles = _TextureSheetAnimation.CycleCount;
                randomStartFrame = _TextureSheetAnimation.RandomStartFrame;
                constLastFrame = Mathf.CeilToInt(sheetCycles * totalTiles) - 1;
                BakeCurve(_TextureSheetAnimation.FrameOverTime, _frameLut);
            }

            _quadCount = 0;

            if (trailsOn)
                FillTrails(uv, tint, useColorGradient);

            for (int i = 0; i < _count; i++)
            {
                ref Particle p = ref _particles[i];
                float t = p.Age / p.Lifetime;

                float half = p.Size * 0.5f;
                if (useSizeCurve)
                    half *= _sizeLut[LutIndex(t)];
                if (half <= 0f)
                    continue;

                Color c = (Color)p.StartColor * tint;
                if (useColorGradient)
                    c *= _colorLut[LutIndex(t)];
                Color32 c32 = c;

                Vector4 quadUV = uv;
                if (useSheet)
                {
                    // Frame 0 is the sheet's top-left tile, rows top to bottom (Shuriken order).
                    // Clamp the curve and cap the final frame so a curve holding at 1 shows
                    // the LAST tile instead of wrapping back to tile 0.
                    float progress = Mathf.Clamp01(_frameLut[LutIndex(t)]) * sheetCycles + p.StartFrame;
                    int frame = Mathf.FloorToInt(progress * totalTiles);
                    int lastFrame = randomStartFrame
                        ? Mathf.CeilToInt((sheetCycles + p.StartFrame) * totalTiles) - 1
                        : constLastFrame;
                    frame = Mathf.Min(frame, lastFrame) % totalTiles;
                    if (frame < 0)
                        frame = 0;
                    int col = frame % tilesX;
                    int row = frame / tilesX;
                    float u0 = uv.x + col * tileW;
                    float v1 = uv.w - row * tileH;
                    quadUV = new Vector4(u0, v1 - tileH, u0 + tileW, v1);
                }

                float rad = p.Rotation * Mathf.Deg2Rad;
                float cos = Mathf.Cos(rad) * half;
                float sin = Mathf.Sin(rad) * half;
                Vector2 right = new Vector2(cos, sin);
                Vector2 up = new Vector2(-sin, cos);
                Vector2 pos = p.Position;

                int vi = _quadCount * 4;
                ParticleVertex v;
                v.Position = pos - right - up; v.Color = c32; v.Uv = new Vector2(quadUV.x, quadUV.y);
                _vertexBuffer[vi] = v;
                v.Position = pos - right + up; v.Uv = new Vector2(quadUV.x, quadUV.w);
                _vertexBuffer[vi + 1] = v;
                v.Position = pos + right + up; v.Uv = new Vector2(quadUV.z, quadUV.w);
                _vertexBuffer[vi + 2] = v;
                v.Position = pos + right - up; v.Uv = new Vector2(quadUV.z, quadUV.y);
                _vertexBuffer[vi + 3] = v;
                _quadCount++;
            }

            PushMesh();
        }

        private void PushMesh()
        {
            // Flip at most once per frame: intra-frame re-pushes (Emit, dirty rebuild)
            // rewrite THIS frame's mesh, never the one the renderer kept from last frame.
            int frame = UnityEngine.Time.frameCount;
            if (frame != _lastFlipFrame)
            {
                _meshFlip ^= 1;
                _lastFlipFrame = frame;
            }
            Mesh mesh = _meshes[_meshFlip];
            int vertexCount = _quadCount * 4;

            mesh.SetVertexBufferData(_vertexBuffer, 0, 0, vertexCount, 0, MeshFlags);
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, _quadCount * 6)
            {
                bounds = _meshBounds,
                firstVertex = 0,
                vertexCount = vertexCount
            }, MeshFlags);

            canvasRenderer.SetMesh(mesh);
        }

        /// <summary>Fills trail ribbons before the particle quads so they render behind them.
        /// Trails sample the sprite's UV center so they stay solid-colored under one material.</summary>
        private void FillTrails(Vector4 uv, Color tint, bool useColorGradient)
        {
            int cap = _trailPointsPerParticle;
            EnsureTrailScratch(cap + 1);

            var uvCenter = new Vector2((uv.x + uv.z) * 0.5f, (uv.y + uv.w) * 0.5f);
            bool inheritColor = _Trails.InheritParticleColor;
            bool sizeAffectsWidth = _Trails.SizeAffectsWidth;
            BakeCurve(_Trails.WidthOverTrail, _widthLut);
            BakeGradient(_Trails.ColorOverTrail, _trailColorLut);

            for (int i = 0; i < _count; i++)
            {
                ref Particle p = ref _particles[i];
                if (!p.HasTrail || p.TrailCount == 0)
                    continue;

                // Polyline: current position first (head), then history newest → oldest.
                int baseIndex = i * cap;
                int n = 0;
                _trailPos[n++] = p.Position;
                for (int k = p.TrailCount - 1; k >= 0; k--)
                {
                    Vector2 pt = _trailPoints[baseIndex + (p.TrailHead + k) % cap].Position;
                    if ((pt - _trailPos[n - 1]).sqrMagnitude > 0.01f)
                        _trailPos[n++] = pt;
                }
                if (n < 2)
                    continue;

                for (int k = 0; k < n; k++)
                {
                    Vector2 dir = k == 0 ? _trailPos[1] - _trailPos[0]
                        : k == n - 1 ? _trailPos[n - 1] - _trailPos[n - 2]
                        : _trailPos[k + 1] - _trailPos[k - 1];
                    float mag = dir.magnitude;
                    Vector2 d = mag > 1e-5f ? dir / mag : Vector2.up;
                    _trailPerp[k] = new Vector2(-d.y, d.x);
                }

                float t = p.Age / p.Lifetime;
                Color particleColor = (Color)p.StartColor * tint;
                if (useColorGradient)
                    particleColor *= _colorLut[LutIndex(t)];
                float baseWidth = sizeAffectsWidth ? p.Size : 1f;

                for (int k = 0; k < n; k++)
                {
                    float f = (float)k / (n - 1);
                    _trailHalfWidth[k] = 0.5f * baseWidth * Mathf.Max(0f, _widthLut[LutIndex(f)]);
                    Color ck = _trailColorLut[LutIndex(f)];
                    _trailColor[k] = inheritColor ? ck * particleColor : ck * tint;
                }

                for (int k = 0; k < n - 1; k++)
                {
                    Vector2 o0 = _trailPerp[k] * _trailHalfWidth[k];
                    Vector2 o1 = _trailPerp[k + 1] * _trailHalfWidth[k + 1];

                    int vi = _quadCount * 4;
                    ParticleVertex v;
                    v.Position = _trailPos[k] - o0; v.Color = _trailColor[k]; v.Uv = uvCenter;
                    _vertexBuffer[vi] = v;
                    v.Position = _trailPos[k] + o0;
                    _vertexBuffer[vi + 1] = v;
                    v.Position = _trailPos[k + 1] + o1; v.Color = _trailColor[k + 1];
                    _vertexBuffer[vi + 2] = v;
                    v.Position = _trailPos[k + 1] - o1;
                    _vertexBuffer[vi + 3] = v;
                    _quadCount++;
                }
            }
        }

        private void EnsureMeshCapacity(int quads)
        {
            if (_meshes != null && _meshes.Length == 2 && _vertexBuffer.IsCreated && quads <= _quadCapacity)
                return;

            int newQuadCapacity = Mathf.Max(quads, _quadCapacity * 2, 64);
            int vertexCapacity = newQuadCapacity * 4;
            bool use32BitIndices = vertexCapacity > ushort.MaxValue;

            if (_vertexBuffer.IsCreated)
                _vertexBuffer.Dispose();
            _vertexBuffer = new NativeArray<ParticleVertex>(vertexCapacity, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);

            if (_meshes == null || _meshes.Length != 2)
            {
                _meshes = new Mesh[2];
                for (int i = 0; i < 2; i++)
                {
                    _meshes[i] = new Mesh
                    {
                        name = "[UIParticles] " + name,
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    _meshes[i].MarkDynamic();
                }
            }

            // The quad index pattern never changes, so the index buffer is written once
            // per capacity change; per frame only the submesh index count moves.
            int indexCount = newQuadCapacity * 6;
            if (use32BitIndices)
            {
                var indices = new NativeArray<uint>(indexCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                for (int q = 0; q < newQuadCapacity; q++)
                {
                    int ii = q * 6;
                    uint vi = (uint)(q * 4);
                    indices[ii] = vi; indices[ii + 1] = vi + 1; indices[ii + 2] = vi + 2;
                    indices[ii + 3] = vi + 2; indices[ii + 4] = vi + 3; indices[ii + 5] = vi;
                }
                for (int i = 0; i < 2; i++)
                    SetupMesh(_meshes[i], vertexCapacity, indices, IndexFormat.UInt32);
                indices.Dispose();
            }
            else
            {
                var indices = new NativeArray<ushort>(indexCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                for (int q = 0; q < newQuadCapacity; q++)
                {
                    int ii = q * 6;
                    ushort vi = (ushort)(q * 4);
                    indices[ii] = vi; indices[ii + 1] = (ushort)(vi + 1); indices[ii + 2] = (ushort)(vi + 2);
                    indices[ii + 3] = (ushort)(vi + 2); indices[ii + 4] = (ushort)(vi + 3); indices[ii + 5] = vi;
                }
                for (int i = 0; i < 2; i++)
                    SetupMesh(_meshes[i], vertexCapacity, indices, IndexFormat.UInt16);
                indices.Dispose();
            }

            _quadCapacity = newQuadCapacity;
        }

        private static void SetupMesh<TIndex>(Mesh mesh, int vertexCapacity, NativeArray<TIndex> indices, IndexFormat format)
            where TIndex : struct
        {
            mesh.SetVertexBufferParams(vertexCapacity, _vertexLayout);
            mesh.SetIndexBufferParams(indices.Length, format);
            mesh.SetIndexBufferData(indices, 0, 0, indices.Length, MeshFlags);
            mesh.subMeshCount = 1;
            mesh.bounds = _meshBounds;
        }

        private static void EnsureTrailScratch(int size)
        {
            if (_trailPos.Length >= size)
                return;
            _trailPos = new Vector2[size];
            _trailPerp = new Vector2[size];
            _trailHalfWidth = new float[size];
            _trailColor = new Color32[size];
        }

        // ---------- helpers ----------

        private void EnsureCapacity()
        {
            // [Min] attributes only guard the inspector; clamp the scripting path too.
            int maxParticles = Mathf.Max(1, _Main.MaxParticles);

            if (_particles == null || _particles.Length != maxParticles)
            {
                var next = new Particle[maxParticles];
                if (_particles != null && _count > 0)
                {
                    _count = Mathf.Min(_count, maxParticles);
                    Array.Copy(_particles, next, _count);
                    // Truncation may have dropped trailed particles — recount.
                    _trailedCount = 0;
                    for (int i = 0; i < _count; i++)
                    {
                        if (next[i].HasTrail)
                            _trailedCount++;
                    }
                }
                _particles = next;
            }

            if (_Trails.Enabled)
            {
                int cap = Mathf.Clamp(_Trails.MaxPoints, 2, 32);
                int needed = maxParticles * cap;
                if (_trailPoints == null || _trailPoints.Length != needed || _trailPointsPerParticle != cap)
                {
                    _trailPoints = new TrailPoint[needed];
                    _trailPointsPerParticle = cap;
                    // Buffer layout changed — existing trails restart from scratch.
                    for (int i = 0; i < _count; i++)
                    {
                        _particles[i].TrailHead = 0;
                        _particles[i].TrailCount = 0;
                    }
                }
            }
        }

        private void InitRng()
        {
            // With autoRandomSeed off the stored seed is authoritative — even 0
            // (XorShift32 maps 0 to a fixed valid state), so replays reproduce.
            uint seed = !_Main.AutoRandomSeed
                ? _Main.RandomSeed
                : (uint)Environment.TickCount ^ (uint)GetInstanceID();
            _rng = new XorShift32(seed);
            _rngInitialized = true;
        }

        private static Gradient DefaultFadeGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            return gradient;
        }

#if UNITY_EDITOR
        protected override void Reset()
        {
            base.Reset();
            raycastTarget = false;
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            _Main.Duration = Mathf.Max(0.05f, _Main.Duration);
            _Main.MaxParticles = Mathf.Max(1, _Main.MaxParticles);
            _Main.SimulationSpeed = Mathf.Max(0f, _Main.SimulationSpeed);

            Burst[] bursts = _Emission.Bursts;
            if (bursts != null)
            {
                for (int i = 0; i < bursts.Length; i++)
                {
                    ref Burst b = ref bursts[i];
                    // A row freshly added with the inspector's "+" is all zeros, which
                    // would silently never fire — give it Shuriken's defaults instead.
                    if (b.Time == 0f && b.Count.Min == 0f && b.Count.Max == 0f
                        && b.Cycles == 0 && b.Interval == 0f && b.Probability == 0f)
                    {
                        b = new Burst(0f, 30f);
                    }
                }
            }
        }
#endif
    }
}
