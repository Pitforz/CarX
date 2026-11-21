using System.Collections;
using CarX.NPC;
using UnityEngine;

namespace CarX.Gameplay
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerStatusHud))]
    public sealed class PlayerInnerVoice : MonoBehaviour
    {
        [SerializeField] AudioClip _civilianWarningVoice;
        [SerializeField] AudioClip _keepPistolVoice;
        [SerializeField] AudioClip _intimidationVoice;
        [SerializeField, Range(0f, 1f)] float _volume = 0.35f;
        [SerializeField, Min(0f)] float _hintRepeatDelay = 8f;

        PlayerStatusHud _hud;
        PlayerHealth _health;
        PlayerMenus _menus;
        AudioSource _voice;
        Coroutine _speech;
        float _nextHintTime;

        internal bool IsSpeaking => _speech != null;

        void Awake()
        {
            _hud = GetComponent<PlayerStatusHud>();
            _health = GetComponent<PlayerHealth>();
            _menus = GetComponent<PlayerMenus>();
        }

        void OnEnable() => _health.HealthChanged += OnHealthChanged;

        internal void WarnAboutCivilian() =>
            Say(_civilianWarningVoice, "Опусти ствол, Лёша. Ты сюда за отцом пришёл.");

        internal void KeepPistol() =>
            Say(_keepPistolVoice, "Я думаю, эта вещь мне ещё понадобится.");

        internal void ConsiderIntimidation() =>
            Say(_intimidationVoice, "А может, попробовать припугнуть его?", important: true);

        internal void CommentOnBrokenFlashlight(string subtitle, string voicePath)
        {
            if (!IsSpeaking && (_menus == null || !_menus.IsNpcSpeaking))
                Say(DialogueVoice.GetStoryClip(voicePath), subtitle, important: true);
        }

        internal void ReadClue(AudioClip clip, string subtitle = null) =>
            Say(clip, subtitle, important: true);

        void Say(AudioClip clip, string subtitle, bool important = false)
        {
            if (!isActiveAndEnabled || _health.IsDead)
                return;
            if (!important && (IsSpeaking || Time.time < _nextHintTime))
                return;
            if (!important && _menus != null && _menus.IsNpcSpeaking)
                return;

            StopSpeech();
            if (clip == null)
            {
                if (!string.IsNullOrEmpty(subtitle))
                    _hud.ShowSubtitle(subtitle);
                return;
            }

            if (_voice == null)
            {
                var emitter = new GameObject("Alexey Inner Voice");
                emitter.transform.SetParent(transform, false);
                _voice = emitter.AddComponent<AudioSource>();
                _voice.playOnAwake = false;
                _voice.spatialBlend = 0f;
                _voice.reverbZoneMix = 0f;
                _voice.dopplerLevel = 0f;
                _voice.priority = 64;
            }
            _voice.volume = _volume;
            _speech = StartCoroutine(Speak(clip, subtitle));
        }

        IEnumerator Speak(AudioClip clip, string subtitle)
        {
            if (!string.IsNullOrEmpty(subtitle))
                _hud.ShowSubtitle(subtitle, duration: clip.length + 0.15f);
            yield return DialogueVoice.Play(_voice, clip);
            _nextHintTime = Time.time + _hintRepeatDelay;
            _speech = null;
        }

        void OnHealthChanged(int health)
        {
            if (health == 0)
                StopSpeech();
        }

        void StopSpeech()
        {
            if (_speech != null)
                StopCoroutine(_speech);
            _speech = null;
            if (_voice != null)
                _voice.Stop();
        }

        void OnDisable()
        {
            _health.HealthChanged -= OnHealthChanged;
            StopSpeech();
        }

        void OnDestroy()
        {
            if (_voice != null)
                Destroy(_voice.gameObject);
        }
    }
}
