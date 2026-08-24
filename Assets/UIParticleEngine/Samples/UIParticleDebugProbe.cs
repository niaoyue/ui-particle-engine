using System.Text;
using UnityEngine;

namespace UIParticleEngine.Samples
{
    /// <summary>Static state dump for driving/diagnosing the engine from the editor bridge.</summary>
    public static class UIParticleDebugProbe
    {
        public static string Dump()
        {
            var sb = new StringBuilder();
            sb.Append("updaterDriver=").Append(UIParticleUpdater.DriverAlive)
              .Append(" registered=").Append(UIParticleUpdater.RegisteredCount).Append('\n');

            UIParticleEmitter[] emitters = Object.FindObjectsByType<UIParticleEmitter>(FindObjectsSortMode.None);
            sb.Append("emitters=").Append(emitters.Length).Append('\n');
            foreach (UIParticleEmitter e in emitters)
            {
                RectTransform rt = (RectTransform)e.transform;
                sb.Append(e.name)
                  .Append(" enabled=").Append(e.enabled)
                  .Append(" active=").Append(e.gameObject.activeInHierarchy)
                  .Append(" isPlaying=").Append(e.IsPlaying)
                  .Append(" particles=").Append(e.ParticleCount)
                  .Append(" cull=").Append(e.canvasRenderer.cull)
                  .Append(" canvas=").Append(e.canvas ? e.canvas.name : "NULL")
                  .Append(" mat=").Append(e.materialForRendering ? e.materialForRendering.shader.name : "NULL")
                  .Append(" tex=").Append(e.mainTexture ? e.mainTexture.name : "NULL")
                  .Append(" pos=").Append(rt.anchoredPosition.ToString("F0"))
                  .Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>Bridge test hook: triggers the coin-collect demo as if the button was clicked.</summary>
        public static string TriggerCoinCollect()
        {
            var demo = Object.FindFirstObjectByType<UIParticleCoinReward>();
            if (demo == null)
                return "no UIParticleCoinReward in scene";
            demo.Collect();
            return "ok";
        }

        /// <summary>
        /// Bridge test hook: raycasts the UI at the named object's screen position and
        /// returns the topmost hit, i.e. what would actually receive a click there.
        /// </summary>
        public static string RaycastAt(string objectName)
        {
            var target = GameObject.Find(objectName);
            if (target == null)
                return $"no object named '{objectName}'";
            var eventSystem = UnityEngine.EventSystems.EventSystem.current;
            if (eventSystem == null)
                return "no EventSystem";

            var rt = target.transform as RectTransform;
            Vector2 screenPos = rt != null
                ? RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center))
                : (Vector2)Camera.main.WorldToScreenPoint(target.transform.position);

            var pointer = new UnityEngine.EventSystems.PointerEventData(eventSystem) { position = screenPos };
            var hits = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
            eventSystem.RaycastAll(pointer, hits);
            if (hits.Count == 0)
                return $"screenPos={screenPos:F0} hits=0";

            var sb = new StringBuilder();
            sb.Append("screenPos=").Append(screenPos.ToString("F0")).Append(" top=").Append(hits[0].gameObject.name);
            for (int i = 1; i < hits.Count; i++)
                sb.Append(" | ").Append(hits[i].gameObject.name);
            return sb.ToString();
        }

        /// <summary>Bridge test hook: starts the firework show.</summary>
        public static string TriggerFireworks()
        {
            var demo = Object.FindFirstObjectByType<UIParticleFireworkShow>();
            if (demo == null)
                return "no UIParticleFireworkShow in scene";
            demo.Fire();
            return "ok";
        }

        /// <summary>Bridge capture hook: deactivates the named objects (semicolon-separated).</summary>
        public static string Deactivate(string names)
        {
            var sb = new StringBuilder();
            foreach (string name in names.Split(';'))
            {
                var go = GameObject.Find(name.Trim());
                if (go != null)
                {
                    go.SetActive(false);
                    sb.Append(name.Trim()).Append(" off; ");
                }
                else
                {
                    sb.Append(name.Trim()).Append(" NOT FOUND; ");
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Bridge capture hook: schedules a coin collect on the main thread after a delay,
        /// so bursts can land inside an already-running capture_sequence.
        /// </summary>
        public static string TriggerCoinCollectAfter(float seconds)
        {
            var ctx = System.Threading.SynchronizationContext.Current;
            if (ctx == null)
                return "no synchronization context";
            System.Threading.Tasks.Task.Delay((int)(seconds * 1000f))
                .ContinueWith(_ => ctx.Post(__ => TriggerCoinCollect(), null));
            return "scheduled";
        }

        /// <summary>Bridge capture hook: moves the named RectTransform to an anchored position.</summary>
        public static string MoveTo(string name, float x, float y)
        {
            var go = GameObject.Find(name);
            if (go == null)
                return $"no object named '{name}'";
            var rt = go.transform as RectTransform;
            if (rt == null)
                return $"'{name}' has no RectTransform";
            rt.anchoredPosition = new Vector2(x, y);
            return "ok";
        }

        /// <summary>Bridge test hook: fires the confetti demo.</summary>
        public static string TriggerConfetti()
        {
            var demo = Object.FindFirstObjectByType<UIParticleConfettiDemo>();
            if (demo == null)
                return "no UIParticleConfettiDemo in scene";
            demo.Fire();
            return "ok";
        }

        /// <summary>
        /// Spawns a throwaway emitter with <paramref name="count"/> long-lived particles and
        /// measures simulation cost and full canvas rebuild cost per frame, averaged over
        /// <paramref name="steps"/> iterations. Runtime-only; the emitter is destroyed after.
        /// </summary>
        public static string StressTest(int count, int steps, bool withTrails)
        {
            Canvas canvas = Object.FindFirstObjectByType<Canvas>();
            if (canvas == null)
                return "no canvas in scene";

            var go = new GameObject("StressEmitter", typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(canvas.transform, false);
            var e = go.AddComponent<UIParticleEmitter>();
            try
            {
                e.raycastTarget = false; // Reset() only runs in the editor UI path
                e.Stop(UIParticleEmitter.StopBehavior.StopEmittingAndClear);
                e.Emission.Enabled = false;
                e.Main.MaxParticles = count;
                e.Main.StartLifetime = new RangeF(9999f, 9999f);
                e.Main.StartSpeed = new RangeF(20f, 120f);
                e.Main.GravityModifier = 0.05f;
                e.RotationOverLifetime.Enabled = true;
                e.SizeOverLifetime.Enabled = true;
                e.ColorOverLifetime.Enabled = true;
                if (withTrails)
                {
                    e.Trails.Enabled = true;
                    e.Trails.Lifetime = new RangeF(1f, 1f);
                    e.Trails.MinVertexDistance = 2f;
                    e.Trails.MaxPoints = 12;
                }
                e.Emit(count);

                // Warm-up (builds trail history, JIT, caches).
                for (int i = 0; i < 30; i++)
                    e.Advance(1f / 60f, 1f / 60f);

                var swSim = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < steps; i++)
                    e.Advance(1f / 60f, 1f / 60f);
                swSim.Stop();

                var swMesh = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < steps; i++)
                {
                    e.SetVerticesDirty();
                    Canvas.ForceUpdateCanvases();
                }
                swMesh.Stop();

                long allocBefore = System.GC.GetTotalMemory(false);
                for (int i = 0; i < 60; i++)
                    e.Advance(1f / 60f, 1f / 60f);
                long allocDelta = System.GC.GetTotalMemory(false) - allocBefore;

                return $"particles={e.ParticleCount} trails={withTrails} " +
                       $"simMsPerFrame={swSim.Elapsed.TotalMilliseconds / steps:F3} " +
                       $"canvasRebuildMsPerFrame={swMesh.Elapsed.TotalMilliseconds / steps:F3} " +
                       $"gcBytesPer60Frames={allocDelta}";
            }
            finally
            {
                Object.Destroy(go);
            }
        }
    }
}
