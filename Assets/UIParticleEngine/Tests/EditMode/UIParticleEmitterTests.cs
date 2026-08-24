using NUnit.Framework;
using UnityEngine;

namespace UIParticleEngine.Tests
{
    public class CoreTests
    {
        [Test]
        public void XorShift_SameSeed_SameSequence()
        {
            var a = new XorShift32(1234);
            var b = new XorShift32(1234);
            for (int i = 0; i < 100; i++)
                Assert.AreEqual(a.NextUInt(), b.NextUInt());
        }

        [Test]
        public void XorShift_Next01_StaysInRange()
        {
            var rng = new XorShift32(42);
            for (int i = 0; i < 10000; i++)
            {
                float v = rng.Next01();
                Assert.GreaterOrEqual(v, 0f);
                Assert.Less(v, 1f);
            }
        }

        [Test]
        public void XorShift_ZeroSeed_IsValid()
        {
            var rng = new XorShift32(0);
            Assert.AreNotEqual(0u, rng.NextUInt());
        }

        [Test]
        public void RangeF_Evaluate_LerpsBetweenMinMax()
        {
            var range = new RangeF(10f, 20f);
            Assert.AreEqual(10f, range.Evaluate(0f));
            Assert.AreEqual(20f, range.Evaluate(1f));
            Assert.AreEqual(15f, range.Evaluate(0.5f));
        }

        [Test]
        public void RangeF_ImplicitFromFloat_IsConstant()
        {
            RangeF range = 7f;
            Assert.AreEqual(7f, range.Evaluate(0f));
            Assert.AreEqual(7f, range.Evaluate(1f));
        }
    }

    public class UIParticleEmitterTests
    {
        private GameObject _canvasGo;
        private UIParticleEmitter _emitter;

        [SetUp]
        public void SetUp()
        {
            _canvasGo = new GameObject("TestCanvas", typeof(Canvas));
            var go = new GameObject("Emitter", typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(_canvasGo.transform, false);
            _emitter = go.AddComponent<UIParticleEmitter>();

            // Deterministic, quiet baseline: tests opt in to the behavior they check.
            _emitter.Main.AutoRandomSeed = false;
            _emitter.Main.RandomSeed = 12345;
            _emitter.Main.StartLifetime = 100f;
            _emitter.Main.StartSpeed = 0f;
            _emitter.Main.MaxParticles = 1000;
            _emitter.Emission.RateOverTime = 0f;
            _emitter.Shape.Enabled = false;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_canvasGo);
        }

        private void Simulate(float total, float step)
        {
            for (float t = 0f; t < total - step * 0.5f; t += step)
                _emitter.Simulate(step);
        }

        [Test]
        public void Emit_SpawnsExactCount()
        {
            _emitter.Emit(37);
            Assert.AreEqual(37, _emitter.ParticleCount);
        }

        [Test]
        public void Simulate_LargeDelta_AdvancesFullTimeAndKeepsEmissionRate()
        {
            // Large manual steps are internally subdivided (hitch-frame protection) but
            // must still advance the system by the full requested time.
            _emitter.Main.StartLifetime = 100f;
            _emitter.Emission.RateOverTime = 40f;
            _emitter.Play();
            _emitter.Simulate(2f);
            Assert.AreEqual(2f, _emitter.Time, 1e-3f);
            Assert.AreEqual(80, _emitter.ParticleCount, 1);
        }

        [Test]
        public void MaxParticles_CapsSpawns()
        {
            _emitter.Main.MaxParticles = 100;
            _emitter.Emit(500);
            Assert.AreEqual(100, _emitter.ParticleCount);
        }

        [Test]
        public void RateOverTime_EmitsExpectedCount()
        {
            _emitter.Emission.RateOverTime = 100f;
            _emitter.Play();
            Simulate(1f, 1f / 60f);
            Assert.GreaterOrEqual(_emitter.ParticleCount, 98);
            Assert.LessOrEqual(_emitter.ParticleCount, 101);
        }

        [Test]
        public void Burst_AtTimeZero_FiresOnFirstFrame()
        {
            _emitter.Emission.Bursts = new[] { new UIParticleEmitter.Burst(0f, 25f) };
            _emitter.Play();
            _emitter.Simulate(0.001f);
            Assert.AreEqual(25, _emitter.ParticleCount);
        }

        [Test]
        public void Burst_FiresOncePerLoopCycle()
        {
            _emitter.Main.Duration = 0.5f;
            _emitter.Main.Looping = true;
            _emitter.Emission.Bursts = new[] { new UIParticleEmitter.Burst(0f, 10f) };
            _emitter.Play();
            Simulate(1.625f, 0.025f); // cycles start at t=0, 0.5, 1.0, 1.5
            Assert.AreEqual(40, _emitter.ParticleCount);
        }

        [Test]
        public void Burst_LargeDeltaSpanningMultipleCycles_FiresEachCycleStart()
        {
            _emitter.Main.Duration = 0.2f;
            _emitter.Emission.Bursts = new[] { new UIParticleEmitter.Burst(0f, 5f) };
            _emitter.Play();
            _emitter.Simulate(0.65f); // spans cycle starts at 0, 0.2, 0.4, 0.6
            Assert.AreEqual(20, _emitter.ParticleCount);
            Assert.Less(_emitter.Time, 0.2f, "cycle time must wrap back inside [0, duration)");
        }

        [Test]
        public void Burst_CyclesAndInterval_RepeatWithinCycle()
        {
            _emitter.Main.Duration = 5f;
            _emitter.Emission.Bursts = new[] { new UIParticleEmitter.Burst(0.1f, 5f, cycles: 3, interval: 0.2f) };
            _emitter.Play();
            Simulate(1f, 0.01f); // fires at 0.1, 0.3, 0.5
            Assert.AreEqual(15, _emitter.ParticleCount);
        }

        [Test]
        public void StartDelay_SuppressesEmissionUntilElapsed()
        {
            _emitter.Main.StartDelay = 0.5f;
            _emitter.Emission.Bursts = new[] { new UIParticleEmitter.Burst(0f, 10f) };
            _emitter.Play();
            Simulate(0.4f, 0.02f);
            Assert.AreEqual(0, _emitter.ParticleCount, "burst fired before startDelay elapsed");
            Simulate(0.2f, 0.02f);
            Assert.AreEqual(10, _emitter.ParticleCount);
        }

        [Test]
        public void Lifetime_ExpiredParticlesAreRemoved()
        {
            _emitter.Main.StartLifetime = 0.2f;
            _emitter.Emit(50);
            Assert.AreEqual(50, _emitter.ParticleCount);
            Simulate(0.3f, 0.02f);
            Assert.AreEqual(0, _emitter.ParticleCount);
        }

        [Test]
        public void NonLooping_StopsEmittingAfterDuration()
        {
            _emitter.Main.Duration = 0.3f;
            _emitter.Main.Looping = false;
            _emitter.Main.StartLifetime = 0.05f;
            _emitter.Emission.RateOverTime = 100f;
            _emitter.Play();
            Simulate(1f, 1f / 60f);
            Assert.IsTrue(_emitter.IsStopped);
            Assert.AreEqual(0, _emitter.ParticleCount);
            Assert.IsFalse(_emitter.IsAlive());
        }

        [Test]
        public void Stop_WithClear_RemovesParticlesImmediately()
        {
            _emitter.Emit(30);
            _emitter.Play();
            _emitter.Stop(UIParticleEmitter.StopBehavior.StopEmittingAndClear);
            Assert.AreEqual(0, _emitter.ParticleCount);
            Assert.IsTrue(_emitter.IsStopped);
        }

        [Test]
        public void Pause_FreezesSimulation()
        {
            _emitter.Main.StartLifetime = 0.1f;
            _emitter.Emit(10);
            _emitter.Play();
            _emitter.Pause();
            Assert.IsTrue(_emitter.IsPaused);
            // Advance is the updater entry point and must respect pause;
            // Simulate is the explicit manual override and must not.
            _emitter.Advance(1f, 1f);
            Assert.AreEqual(10, _emitter.ParticleCount);
        }

        [Test]
        public void Prewarm_PopulatesOnPlay()
        {
            _emitter.Main.Prewarm = true;
            _emitter.Main.Looping = true;
            _emitter.Main.StartLifetime = 100f;
            _emitter.Emission.RateOverTime = 20f;
            _emitter.Play();
            Assert.GreaterOrEqual(_emitter.ParticleCount, 90, "prewarm should simulate one full duration (5s * 20/s)");
        }

        [Test]
        public void SeededEmitters_ProduceIdenticalCounts()
        {
            var otherGo = new GameObject("Emitter2", typeof(RectTransform), typeof(CanvasRenderer));
            otherGo.transform.SetParent(_canvasGo.transform, false);
            var other = otherGo.AddComponent<UIParticleEmitter>();
            other.Main.AutoRandomSeed = false;
            other.Main.RandomSeed = 12345;
            other.Main.StartLifetime = new RangeF(0.1f, 0.5f);
            other.Emission.RateOverTime = 60f;
            other.Shape.Enabled = false;

            _emitter.Main.StartLifetime = new RangeF(0.1f, 0.5f);
            _emitter.Emission.RateOverTime = 60f;

            _emitter.Play();
            other.Play();
            for (int i = 0; i < 120; i++)
            {
                _emitter.Simulate(1f / 60f);
                other.Simulate(1f / 60f);
                Assert.AreEqual(_emitter.ParticleCount, other.ParticleCount, $"diverged at step {i}");
            }
        }

        [Test]
        public void Attractor_ConsumesParticlesInRadius_AndFiresPerParticle()
        {
            _emitter.Emit(20);
            int attracted = 0;
            _emitter.AttractParticles(Vector2.zero, 0.016f, delaySeconds: 0f, maxSpeed: 1000f,
                destinationRadius: 50f, UIParticleAttractor.MovementMode.Smooth, () => attracted++);
            Assert.AreEqual(0, _emitter.ParticleCount);
            Assert.AreEqual(20, attracted);
        }

        [Test]
        public void Attractor_RespectsDelay()
        {
            _emitter.Emit(20);
            int attracted = 0;
            _emitter.AttractParticles(Vector2.zero, 0.016f, delaySeconds: 5f, maxSpeed: 1000f,
                destinationRadius: 50f, UIParticleAttractor.MovementMode.Smooth, () => attracted++);
            Assert.AreEqual(20, _emitter.ParticleCount, "particles younger than the delay must not be attracted");
            Assert.AreEqual(0, attracted);
        }

        [Test]
        public void TrailsAndTextureSheet_SimulateWithoutErrors()
        {
            _emitter.Trails.Enabled = true;
            _emitter.Trails.Lifetime = 0.5f;
            _emitter.TextureSheetAnimation.Enabled = true;
            _emitter.TextureSheetAnimation.NumTilesX = 4;
            _emitter.TextureSheetAnimation.NumTilesY = 4;
            _emitter.Main.StartSpeed = 100f;
            _emitter.Main.StartLifetime = 0.4f;
            _emitter.Emission.RateOverTime = 60f;
            _emitter.Play();
            Simulate(1f, 1f / 60f);
            Assert.Greater(_emitter.ParticleCount, 0);
        }

        [Test]
        public void NonLooping_DoesNotOverEmitOnLargeDelta()
        {
            _emitter.Main.Duration = 0.3f;
            _emitter.Main.Looping = false;
            _emitter.Emission.RateOverTime = 100f;
            _emitter.Play();
            _emitter.Simulate(1f); // one huge step spanning far past the duration
            Assert.LessOrEqual(_emitter.ParticleCount, 31, "rate must be clamped to the cycle window");
            Assert.GreaterOrEqual(_emitter.ParticleCount, 29);
            Assert.IsTrue(_emitter.IsStopped);
        }

        [Test]
        public void Burst_RepeatsPastCycleEnd_DoNotFire()
        {
            _emitter.Main.Duration = 0.5f;
            // Repeats at 0.4 / 0.6 / 0.8 — only the 0.4 one lies inside the cycle.
            _emitter.Emission.Bursts = new[] { new UIParticleEmitter.Burst(0.4f, 5f, cycles: 3, interval: 0.2f) };
            _emitter.Play();
            Simulate(2f, 0.016f); // cycles at 0, 0.5, 1.0, 1.5 -> 4 in-cycle fires
            Assert.AreEqual(20, _emitter.ParticleCount);
        }

        [Test]
        public void Burst_ProbabilityZero_NeverFires()
        {
            _emitter.Emission.Bursts = new[] { new UIParticleEmitter.Burst(0f, 10f, probability: 0f) };
            _emitter.Play();
            Simulate(1f, 0.02f);
            Assert.AreEqual(0, _emitter.ParticleCount, "probability 0 must never fire (Shuriken semantics)");
        }

        [Test]
        public void Pause_WorksOnStoppedSystemWithAliveParticles()
        {
            _emitter.Main.StartLifetime = 0.1f;
            _emitter.Emit(10);
            Assert.IsTrue(_emitter.IsStopped);
            _emitter.Pause();
            Assert.IsTrue(_emitter.IsPaused);
            _emitter.Advance(1f, 1f);
            Assert.AreEqual(10, _emitter.ParticleCount, "paused particles must not age even when stopped");
        }

        [Test]
        public void FixedSeed_ReproducesAcrossRestarts()
        {
            _emitter.Main.StartLifetime = new RangeF(0.1f, 0.5f);
            _emitter.Emission.RateOverTime = 60f;

            _emitter.Play();
            Simulate(1f, 1f / 60f);
            int firstRun = _emitter.ParticleCount;

            _emitter.Stop(UIParticleEmitter.StopBehavior.StopEmittingAndClear);
            _emitter.Play();
            Simulate(1f, 1f / 60f);
            Assert.AreEqual(firstRun, _emitter.ParticleCount, "same seed must reproduce after restart");
        }

        [Test]
        public void Emit_WorksWithoutPlay()
        {
            Assert.IsTrue(_emitter.IsStopped);
            _emitter.Emit(5);
            Assert.AreEqual(5, _emitter.ParticleCount);
            Assert.IsTrue(_emitter.IsAlive());
        }
    }
}
