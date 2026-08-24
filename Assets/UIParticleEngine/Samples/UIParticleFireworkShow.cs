using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace UIParticleEngine.Samples
{
    /// <summary>
    /// UI adaptation of a classic firework effect: each shell teleports the emitter to
    /// a random point inside the sky area, tints the burst with a random hue, and fires
    /// a radial burst whose trails draw the streaks. World simulation space keeps the
    /// previous shells falling while the emitter moves to the next one.
    /// </summary>
    public class UIParticleFireworkShow : MonoBehaviour
    {
        [SerializeField] private Button _Button;
        [SerializeField] private UIParticleEmitter _Emitter;
        [SerializeField] private RectTransform _SkyArea;
        [SerializeField, Min(1)] private int _Shells = 5;
        [SerializeField, Min(0.05f)] private float _ShellInterval = 0.3f;
        [SerializeField] private RangeF _ParticlesPerShell = new RangeF(28f, 40f);

        private Coroutine _show;
        private WaitForSeconds _wait;

        private void Awake()
        {
            if (_Button != null)
                _Button.onClick.AddListener(Fire);
            if (_Emitter != null && _Emitter.Sprite == null)
                _Emitter.Sprite = UIParticleSpriteLibrary.SoftCircle;
            if (_Emitter != null && _Emitter.Trails.Enabled)
            {
                // The module defaults keep ribbon width and alpha constant, which draws
                // firework trails as solid bars; taper both so they read as streaks.
                // Kept deliberately thin and short: once drag slows a particle down, a
                // full-width ribbon collapses into a fat spike-shaped cone.
                _Emitter.Trails.WidthOverTrail = AnimationCurve.Linear(0f, 0.55f, 1f, 0f);
                var fade = new Gradient();
                fade.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(0.65f, 0f), new GradientAlphaKey(0f, 1f) });
                _Emitter.Trails.ColorOverTrail = fade;
                _Emitter.Trails.Lifetime = new RangeF(0.26f, 0.26f);
            }
            _wait = new WaitForSeconds(_ShellInterval);
        }

        public void Fire()
        {
            if (_Emitter == null || _SkyArea == null)
                return;
            if (_show != null)
                StopCoroutine(_show);
            _show = StartCoroutine(Show());
        }

        private IEnumerator Show()
        {
            var emitterRect = (RectTransform)_Emitter.transform;
            for (int i = 0; i < _Shells; i++)
            {
                Rect sky = _SkyArea.rect;
                emitterRect.anchoredPosition = new Vector2(
                    Random.Range(sky.xMin, sky.xMax), Random.Range(sky.yMin, sky.yMax));

                float hue = Random.value;
                _Emitter.Main.StartColorA = Color.HSVToRGB(hue, 0.8f, 1f);
                _Emitter.Main.StartColorB = Color.HSVToRGB(Mathf.Repeat(hue + 0.08f, 1f), 0.3f, 1f);
                _Emitter.Emit(Mathf.RoundToInt(_ParticlesPerShell.Evaluate(Random.value)));
                yield return _wait;
            }
            _show = null;
        }
    }
}
