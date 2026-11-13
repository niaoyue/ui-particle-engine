using System;
using NUnit.Framework;
using Unity.Profiling;
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
                using (var recorder = new ProfilerRecorder(ProfilerCategory.Memory, "GC.Alloc", 4096,
                    ProfilerRecorderOptions.CollectOnlyOnCurrentThread))
                {
                    Assert.IsTrue(recorder.Valid, "GC.Alloc recording must be available.");
                    recorder.Start();
                    var calibration = new byte[4096];
                    GC.KeepAlive(calibration);
                    recorder.Stop();
                    Assert.Greater(recorder.Count, 0, "An intentional allocation must be recorded before trusting zero samples.");
                    recorder.Reset();
                    recorder.Start();
                    for (int i = 0; i < samples; i++)
                        emitter.Simulate(step);
                    recorder.Stop();

                    Assert.AreEqual(count, emitter.ParticleCount);
                    TestContext.WriteLine($"particles={count}, trails={trails}, samples={samples}, allocationEvents={recorder.Count}");
                    Assert.AreEqual(0, recorder.Count, "Only the warmed Simulate + mesh submission path is measured.");
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
