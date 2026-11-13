using System;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UIParticleEngine.Tests
{
    public class UIParticleAllocationTests
    {
        [TestCase(500, false)]
        [TestCase(2000, false)]
        [TestCase(500, true)]
        public void WarmSimulationAndMeshSubmissionDoNotAllocate(int count, bool trails)
        {
            var root = new GameObject("Allocation canvas", typeof(Canvas));
            try
            {
                var go = new GameObject("Particles", typeof(RectTransform));
                go.transform.SetParent(root.transform, false);
                var emitter = go.AddComponent<UIParticleEmitter>();
                emitter.Main.MaxParticles = count;
                emitter.Main.StartLifetime = 100f;
                emitter.Main.StartSpeed = 50f;
                emitter.Emission.RateOverTime = 0f;
                emitter.Trails.Enabled = trails;
                emitter.Trails.MaxPoints = 12;
                emitter.Emit(count);
                const float step = 1f / 60f;
                for (int i = 0; i < 32; i++)
                    emitter.Simulate(step);

                const int samples = 120;
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < samples; i++)
                    emitter.Simulate(step);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

                Assert.AreEqual(count, emitter.ParticleCount);
                TestContext.WriteLine($"particles={count}, trails={trails}, samples={samples}, allocatedBytes={allocated}");
                Assert.AreEqual(0L, allocated, "Only the warmed Simulate + mesh submission path is measured.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
