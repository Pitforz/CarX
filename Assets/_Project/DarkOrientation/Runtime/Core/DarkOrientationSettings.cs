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
        [Tooltip("Время, за которое руки поднимаются, с")]
        [Min(0.05f)] public float activateTime = 0.4f;
        [Tooltip("Время, за которое темнота и контур проявляются после того, как руки уже подняты, с")]
        [Min(0.05f)] public float effectTime = 0.55f;
        [Tooltip("Доля подъёма рук, после которой начинает проявляться эффект")]
        [Range(0.5f, 1f)] public float effectAfterHands = 0.88f;
        [Tooltip("Время исчезновения эффекта, затем руки опускаются, с")]
        [Min(0.05f)] public float deactivateTime = 0.35f;

        [Header("Плечи и досягаемость")]
        [Tooltip("Правое плечо относительно камеры (вправо, вверх, вперёд), м. Левое зеркально")]
        public Vector3 shoulderOffset = new(0.19f, -0.24f, -0.04f);
        [Tooltip("Дальше этого от плеча рука не нащупывает новую опору, м")]
        [Min(0.2f)] public float reach = 1.15f;
        [Tooltip("Уже взятая стена держится до этого расстояния, даже если перед ней препятствие, м")]
        [Min(0.2f)] public float releaseDistance = 1.35f;
        [Tooltip("Рука срывается, если точка движется относительно игрока быстрее, м/с")]
        [Min(0.5f)] public float maxAnchorSpeed = 3f;
        [Tooltip("Кисть не подходит к камере ближе, м (ближняя плоскость камеры 0.2)")]
        [Min(0.1f)] public float minCameraDistance = 0.46f;
        [Tooltip("Зазор между кончиком пальца и поверхностью, м")]
        [Min(0f)] public float palmOffset = 0.008f;
        [Tooltip("Ближе этого рука ложится ладонью. Дальше касается выпрямленными пальцами, м")]
        [Min(0.2f)] public float palmReach = 0.62f;
        [Tooltip("Насколько легко отойти от стены, м/с. Меньше — отпускает от меньшего шага в сторону от стены. Вдоль стены не действует")]
        [Min(0.2f)] public float gripBreakSpeed = 1.4f;
        [Tooltip("Насколько сильно хват тянет к стене. 0 — не тянет совсем")]
        [Range(0f, 1f)] public float gripStick = 0.2f;
        [Tooltip("Быстрее этого рука не двигается, м/с. Пока она держит поверхность, ограничение не действует")]
        [Min(0.2f)] public float handMaxSpeed = 1.1f;
        [Tooltip("Дальше этого наклона взгляда вверх или вниз свободные руки не поднимаются и не опускаются, градусы")]
        [Range(5f, 60f)] public float handPitchLimit = 20f;
        [Tooltip("Ближняя плоскость камеры, пока руки видны, м: при взгляде вверх и вниз камера не срезает руки. 0 — не трогать камеру")]
        [Range(0f, 0.2f)] public float handsNearClip = 0.02f;
        [Tooltip("Как быстро камера доворачивается вдоль угла, градусы в секунду")]
        [Min(0f)] public float cornerYawSpeed = 46f;
        [Tooltip("Две руки не держатся ближе друг к другу, м")]
        [Min(0f)] public float minAnchorSeparation = 0.28f;
        [Tooltip("Пауза после отпускания стены до нового касания, с")]
        [Min(0f)] public float regrabDelay = 0.3f;
        [Tooltip("Сколько рука лежит на предмете (не стене), потом перехватывается на ближайшее удобное место, с (от и до)")]
        public Vector2 objectHoldTime = new(0.9f, 1.6f);
        [Tooltip("Пауза перед перехватом предмета, с")]
        [Min(0f)] public float objectRegrabDelay = 0.1f;
        [Tooltip("За сколько рука доходит до найденной точки, с")]
        [Min(0.01f)] public float reachTime = 0.12f;

        [Header("Две руки")]
        [Tooltip("Точка, которую держит рука, ушла на чужую половину взгляда на этот угол: её перехватывает другая рука, градусы")]
        [Range(0f, 60f)] public float handoverAngle = 15f;
        [Tooltip("Другая рука не может перехватить (держит стену или пистолет): рука отпускает после этого угла, градусы")]
        [Range(10f, 120f)] public float handoverMaxAngle = 60f;
        [Tooltip("Перехватившая рука ложится на стену на столько в свою сторону, м")]
        [Min(0f)] public float handoverOffset = 0.12f;
        [Tooltip("Левая ладонь всегда левее правой хотя бы на столько, м")]
        [Min(0f)] public float handGap = 0.14f;

        [Header("Рука на стене тянется к взгляду")]
        [Tooltip("Рука на стене ищет точку ближе к центру взгляда: лицом к стене прижимается, вдоль стены вытягивается вперёд, при повороте от стены распрямляется к взгляду")]
        public bool seekView = true;
        [Tooltip("Как далеко вдоль стены рука ищет точку ближе к взгляду, м")]
        [Min(0.1f)] public float seekRange = 0.9f;
        [Tooltip("Как быстро точка касания скользит к взгляду, м/с")]
        [Min(0.1f)] public float seekSpeed = 1.2f;
        [Tooltip("Удобная высота ладони ниже глаз, м")]
        [Min(0f)] public float seekHeightBelowEyes = 0.3f;

        [Header("Мягкая физика рук")]
        [Tooltip("Кисть и предплечье выталкиваются из стен и предметов")]
        public bool handCollision = true;
        [Tooltip("Толщина кисти с пальцами (радиус капсулы), м")]
        [Min(0.005f)] public float handRadius = 0.035f;
        [Tooltip("Толщина предплечья (радиус капсулы), м")]
        [Min(0.005f)] public float forearmRadius = 0.045f;

        [Header("Поиск")]
        [Tooltip("Радиус сферы, которой рука нащупывает препятствия, м")]
        [Min(0.005f)] public float probeRadius = 0.04f;
        [Tooltip("Слои, которых можно касаться")]
        public LayerMask touchLayers = Physics.DefaultRaycastLayers;

        [Header("Руки")]
        [Tooltip("Модель рук в рукавах. Если пусто, ищется Resources/CarX.DarkOrientation/SleeveArms, а если нет и её, остаются процедурные руки")]
        public GameObject sleeveArmsPrefab;
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
        [Tooltip("Стена: до этого расстояния от точки касания подсветка полной яркости, м")]
        [Min(0.01f)] public float wallZoneCore = 0.15f;
        [Tooltip("Стена: здесь подсветка гаснет полностью, м")]
        [Min(0.02f)] public float wallZoneEnd = 0.4f;
        [Tooltip("Предмет или человек: до этого расстояния подсветка полной яркости, м")]
        [Min(0.01f)] public float objectZoneCore = 0.08f;
        [Tooltip("Предмет или человек: здесь подсветка гаснет полностью, м")]
        [Min(0.02f)] public float objectZoneEnd = 0.2f;
        [Tooltip("Грани, повёрнутые от поверхности касания больше чем на этот угол, не подсвечиваются, градусы")]
        [Range(60f, 180f)] public float faceCutoffAngle = 110f;
        [Min(0.01f)] public float zoneFadeIn = 0.2f;
        [Min(0.01f)] public float zoneFadeOut = 0.4f;
        [Tooltip("Цвет ближних точек карты глубины")]
        public Color depthNear = new(0.72f, 0.75f, 0.8f, 1f);
        [Tooltip("Цвет дальних точек карты глубины")]
        public Color depthFar = new(0.06f, 0.07f, 0.08f, 1f);
        [Tooltip("Подсветка рёбер и перепадов глубины")]
        [Range(0f, 2f)] public float depthEdges = 0.8f;
        [Tooltip("Яркость колец вокруг точки касания")]
        [Range(0f, 1f)] public float depthRings = 0f;
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
