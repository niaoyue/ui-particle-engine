using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UIParticleEngine.Samples
{
    /// <summary>
    /// The classic coin-earn animation: a button click bursts coins out of the button,
    /// the attractor pulls them into the wallet icon after a short delay, and every
    /// coin that arrives bumps the counter and punches the icon's scale.
    /// </summary>
    public class UIParticleCoinReward : MonoBehaviour
    {
        [SerializeField] private UIParticleEmitter _Emitter;
        [SerializeField] private UIParticleAttractor _Attractor;
        [SerializeField] private Button _CollectButton;
        [SerializeField] private TMP_Text _CounterText;
        [SerializeField] private RectTransform _WalletIcon;
        [SerializeField, Min(1)] private int _CoinsPerCollect = 12;

        private int _total;
        private Vector3 _iconBaseScale = Vector3.one;

        private void Awake()
        {
            if (_Emitter != null && _Emitter.Sprite == null)
                _Emitter.Sprite = UIParticleSpriteLibrary.Circle;
            if (_CollectButton != null)
                _CollectButton.onClick.AddListener(Collect);
            if (_Attractor != null)
                _Attractor.OnAttracted.AddListener(OnCoinArrived);
            if (_WalletIcon != null)
                _iconBaseScale = _WalletIcon.localScale;

            if (_CounterText != null)
                _CounterText.text = "0";
        }

        public void Collect()
        {
            if (_Emitter != null)
                _Emitter.Emit(_CoinsPerCollect);
        }

        private void OnCoinArrived()
        {
            _total++;
            if (_CounterText != null)
                _CounterText.text = _total.ToString();
            if (_WalletIcon != null)
                _WalletIcon.localScale = _iconBaseScale * 1.35f;
        }

        private void Update()
        {
            if (_WalletIcon != null)
                _WalletIcon.localScale = Vector3.Lerp(_WalletIcon.localScale, _iconBaseScale,
                    Time.unscaledDeltaTime * 10f);
        }
    }
}
