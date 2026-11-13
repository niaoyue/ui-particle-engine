using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace UIParticleEngine.Tests
{
    public class UIParticleCanvasTests
    {
        private GameObject _root;
        private UIParticleEmitter _emitter;
        private float _timeScale;

        [SetUp]
        public void SetUp()
        {
            _timeScale = Time.timeScale;
            _root = new GameObject("Particle canvas", typeof(Canvas));
            _root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var go = new GameObject("Emitter", typeof(RectTransform));
            go.SetActive(false);
            go.transform.SetParent(_root.transform, false);
            _emitter = go.AddComponent<UIParticleEmitter>();
            _emitter.Main.PlayOnAwake = false;
            _emitter.Main.StartLifetime = 100f;
            _emitter.Main.StartSpeed = 0f;
            _emitter.Main.StartSize = 32f;
            _emitter.Main.StartColorA = Color.white;
            _emitter.Main.StartColorB = Color.white;
            _emitter.Shape.Enabled = false;
            _emitter.Emission.RateOverTime = 0f;
            go.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = _timeScale;
            Object.DestroyImmediate(_root);
        }

        [UnityTest]
        public IEnumerator DriverAdvancesAndRecoversAfterDisableEnable()
        {
            Assert.IsNotNull(_emitter.GetComponent<CanvasRenderer>());
            _emitter.Play();
            _emitter.Emit(4);
            yield return null;
            Assert.Greater(_emitter.Time, 0f, "The actual runtime driver must advance playback.");
            Assert.AreEqual(4, _emitter.ParticleCount);

            _emitter.gameObject.SetActive(false);
            Assert.AreEqual(0, _emitter.ParticleCount);
            _emitter.gameObject.SetActive(true);
            _emitter.Play();
            _emitter.Emit(3);
            yield return null;
            Assert.AreEqual(3, _emitter.ParticleCount);
            Assert.Greater(_emitter.Time, 0f);
        }

        [UnityTest]
        public IEnumerator PauseFreezesPlaybackAndPlayResumesIt()
        {
            _emitter.Play();
            _emitter.Emit(1);
            yield return null;
            _emitter.Pause();
            float pausedAt = _emitter.Time;
            yield return null;
            Assert.IsTrue(_emitter.IsPaused);
            Assert.AreEqual(pausedAt, _emitter.Time);
            _emitter.Play();
            yield return null;
            Assert.Greater(_emitter.Time, pausedAt);
        }

        [UnityTest]
        public IEnumerator UnscaledPlaybackWorksWhenGameTimeIsPaused()
        {
            _emitter.Main.UseUnscaledTime = true;
            _emitter.Emission.RateOverTime = 100f;
            Time.timeScale = 0f;
            _emitter.Play();
            yield return new WaitForSecondsRealtime(0.1f);
            Assert.Greater(_emitter.Time, 0f);
            Assert.Greater(_emitter.ParticleCount, 0);
        }

        [UnityTest]
        public IEnumerator RenderingRespectsCanvasGroupAndBothMaskTypes()
        {
            var cameraGo = new GameObject("Capture camera", typeof(Camera));
            var camera = cameraGo.GetComponent<Camera>();
            var target = new RenderTexture(256, 256, 24);
            var readback = new Texture2D(256, 256, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.orthographic = true;
                camera.orthographicSize = 128f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.transform.position = new Vector3(0f, 0f, -10f);
                camera.targetTexture = target;
                camera.enabled = false;
                var canvas = _root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1f;
                var clippingGo = new GameObject("Clip", typeof(RectTransform), typeof(CanvasGroup));
                var rect = (RectTransform)clippingGo.transform;
                rect.SetParent(_root.transform, false);
                rect.sizeDelta = new Vector2(64f, 64f);
                _emitter.transform.SetParent(rect, false);
                _emitter.rectTransform.anchoredPosition = new Vector2(48f, 0f);
                _emitter.Emit(1);
                yield return null;
                Assert.Greater(CaptureParticle(camera, target, readback), 0.8f);

                clippingGo.GetComponent<CanvasGroup>().alpha = 0.25f;
                yield return null;
                float faded = CaptureParticle(camera, target, readback);
                Assert.That(faded, Is.InRange(0.1f, 0.7f));

                clippingGo.GetComponent<CanvasGroup>().alpha = 1f;
                var rectMask = clippingGo.AddComponent<RectMask2D>();
                yield return null;
                Assert.Less(CaptureParticle(camera, target, readback), 0.03f);

                rectMask.enabled = false;
                clippingGo.AddComponent<Image>();
                var mask = clippingGo.AddComponent<Mask>();
                mask.showMaskGraphic = false;
                yield return null;
                Assert.Less(CaptureParticle(camera, target, readback), 0.03f);

                mask.enabled = false;
                clippingGo.GetComponent<Image>().enabled = false;
                yield return null;
                Assert.Greater(CaptureParticle(camera, target, readback), 0.8f);
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                Object.DestroyImmediate(cameraGo);
                Object.DestroyImmediate(readback);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        private float CaptureParticle(Camera camera, RenderTexture target, Texture2D readback)
        {
            Canvas.ForceUpdateCanvases();
            _emitter.Simulate(0f);
            camera.Render();
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0f, 0f, 256f, 256f), 0, 0);
            readback.Apply();
            Vector3 point = camera.WorldToScreenPoint(_emitter.transform.position);
            return readback.GetPixel(Mathf.RoundToInt(point.x), Mathf.RoundToInt(point.y)).r;
        }
    }
}
