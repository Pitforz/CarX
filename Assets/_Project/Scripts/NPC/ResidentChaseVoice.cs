using CarX.Gameplay;
using UnityEngine;

namespace CarX.NPC
{
    [DisallowMultipleComponent]
    internal sealed class ResidentChaseVoice : MonoBehaviour
    {
        static readonly (string Subtitle, string Clip)[] Lines =
        {
            ("Эй, ты, а ну подошёл!", "StepCloser"),
            ("Куда ты убегаешь?", "RunningAway"),
            ("Вот ты где!", "FoundYou"),
            ("Щас ты получишь!", "YoullGetIt"),
            ("Выключи свет!", "LightsOff")
        };

        AudioSource _voice;
        PlayerStatusHud _hud;
        PlayerMenus _menus;
        float _nextShout;
        int _lastLine = -1;

        internal static ResidentChaseVoice ForPlayer(PlayerHealth player) =>
            player.TryGetComponent<ResidentChaseVoice>(out var shared)
                ? shared : player.gameObject.AddComponent<ResidentChaseVoice>();

        void Awake()
        {
            _hud = GetComponent<PlayerStatusHud>();
            _menus = GetComponent<PlayerMenus>();
        }

        internal void TryShout(Transform speaker)
        {
            if (Time.time < _nextShout || (_voice != null && _voice.isPlaying) ||
                (_menus != null && _menus.IsNpcSpeaking))
                return;

            int index = Random.Range(0, Lines.Length - (_lastLine < 0 ? 0 : 1));
            if (_lastLine >= 0 && index >= _lastLine)
                index++;

            _lastLine = index;
            var line = Lines[index];
            AudioClip clip = DialogueVoice.GetStoryClip("Residents/" + line.Clip);
            _nextShout = Time.time + Random.Range(7f, 8f);
            if (clip != null)
            {
                if (_voice == null)
                    _voice = DialogueVoice.CreateSource(speaker, "Resident Chase Voice", 3f, 18f);
                _voice.transform.SetParent(speaker, false);
                _voice.transform.localPosition = Vector3.up * 1.55f;
                _voice.clip = clip;
                _voice.Play();
            }
            _hud?.ShowSubtitle($"Жилец: {line.Subtitle}", duration: clip != null ? clip.length + 0.25f : 2f);
        }

        void OnDestroy()
        {
            if (_voice != null)
                Destroy(_voice.gameObject);
        }
    }
}
