using System.Collections.Generic;
using NUnit.Framework;
using UIParticleEngine.Editor;
using UnityEngine;

namespace UIParticleEngine.Tests
{
    /// <summary>Shuriken -> UIParticleEmitter migration mapping tests.</summary>
    public class UIParticleMigratorTests
    {
        private readonly List<GameObject> _cleanup = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _cleanup)
                if (go != null)
                    Object.DestroyImmediate(go);
            _cleanup.Clear();
        }

        private ParticleSystem CreateSystem()
        {
            var go = new GameObject("SourcePS", typeof(ParticleSystem));
            _cleanup.Add(go);
            return go.GetComponent<ParticleSystem>();
        }

        private UIParticleEmitter CreateEmitter()
        {
            var go = new GameObject("Emitter", typeof(RectTransform), typeof(CanvasRenderer));
            _cleanup.Add(go);
            return go.AddComponent<UIParticleEmitter>();
        }

        [Test]
        public void MigratesMainModuleWithUnitScaling()
        {
            ParticleSystem ps = CreateSystem();
            ParticleSystem.MainModule psMain = ps.main;
            psMain.duration = 3f;
            psMain.loop = false;
            psMain.startLifetime = new ParticleSystem.MinMaxCurve(1f, 2f);
            psMain.startSpeed = 2f;
            psMain.startSize = 0.5f;
            psMain.startColor = new ParticleSystem.MinMaxGradient(Color.red, Color.blue);
            psMain.gravityModifier = 1f;
            psMain.simulationSpace = ParticleSystemSimulationSpace.World;
            psMain.maxParticles = 77;

            UIParticleEmitter emitter = CreateEmitter();
            UIParticleMigrator.MigrateSystem(ps, emitter, 100f, null);

            Assert.AreEqual(3f, emitter.Main.Duration);
            Assert.IsFalse(emitter.Main.Looping);
            Assert.AreEqual(1f, emitter.Main.StartLifetime.Min, 1e-4f);
            Assert.AreEqual(2f, emitter.Main.StartLifetime.Max, 1e-4f);
            Assert.AreEqual(200f, emitter.Main.StartSpeed.Min, 1e-3f);
            Assert.AreEqual(50f, emitter.Main.StartSize.Min, 1e-3f);
            Assert.AreEqual(Color.red, emitter.Main.StartColorA);
            Assert.AreEqual(Color.blue, emitter.Main.StartColorB);
            Assert.AreEqual(1f, emitter.Main.GravityModifier, 1e-4f);
            Assert.AreEqual(UIParticleEmitter.SimulationSpace.World, emitter.Main.SimulationSpace);
            Assert.AreEqual(77, emitter.Main.MaxParticles);
        }

        [Test]
        public void MigratesEmissionShapeAndOverLifetimeModules()
        {
            ParticleSystem ps = CreateSystem();
            ParticleSystem.EmissionModule psEmission = ps.emission;
            psEmission.rateOverTime = 30f;
            psEmission.SetBursts(new[] { new ParticleSystem.Burst(0.5f, 10, 20, 2, 0.2f) });

            ParticleSystem.ShapeModule psShape = ps.shape;
            psShape.shapeType = ParticleSystemShapeType.Cone;
            psShape.angle = 30f;
            psShape.radius = 0.25f;

            ParticleSystem.RotationOverLifetimeModule psRot = ps.rotationOverLifetime;
            psRot.enabled = true;
            psRot.z = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);

            ParticleSystem.TrailModule psTrails = ps.trails;
            psTrails.enabled = true;
            psTrails.lifetime = 0.5f;
            psTrails.minVertexDistance = 0.05f;

            UIParticleEmitter emitter = CreateEmitter();
            UIParticleMigrator.MigrateSystem(ps, emitter, 100f, null);

            Assert.AreEqual(30f, emitter.Emission.RateOverTime, 1e-3f);
            Assert.AreEqual(1, emitter.Emission.Bursts.Length);
            Assert.AreEqual(0.5f, emitter.Emission.Bursts[0].Time, 1e-4f);
            Assert.AreEqual(10f, emitter.Emission.Bursts[0].Count.Min, 1e-3f);
            Assert.AreEqual(20f, emitter.Emission.Bursts[0].Count.Max, 1e-3f);
            Assert.AreEqual(2, emitter.Emission.Bursts[0].Cycles);

            Assert.AreEqual(UIParticleEmitter.ShapeType.Cone, emitter.Shape.ShapeType);
            Assert.AreEqual(30f, emitter.Shape.Angle, 1e-3f);
            Assert.AreEqual(25f, emitter.Shape.Radius, 1e-3f);

            Assert.IsTrue(emitter.RotationOverLifetime.Enabled);
            Assert.AreEqual(-180f, emitter.RotationOverLifetime.AngularVelocity.Min, 1e-2f);
            Assert.AreEqual(180f, emitter.RotationOverLifetime.AngularVelocity.Max, 1e-2f);

            Assert.IsTrue(emitter.Trails.Enabled);
            Assert.AreEqual(0.5f, emitter.Trails.Lifetime.Min, 1e-4f);
            Assert.AreEqual(5f, emitter.Trails.MinVertexDistance, 1e-3f);
        }

        [Test]
        public void UnsupportedFeaturesLandInReport()
        {
            ParticleSystem ps = CreateSystem();
            ParticleSystem.ShapeModule psShape = ps.shape;
            psShape.shapeType = ParticleSystemShapeType.Mesh;
            ParticleSystem.NoiseModule psNoise = ps.noise;
            psNoise.enabled = true;

            UIParticleEmitter emitter = CreateEmitter();
            var report = new List<string>();
            UIParticleMigrator.MigrateSystem(ps, emitter, 100f, report);

            Assert.AreEqual(UIParticleEmitter.ShapeType.Point, emitter.Shape.ShapeType);
            Assert.IsTrue(report.Exists(l => l.Contains("Mesh")), "shape fallback should be reported");
            Assert.IsTrue(report.Exists(l => l.Contains("noise")), "noise skip should be reported");
        }

        [Test]
        public void MigrateHierarchyCreatesEmitterPerSystemWithScaledPositions()
        {
            var root = new GameObject("FXRoot");
            _cleanup.Add(root);
            var childA = new GameObject("Burst", typeof(ParticleSystem));
            childA.transform.SetParent(root.transform, false);
            childA.transform.localPosition = new Vector3(1f, 2f, 0f);
            var childB = new GameObject("Glow", typeof(ParticleSystem));
            childB.transform.SetParent(childA.transform, false);
            childB.transform.localPosition = new Vector3(0f, -0.5f, 0f);

            GameObject result = UIParticleMigrator.MigrateHierarchy(root, 100f, null);
            _cleanup.Add(result);

            UIParticleEmitter[] emitters = result.GetComponentsInChildren<UIParticleEmitter>();
            Assert.AreEqual(2, emitters.Length);

            var burst = result.transform.Find("Burst") as RectTransform;
            var glow = result.transform.Find("Glow") as RectTransform;
            Assert.IsNotNull(burst);
            Assert.IsNotNull(glow);
            Assert.AreEqual(new Vector2(100f, 200f), burst.anchoredPosition);
            Assert.AreEqual(new Vector2(100f, 150f), glow.anchoredPosition);
        }
    }
}
