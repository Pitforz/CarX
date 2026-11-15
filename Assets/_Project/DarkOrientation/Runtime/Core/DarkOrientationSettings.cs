using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace CarX.DarkOrientation
{
    public enum HandStyle { Outline, SolidWhite }

    /// <summary>All tunables of the dark-orientation mode. The asset lives in Resources so play-mode tweaks persist.</summary>
    [CreateAssetMenu(menuName = "CarX/Dark Orientation Settings", fileName = "DarkOrientationSettings")]
    public sealed class DarkOrientationSettings : ScriptableObject
    {
        public const string ResourcePath = "CarX.DarkOrientation/DarkOrientationSettings";

        [Header("Подключение")]
        [Tooltip("Сам добавлять режим в каждую сцену, где есть игрок (тег Player и CharacterController)")]
        public bool autoInstall = true;
        [Tooltip("Клавиша включения и выключения режима")]
        public Key toggleKey = Key.G;
        [Tooltip("Время появления рук и эффекта, с")]
        [Min(0.05f)] public float activateTime = 0.35f;
        [Tooltip("Время исчезновения рук и эффекта, с")]
        [Min(0.05f)] public float deactivateTime = 0.3f;

        [Header("Плечи и досягаемость")]
        [Tooltip("Правое плечо относительно камеры (вправо, вверх, вперёд), м. Левое зеркально")]
        public Vector3 shoulderOffset = new(0.19f, -0.24f, -0.04f);
        [Tooltip("Какую долю наклона взгляда вверх-вниз повторяют руки")]
        [Range(0f, 1f)] public float pitchFollow = 0.5f;
        [Tooltip("Дальше этого от плеча рука не дотягивается, м")]
        [Min(0.2f)] public float reach = 0.62f;
        [Tooltip("Рука отпускает точку, когда плечо отошло от неё дальше, м")]
        [Min(0.2f)] public float releaseDistance = 0.72f;
        [Tooltip("Рука отпускает точку, когда она ушла за этот угол от направления тела, градусы")]
        [Range(30f, 170f)] public float releaseAngle = 100f;
        [Tooltip("Рука срывается, если точка движется относительно игрока быстрее, м/с")]
        [Min(0.5f)] public float maxAnchorSpeed = 3f;
        [Tooltip("Кисть не подходит к камере ближе, м (ближняя плоскость камеры 0.2)")]
        [Min(0.1f)] public float minCameraDistance = 0.28f;
        [Tooltip("Зазор между ладонью и поверхностью, м")]
        [Min(0f)] public float palmOffset = 0.012f;
        [Tooltip("Две руки не держатся ближе друг к другу, м")]
        [Min(0f)] public float minAnchorSeparation = 0.15f;
        [Tooltip("Пауза после отпускания до нового касания, с")]
        [Min(0f)] public float regrabDelay = 0.3f;
        [Tooltip("За сколько рука доходит до найденной точки, с")]
        [Min(0.01f)] public float reachTime = 0.12f;

        [Header("Поиск")]
        [Tooltip("Расстояние ладони от плеча вперёд при поиске, м")]
        public Vector2 searchDistance = new(0.36f, 0.56f);
        [Tooltip("Смещение ладони вбок от плеча при поиске (минус = к центру), м")]
        public Vector2 searchSideRange = new(-0.10f, 0.05f);
        [Tooltip("Высота ладони над плечом при поиске, м")]
        public Vector2 searchHeightRange = new(0.08f, 0.20f);
        [Tooltip("Как часто рука выбирает новую точку поиска, с")]
        public Vector2 retargetInterval = new(0.5f, 1.3f);
        [Min(0.01f)] public float searchSmoothTime = 0.28f;
        [Tooltip("Мелкое дрожание руки при поиске, м")]
        [Min(0f)] public float searchJitter = 0.025f;
        [Tooltip("Радиус сферы, которой рука нащупывает препятствия, м")]
        [Min(0.005f)] public float probeRadius = 0.04f;
        [Tooltip("Слои, которых можно касаться")]
        public LayerMask touchLayers = Physics.DefaultRaycastLayers;

        [Header("Руки")]
        [Tooltip("Масштаб процедурных рук (1 = длина кисти 19 см)")]
        [Min(0.5f)] public float handScale = 1f;
        public HandStyle handStyle = HandStyle.Outline;
        public Color handFill = new(0.006f, 0.006f, 0.008f, 1f);
        public Color outlineColor = new(0.92f, 0.95f, 1f, 1f);
        [Tooltip("Толщина контура в пикселях при высоте экрана 1080")]
        [Range(0.5f, 8f)] public float outlineWidth = 3.5f;
        [Tooltip("Линии между пальцами и по краям ладони")]
        [Range(0f, 1f)] public float innerLines = 0.35f;

        [Header("Зона касания")]
        [Tooltip("Радиус сферы вокруг точки касания, м")]
        [Range(0.05f, 0.3f)] public float zoneRadius = 0.15f;
        [Tooltip("Доля радиуса, на которой зона плавно гаснет к краю")]
        [Range(0.05f, 0.95f)] public float zoneSoftness = 0.45f;
        [Min(0.01f)] public float zoneFadeIn = 0.2f;
        [Min(0.01f)] public float zoneFadeOut = 0.4f;
        [Tooltip("Цвет ближних точек карты глубины")]
        public Color depthNear = new(0.72f, 0.75f, 0.8f, 1f);
        [Tooltip("Цвет дальних точек карты глубины")]
        public Color depthFar = new(0.06f, 0.07f, 0.08f, 1f);
        [Tooltip("Подсветка рёбер и перепадов глубины")]
        [Range(0f, 2f)] public float depthEdges = 0.8f;
        [Tooltip("Яркость колец вокруг точки касания")]
        [Range(0f, 1f)] public float depthRings = 0.2f;
        [Min(0.005f)] public float ringSpacing = 0.03f;

        [Header("Остальная картинка")]
        [Tooltip("Яркость картинки вне зон касания")]
        [Range(0f, 1f)] public float dim = 0.15f;
        [Range(0f, 1f)] public float desaturate = 0.7f;
        [Tooltip("Сила размытия: число уровней уменьшения")]
        [Range(1, 6)] public int blurIterations = 4;
        [Tooltip("Где в кадре рисуется эффект. После постобработки цвета точные; до неё на эффект ложатся тонмаппинг и блум")]
        public RenderPassEvent renderEvent = RenderPassEvent.AfterRenderingPostProcessing;

        [Header("NPC")]
        [Tooltip("Создавать невидимые триггеры на костях Humanoid-персонажей без своих коллайдеров, пока режим включён")]
        public bool npcProxies = true;
        [Min(1f)] public float npcProxyRange = 8f;

        [Header("Шейдеры (ссылки нужны, чтобы шейдеры попали в сборку)")]
        public Shader handShader;
        public Shader blurShader;
        public Shader compositeShader;

        static DarkOrientationSettings s_Fallback;

        public Shader HandShader => handShader != null ? handShader : Shader.Find("Hidden/CarX/DarkOrientation/HandMask");
        public Shader BlurShader => blurShader != null ? blurShader : Shader.Find("Hidden/CarX/DarkOrientation/Blur");
        public Shader CompositeShader => compositeShader != null ? compositeShader : Shader.Find("Hidden/CarX/DarkOrientation/Composite");

        public static DarkOrientationSettings Load()
        {
            var settings = Resources.Load<DarkOrientationSettings>(ResourcePath);
            if (settings != null) return settings;
            if (s_Fallback == null)
            {
                s_Fallback = CreateInstance<DarkOrientationSettings>();
                s_Fallback.hideFlags = HideFlags.DontSave;
                Debug.LogWarning("CarX DarkOrientation: settings asset not found in Resources/" + ResourcePath + ", using defaults");
            }
            return s_Fallback;
        }
    }
}
