using System.Collections.Generic;
using System.IO;
using CarX.Flow;
using CarX.Level;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CarX.EditorTools
{
    /// <summary>
    /// Создаёт сцену-заглушку MainMenu с тестовой кнопкой, файл катсцены Cutscene_NewGame, ставит игрока в сцену канала
    /// и прописывает обе сцены в Build Profiles. Безопасно запускать повторно: ваши правки в катсцене не затираются.
    /// </summary>
    public static class SceneFlowBuilder
    {
        const string FlowDir = "Assets/_Project/Flow";
        const string CutscenePath = FlowDir + "/Cutscene_NewGame.asset";
        const string MenuPath = "Assets/_Project/Scenes/MainMenu.unity";
        const string CanalPath = "Assets/_Project/Scenes/Test/CanalEntrance_Test.unity";
        const string LabPath = "Assets/_Project/Scenes/MAIN_LEVEL_GRAPHICS_lab.unity";

        [MenuItem("CarX/Flow/Создать главное меню (заглушка), катсцену и игрока в канале")]
        static void BuildMenu() { Debug.Log("CarX Flow: " + BuildAll()); }

        public static string BuildAll()
        {
            string log = "";
            var seq = EnsureCutscene();
            log += "катсцена ok; ";
            log += PreparePlayerInCanal() + "; ";
            BuildMenuScene(seq);
            log += "меню ok; ";
            SetBuildScenes();
            log += "Build Profiles ok";
            return log;
        }

        // ------------------------------------------------------------------ катсцена-заглушка

        static CutsceneSequence EnsureCutscene()
        {
            Directory.CreateDirectory(FlowDir);
            var seq = AssetDatabase.LoadAssetAtPath<CutsceneSequence>(CutscenePath);
            if (seq != null) return seq;
            seq = ScriptableObject.CreateInstance<CutsceneSequence>();
            seq.targetScene = "MAIN_LEVEL_GRAPHICS_lab";
            seq.fadeOut = 1.4f;
            seq.blackHold = 0.8f;
            seq.holdAfterCards = 1.2f;
            seq.fadeIn = 3f;
            seq.cards = new[]
            {
                Card("ЗАГЛУШКА СЦЕНАРИЯ.\nЛас-Вегас. Мотель «Пыльная роза», 03:12.", 1f, 3.2f),
                Card("Старый «Крайслер» ползёт по Бульвару мимо погасших вывесок.", 1f, 3.2f),
                Card("Последний адрес, который назвал отец, — водоотводный канал за шоссе.", 1f, 3.6f),
                Card("Дальше пешком.", 0.6f, 2.2f),
            };
            AssetDatabase.CreateAsset(seq, CutscenePath);
            AssetDatabase.SaveAssets();
            return seq;
        }

        static CutsceneSequence.Card Card(string text, float fadeIn, float hold)
        {
            return new CutsceneSequence.Card { text = text, fadeIn = fadeIn, hold = hold, fadeOut = 0.9f, pauseAfter = 0.5f };
        }

        // ------------------------------------------------------------------ игрок в сцене канала

        static string PreparePlayerInCanal()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.isDirty && !string.IsNullOrEmpty(scene.path)) EditorSceneManager.SaveScene(scene);
            scene = EditorSceneManager.OpenScene(CanalPath, OpenSceneMode.Single);
            var canal = Object.FindAnyObjectByType<CanalEntrance>();
            if (canal == null) return "в сцене канала нет CanalEntrance";
            var existing = Object.FindAnyObjectByType<CharacterController>();
            if (existing != null) return "игрок в канале уже есть";

            const float s = 50f;   // прямой участок после поворота, лицом к входу в тоннель
            Vector2 p = canal.PointAt(s, 0f), d = -canal.DirAt(s);
            Vector3 world = canal.transform.TransformPoint(new Vector3(p.x, 0.05f, p.y));
            Vector3 dir = canal.transform.TransformDirection(new Vector3(d.x, 0f, d.y)).normalized;
            var player = TunnelMazeBuilder.SpawnPlayer(world, dir);
            if (player != null) player.name = "Игрок (старт после катсцены; двигайте как хотите)";
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return "игрок поставлен в канал";
        }

        // ------------------------------------------------------------------ сцена главного меню

        static void BuildMenuScene(CutsceneSequence seq)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var root = new GameObject("MainMenu (заглушка)");
            root.AddComponent<MainMenuScene>();

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.03f, 0.03f, 0.04f);
            cam.cullingMask = 0;
            camGo.AddComponent<AudioListener>();

            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            es.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();

            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/ThirdParty/LiberationMono/Resources/CarX/Fonts/LiberationMono-Regular.ttf")
                       ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            Label(canvasGo.transform, "Заголовок", "CARX", font, 96, new Vector2(0.5f, 0.68f), new Vector2(1200f, 160f));
            Label(canvasGo.transform, "Подпись", "ЗАГЛУШКА ГЛАВНОГО МЕНЮ — настоящее меню сделаем отдельно", font, 28, new Vector2(0.5f, 0.58f), new Vector2(1500f, 60f));

            var btnGo = new GameObject("Кнопка: Начать (тест)", typeof(RectTransform), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(canvasGo.transform, false);
            var rt = btnGo.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.4f);
            rt.sizeDelta = new Vector2(520f, 110f);
            btnGo.GetComponent<Image>().color = new Color(0.16f, 0.15f, 0.14f, 1f);
            Label(btnGo.transform, "Текст", "НАЧАТЬ (тест)", font, 40, new Vector2(0.5f, 0.5f), new Vector2(500f, 100f));
            var play = btnGo.AddComponent<PlayCutsceneButton>();
            play.sequence = seq;

            EditorSceneManager.SaveScene(scene, MenuPath);
        }

        static void Label(Transform parent, string name, string text, Font font, int size, Vector2 anchor, Vector2 sizeDelta)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchor;
            rt.sizeDelta = sizeDelta;
            var t = go.GetComponent<Text>();
            t.font = font;
            t.text = text;
            t.fontSize = size;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = new Color(0.92f, 0.9f, 0.84f);
            t.raycastTarget = false;
        }

        // ------------------------------------------------------------------ Build Profiles

        static void SetBuildScenes()
        {
            var list = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(MenuPath, true),
                new EditorBuildSettingsScene(LabPath, true),
                new EditorBuildSettingsScene(CanalPath, true),
            };
            foreach (var s in EditorBuildSettings.scenes)
                if (s.path != MenuPath && s.path != LabPath && s.path != CanalPath && File.Exists(s.path)) list.Add(s);
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
