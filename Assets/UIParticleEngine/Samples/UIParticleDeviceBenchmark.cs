using System.Collections;
using UnityEngine;

namespace UIParticleEngine.Samples
{
    /// <summary>
    /// On-device performance probe: a few seconds after launch it runs the stress
    /// scenarios and prints results to the log (adb logcat -s Unity) with a
    /// [UIPE-BENCH] prefix. Editor play mode is skipped — use the debug probe there.
    /// </summary>
    public class UIParticleDeviceBenchmark : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float _StartDelay = 5f;

        private IEnumerator Start()
        {
            if (Application.isEditor)
                yield break;

            // Uncap the frame rate so the live sample measures real headroom, not the
            // Android default 30fps cap.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 300;

            yield return new WaitForSeconds(_StartDelay);
            Debug.Log($"[UIPE-BENCH] device={SystemInfo.deviceModel} cpu={SystemInfo.processorType} " +
                      $"gpu={SystemInfo.graphicsDeviceName} screen={Screen.width}x{Screen.height}@{Screen.currentResolution.refreshRateRatio}");

            // (count, steps, withTrails)
            (int, int, bool)[] scenarios =
            {
                (500, 120, false),
                (2000, 120, false),
                (8000, 120, false),
                (500, 120, true),
                (1000, 120, true),
            };

            foreach ((int count, int steps, bool trails) in scenarios)
            {
                // A frame between scenarios lets the destroyed stress emitter clean up.
                yield return null;
                string result = UIParticleDebugProbe.StressTest(count, steps, trails);
                Debug.Log($"[UIPE-BENCH] {result}");
                yield return null;
            }

            // Real frame-time sample: 8k alive particles on screen for ~3 seconds.
            yield return FrameTimeSample(8000);
            Debug.Log("[UIPE-BENCH] done");
        }

        private void Update()
        {
            // Hitch reporter: surfaces long frames in logcat so device stutters can be
            // correlated with what the demo was doing (button presses, bursts).
            float dt = Time.unscaledDeltaTime;
            if (!Application.isEditor && dt > 0.05f && Time.timeSinceLevelLoad > 1f)
                Debug.Log($"[UIPE-SPIKE] frame={Time.frameCount} dtMs={dt * 1000f:F1}");
        }

        private IEnumerator FrameTimeSample(int count)
        {
            Canvas canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
                yield break;

            var go = new GameObject("BenchEmitter", typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(canvas.transform, false);
            var e = go.AddComponent<UIParticleEmitter>();
            e.raycastTarget = false;
            e.Emission.Enabled = false;
            e.Main.MaxParticles = count;
            e.Main.StartLifetime = new RangeF(9999f, 9999f);
            e.Main.StartSpeed = new RangeF(20f, 120f);
            e.RotationOverLifetime.Enabled = true;
            e.SizeOverLifetime.Enabled = true;
            e.ColorOverLifetime.Enabled = true;
            e.Emit(count);

            yield return null; // let the first (spiky) frame pass
            float total = 0f, worst = 0f;
            int frames = 0;
            float end = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < end)
            {
                yield return null;
                float dt = Time.unscaledDeltaTime;
                total += dt;
                worst = Mathf.Max(worst, dt);
                frames++;
            }
            Destroy(go);
            Debug.Log($"[UIPE-BENCH] live8k avgFrameMs={(total / frames) * 1000f:F2} " +
                      $"worstFrameMs={worst * 1000f:F2} fps={frames / total:F1}");
        }
    }
}
