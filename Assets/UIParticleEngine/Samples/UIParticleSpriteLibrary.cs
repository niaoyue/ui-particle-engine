using UnityEngine;

namespace UIParticleEngine.Samples
{
    /// <summary>
    /// Procedurally generated sprites for the samples, so the demos need no texture
    /// assets. Generated once, cached for the app lifetime.
    /// </summary>
    public static class UIParticleSpriteLibrary
    {
        private static Sprite _softCircle;
        private static Sprite _circle;

        /// <summary>Radial falloff disc — glows/sparkles (use an additive-ish look via color).</summary>
        public static Sprite SoftCircle
        {
            get
            {
                if (_softCircle == null)
                    _softCircle = Generate("UIParticle SoftCircle", soft: true);
                return _softCircle;
            }
        }

        /// <summary>Solid anti-aliased disc — coins, dots, bubbles.</summary>
        public static Sprite Circle
        {
            get
            {
                if (_circle == null)
                    _circle = Generate("UIParticle Circle", soft: false);
                return _circle;
            }
        }

        private static Sprite Generate(string name, bool soft)
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var pixels = new Color32[size * size];
            float center = (size - 1) * 0.5f;
            float radius = size * 0.5f - 1f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / radius;

                    float alpha;
                    if (soft)
                    {
                        float f = Mathf.Clamp01(1f - d);
                        alpha = f * f; // quadratic falloff reads as a glow
                    }
                    else
                    {
                        alpha = Mathf.Clamp01((0.95f - d) / 0.08f); // hard disc, thin AA rim
                    }

                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f + 0.5f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);

            var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }

    /// <summary>Assigns a procedural sprite to the emitter at startup (samples only).</summary>
    [RequireComponent(typeof(UIParticleEmitter))]
    public class UIParticleProceduralSprite : MonoBehaviour
    {
        public enum Kind
        {
            SoftCircle,
            Circle
        }

        [SerializeField] private Kind _Kind = Kind.SoftCircle;

        private void Awake()
        {
            var emitter = GetComponent<UIParticleEmitter>();
            emitter.Sprite = _Kind == Kind.Circle
                ? UIParticleSpriteLibrary.Circle
                : UIParticleSpriteLibrary.SoftCircle;
        }
    }
}
