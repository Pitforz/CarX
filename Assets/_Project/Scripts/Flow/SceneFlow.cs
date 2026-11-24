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
        AudioSource _sfx, _ambient, _voice, _bed, _layer;
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

        /// <summary>Сколько секунд первая сцена проявляется из чёрного при запуске игры.</summary>
        public const float StartupFadeIn = 2f;

        /// <summary>
        /// Запуск игры: чёрный экран, пока грузится первая сцена, потом она 2 с проявляется из темноты.
        /// Только если игра стартует с первой сцены списка (MainMenu), чтобы тестовый Play в других сценах не менялся.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void StartupBlack()
        {
            if (SceneManager.GetActiveScene().buildIndex != 0) return;
            var flow = Get();
            flow._busy = true;
            flow._fade.color = Color.black;
            flow._fade.raycastTarget = true;
            flow.StartCoroutine(flow.StartupReveal());
        }

        IEnumerator StartupReveal()
        {
            // пара кадров, чтобы Awake/Start сцены и первая отрисовка прошли под чёрным
            yield return null;
            yield return null;
            yield return null;
            yield return Fade(1f, 0f, StartupFadeIn, Color.black);
            _fade.raycastTarget = false;
            _busy = false;
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
            _text.supportRichText = true;

            _sfx = AddSource(false);
            _ambient = AddSource(true);
            _voice = AddSource(false);
            _bed = AddSource(true);
            _layer = AddSource(true);
        }

        AudioSource AddSource(bool loop)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            s.loop = loop;
            return s;
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

            // 3. ход катсцены: диалог и дорога, либо старые карточки
            if (s.moments != null && s.moments.Length > 0)
                yield return Timeline(s);
            else if (s.cards != null)
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

        IEnumerator Timeline(CutsceneSequence s)
        {
            foreach (var m in s.moments)
            {
                if (m == null) continue;
                if (m.stopBeds) StopBeds();
                bool dialogue = m.lines != null && m.lines.Length > 0;
                float dur = m.seconds;
                bool usingSfx = false, usingBed = false;
                if (m.clip != null)
                {
                    if (dialogue)
                    {
                        _voice.clip = m.clip;
                        _voice.volume = m.volume;
                        _voice.loop = false;
                        _voice.Play();
                        // Длина клипа иногда ещё 0, пока файл не раскрылся. Секунды в катсцене — нижняя граница,
                        // чтобы разговор не обрывался на полуслове.
                        dur = Mathf.Max(m.clip.length, m.seconds);
                        if (dur < 1f) dur = 54.8f;
                    }
                    else if (m.loop && (dur <= 0f || dur > m.clip.length - 0.05f))
                    {
                        _bed.clip = m.clip;
                        _bed.volume = m.volume;
                        _bed.loop = true;
                        _bed.Play();
                        usingBed = true;
                        if (dur <= 0f) dur = m.clip.length;
                    }
                    else
                    {
                        _sfx.clip = m.clip;
                        _sfx.volume = m.volume;
                        _sfx.loop = false;
                        _sfx.Play();
                        usingSfx = true;
                        if (dur <= 0f) dur = m.clip.length;
                        else dur = Mathf.Max(dur, m.clip.length);
                    }
                }
                if (m.layer != null)
                {
                    _layer.clip = m.layer;
                    _layer.volume = m.layerVolume;
                    _layer.loop = dur <= 0f || dur > m.layer.length - 0.05f;
                    _layer.Play();
                }
                if (m.sting != null) _sfx.PlayOneShot(m.sting, m.stingVolume);
                if (dialogue) yield return Dialogue(m, dur, s.skippable);
                else yield return PlayMoment(m, dur, s.skippable, usingSfx, usingBed);
                if (usingSfx) _sfx.Stop();
            }
            _voice.Stop();
            StopBeds();
            _sfx.Stop();
            _text.text = "";
            SetTextAlpha(0f);
        }

        IEnumerator Dialogue(CutsceneSequence.Moment m, float dur, bool skippable)
        {
            PlaceText(false);
            int shown = -1;
            float t = 0f;
            while (t < dur || (_voice.isPlaying && t < dur + 2f))
            {
                if (t > 90f) break;
                if (skippable && SkipPressed()) break;
                int idx = -1;
                for (int i = 0; i < m.lines.Length; i++)
                    if (m.lines[i] != null && t + 0.05f >= m.lines[i].time) idx = i;
                if (idx != shown && idx >= 0)
                {
                    shown = idx;
                    _text.text = FormatLine(m.lines[idx]);
                    SetTextAlpha(1f);
                }
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            _voice.Stop();
            for (float f = 0f; f < 0.35f; f += Time.unscaledDeltaTime)
            {
                SetTextAlpha(1f - f / 0.35f);
                yield return null;
            }
            SetTextAlpha(0f);
            _text.text = "";
        }

        IEnumerator PlayMoment(CutsceneSequence.Moment m, float dur, bool skippable, bool usingSfx, bool usingBed)
        {
            bool timed = m.places != null && m.places.Length > 0;
            bool titled = !timed && !string.IsNullOrEmpty(m.title);
            if (titled)
            {
                PlaceText(true);
                _text.text = FormatTitle(m.title);
            }
            float fadeIn = Mathf.Min(0.7f, Mathf.Max(0.05f, dur * 0.25f));
            float textOut = Mathf.Min(0.6f, Mathf.Max(0.05f, dur * 0.2f));
            int shown = -1;
            float t = 0f;
            while (t < dur)
            {
                if (skippable && SkipPressed()) break;
                float k = 1f;
                if (m.fadeOut > 0.05f && t > dur - m.fadeOut)
                    k = Mathf.Clamp01((dur - t) / m.fadeOut);
                if (usingSfx) _sfx.volume = m.volume * k;
                if (usingBed) _bed.volume = m.volume * k;
                if (m.layer != null) _layer.volume = m.layerVolume * k;
                if (timed)
                {
                    int idx = -1;
                    for (int i = 0; i < m.places.Length; i++)
                    {
                        var card = m.places[i];
                        if (card == null || string.IsNullOrEmpty(card.text)) continue;
                        if (t >= card.time && t < card.time + Mathf.Max(0.2f, card.hold)) idx = i;
                    }
                    if (idx != shown)
                    {
                        shown = idx;
                        if (idx >= 0)
                        {
                            PlaceText(true);
                            _text.text = FormatTitle(m.places[idx].text);
                        }
                        else _text.text = "";
                    }
                    if (idx >= 0)
                    {
                        var card = m.places[idx];
                        float local = t - card.time;
                        float hold = Mathf.Max(0.2f, card.hold);
                        float edge = Mathf.Min(0.55f, hold * 0.22f);
                        float a = 1f;
                        if (local < edge) a = local / edge;
                        else if (local > hold - edge) a = (hold - local) / edge;
                        SetTextAlpha(Mathf.Clamp01(a));
                    }
                    else SetTextAlpha(0f);
                }
                else if (titled)
                {
                    float a = 1f;
                    if (t < fadeIn) a = t / fadeIn;
                    else if (t > dur - textOut) a = (dur - t) / textOut;
                    SetTextAlpha(Mathf.Clamp01(a));
                }
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            if (titled || timed)
            {
                SetTextAlpha(0f);
                _text.text = "";
            }
        }

        static string FormatLine(CutsceneSequence.Line line)
        {
            string color = line.speaker == "Виктор" ? "#e6c48a" : "#c5d4e4";
            if (string.IsNullOrEmpty(line.speaker)) return line.text;
            return "<color=" + color + ">" + line.speaker + "</color>\n" + line.text;
        }

        static string FormatTitle(string text)
        {
            int n = text.IndexOf('\n');
            if (n < 0) return "<size=50>" + text + "</size>";
            return "<size=52>" + text.Substring(0, n) + "</size>\n<size=30>" + text.Substring(n + 1) + "</size>";
        }

        void PlaceText(bool title)
        {
            var rt = _text.rectTransform;
            if (title)
            {
                rt.anchorMin = new Vector2(0.12f, 0.34f);
                rt.anchorMax = new Vector2(0.88f, 0.7f);
                _text.alignment = TextAnchor.MiddleCenter;
                _text.fontSize = 42;
            }
            else
            {
                rt.anchorMin = new Vector2(0.1f, 0.06f);
                rt.anchorMax = new Vector2(0.9f, 0.4f);
                _text.alignment = TextAnchor.LowerCenter;
                _text.fontSize = 32;
            }
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        void StopBeds()
        {
            _bed.Stop();
            _layer.Stop();
        }

        IEnumerator ShowCard(CutsceneSequence.Card c, bool skippable)
        {
            PlaceText(true);
            _text.fontSize = 40;
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
