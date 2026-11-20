using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CarX.Flow
{
    /// <summary>
    /// Единый загрузчик сцен с затемнением. Создаёт себя сам при первом вызове и переживает смену сцены, поэтому
    /// чёрный экран не мигает на стыке. Использование:
    /// <code>
    /// SceneFlow.Play(sequence);                 // затемнение → карточки текста → загрузка → проявление
    /// SceneFlow.Load("ИмяСцены");               // то же без карточек, быстро
    /// SceneFlow.Load("ИмяСцены", 0.5f, 1f);     // свои времена затемнения и проявления
    /// SceneFlow.SceneReady += ...               // событие: новая сцена загружена и начинает проявляться
    /// </code>
    /// </summary>
    public sealed class SceneFlow : MonoBehaviour
    {
        static SceneFlow _instance;
        const string FontPath = "CarX/Fonts/LiberationMono-Regular";

        Image _fade;
        Text _text;
        AudioSource _sfx, _ambient;
        bool _busy;

        /// <summary>Идёт ли сейчас переход (повторный запуск игнорируется).</summary>
        public static bool Busy { get { return _instance != null && _instance._busy; } }

        /// <summary>Новая сцена загружена, начинается проявление. Передаётся имя сцены.</summary>
        public static event Action<string> SceneReady;

        public static void Play(CutsceneSequence sequence)
        {
            if (sequence == null) { Debug.LogError("SceneFlow.Play: не задана катсцена (CutsceneSequence)."); return; }
            var flow = Get();
            if (flow._busy) return;
            flow.StartCoroutine(flow.Run(sequence));
        }

        /// <summary>Простой переход без карточек текста.</summary>
        public static void Load(string sceneName, float fadeOut = 0.8f, float fadeIn = 1.2f, bool lockCursor = true)
        {
            var seq = ScriptableObject.CreateInstance<CutsceneSequence>();
            seq.targetScene = sceneName;
            seq.fadeOut = fadeOut;
            seq.fadeIn = fadeIn;
            seq.blackHold = 0.1f;
            seq.holdAfterCards = 0.1f;
            seq.lockCursorInTarget = lockCursor;
            Play(seq);
        }

        static SceneFlow Get()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("SceneFlow (переживает смену сцены)");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<SceneFlow>();
            _instance.Build();
            return _instance;
        }

        void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var fadeGo = new GameObject("Затемнение", typeof(RectTransform), typeof(Image));
            fadeGo.transform.SetParent(transform, false);
            Stretch(fadeGo.GetComponent<RectTransform>());
            _fade = fadeGo.GetComponent<Image>();
            _fade.color = new Color(0f, 0f, 0f, 0f);
            _fade.raycastTarget = false;

            var textGo = new GameObject("Текст", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(transform, false);
            var rt = textGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.15f, 0.2f);
            rt.anchorMax = new Vector2(0.85f, 0.8f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            _text = textGo.GetComponent<Text>();
            _text.font = Resources.Load<Font>(FontPath) ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _text.fontSize = 40;
            _text.alignment = TextAnchor.MiddleCenter;
            _text.horizontalOverflow = HorizontalWrapMode.Wrap;
            _text.verticalOverflow = VerticalWrapMode.Overflow;
            _text.color = new Color(0.92f, 0.9f, 0.84f, 0f);
            _text.raycastTarget = false;

            _sfx = gameObject.AddComponent<AudioSource>();
            _sfx.playOnAwake = false;
            _sfx.spatialBlend = 0f;
            _ambient = gameObject.AddComponent<AudioSource>();
            _ambient.playOnAwake = false;
            _ambient.loop = true;
            _ambient.spatialBlend = 0f;
        }

        static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
        }

        IEnumerator Run(CutsceneSequence s)
        {
            _busy = true;
            Cursor.visible = false;
            _text.text = "";
            SetTextAlpha(0f);

            // 1. затемнение (во время него блокируем клики по меню)
            _fade.raycastTarget = true;
            yield return Fade(0f, 1f, s.fadeOut, s.fadeColor);
            if (s.ambientLoop != null)
            {
                _ambient.clip = s.ambientLoop;
                _ambient.volume = s.ambientVolume;
                _ambient.Play();
            }
            yield return Wait(s.blackHold, false);

            // 2. загрузка новой сцены идёт в фоне, пока показываются карточки
            AsyncOperation load = SceneManager.LoadSceneAsync(s.targetScene);
            if (load == null)
            {
                Debug.LogError("SceneFlow: сцена «" + s.targetScene + "» не найдена. Добавьте её в File → Build Profiles → Scene List.");
                _ambient.Stop();
                yield return Fade(1f, 0f, s.fadeIn, s.fadeColor);
                _fade.raycastTarget = false;
                Cursor.visible = true;
                _busy = false;
                yield break;
            }
            load.allowSceneActivation = false;

            // 3. карточки на чёрном экране
            if (s.cards != null)
                foreach (var card in s.cards)
                    if (card != null) yield return ShowCard(card, s.skippable);
            yield return Wait(s.holdAfterCards, false);

            // 4. переключение сцены, пока экран чёрный
            while (load.progress < 0.9f) yield return null;
            load.allowSceneActivation = true;
            while (!load.isDone) yield return null;
            yield return null;   // Awake/Start новой сцены успевают отработать в темноте
            yield return null;

            if (s.lockCursorInTarget) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
            else { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            if (s.arriveSound != null) _sfx.PlayOneShot(s.arriveSound, s.arriveVolume);
            if (SceneReady != null) SceneReady(s.targetScene);

            // 5. проявление; фоновый звук затихает вместе с ним
            float ambientStart = _ambient.volume;
            yield return Fade(1f, 0f, s.fadeIn, s.fadeColor, t => _ambient.volume = ambientStart * (1f - t));
            _ambient.Stop();
            _fade.raycastTarget = false;
            _busy = false;
        }

        IEnumerator ShowCard(CutsceneSequence.Card c, bool skippable)
        {
            _text.text = c.typewriter ? "" : c.text;
            SetTextAlpha(0f);
            if (c.sound != null) _sfx.PlayOneShot(c.sound, c.soundVolume);

            if (c.typewriter)
            {
                SetTextAlpha(1f);
                float shown = 0f;
                while (shown < c.text.Length)
                {
                    if (skippable && SkipPressed()) break;
                    shown += Time.unscaledDeltaTime * c.charsPerSecond;
                    _text.text = c.text.Substring(0, Mathf.Min(c.text.Length, (int)shown));
                    yield return null;
                }
                _text.text = c.text;
            }
            else
            {
                for (float t = 0f; t < c.fadeIn; t += Time.unscaledDeltaTime)
                {
                    if (skippable && SkipPressed()) break;
                    SetTextAlpha(Mathf.Clamp01(t / c.fadeIn));
                    yield return null;
                }
                SetTextAlpha(1f);
            }
            yield return Wait(c.hold, skippable);
            for (float t = 0f; t < c.fadeOut; t += Time.unscaledDeltaTime)
            {
                SetTextAlpha(1f - Mathf.Clamp01(t / c.fadeOut));
                yield return null;
            }
            SetTextAlpha(0f);
            _text.text = "";
            yield return Wait(c.pauseAfter, false);
        }

        IEnumerator Wait(float seconds, bool skippable)
        {
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                if (skippable && SkipPressed()) yield break;
                yield return null;
            }
        }

        IEnumerator Fade(float from, float to, float seconds, Color color, Action<float> onProgress = null)
        {
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / seconds);
                color.a = Mathf.Lerp(from, to, k);
                _fade.color = color;
                if (onProgress != null) onProgress(k);
                yield return null;
            }
            color.a = to;
            _fade.color = color;
            if (onProgress != null) onProgress(1f);
        }

        void SetTextAlpha(float a)
        {
            var c = _text.color;
            c.a = a;
            _text.color = c;
        }

        static bool SkipPressed()
        {
            var k = Keyboard.current;
            var m = Mouse.current;
            return (k != null && (k.spaceKey.wasPressedThisFrame || k.enterKey.wasPressedThisFrame || k.escapeKey.wasPressedThisFrame))
                || (m != null && m.leftButton.wasPressedThisFrame);
        }
    }
}
