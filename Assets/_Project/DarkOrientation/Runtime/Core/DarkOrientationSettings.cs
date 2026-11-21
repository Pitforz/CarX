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
        [Tooltip("Время, за которое руки поднимаются (уже в темноте), с")]
        [Min(0.05f)] public float activateTime = 0.4f;
        [Tooltip("Время, за которое темнота и контур проявляются при включении, до подъёма рук, с")]
        [Min(0.05f)] public float effectTime = 0.55f;
        [Tooltip("Доля проявления темноты, после которой поднимаются руки: текстуру рук не видно, только контур")]
        [Range(0.5f, 1f)] public float handsAfterEffect = 0.95f;
        [Tooltip("Время, за которое руки опускаются при выключении; темнота уходит после них за то же время, с")]
        [Min(0.05f)] public float deactivateTime = 0.35f;
        [Tooltip("Доля темноты, при которой пистолет с его руками убирается (при включении) и возвращается (при выключении)")]
        [Range(0.05f, 1f)] public float weaponHideBlend = 0.5f;

        [Tooltip("На сколько градусов шире поле зрения в режиме (0 — не менять)")]
        [Range(0f, 60f)] public float modeFovBonus = 20f;
        [Tooltip("Время, за которое поле зрения расширяется и возвращается, с (быстрее темноты и рук)")]
        [Min(0.01f)] public float modeFovTime = 0.25f;

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
        [Tooltip("Слои, которых можно касаться. Невидимая лента стен тоннеля (слой 31) добавляется всегда сама")]
        public LayerMask touchLayers = Physics.DefaultRaycastLayers;

        /// <summary>Layers the project always adds (the invisible touch band of the tunnels), whatever touchLayers says.</summary>
        [System.NonSerialized] public int extraTouchLayers;
        public int TouchMask => touchLayers.value | extraTouchLayers;

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
        [Tooltip("Общий масштаб всех зон касания (стены и предметы): 2 — вдвое шире круг вокруг руки")]
        [Range(0.25f, 5f)] public float zoneScale = 1f;
        [Tooltip("Цвет ближних точек карты глубины")]
        public Color depthNear = new(0.72f, 0.75f, 0.8f, 1f);
        [Tooltip("Цвет дальних точек карты глубины")]
        public Color depthFar = new(0.06f, 0.07f, 0.08f, 1f);
        [Tooltip("Карта глубины в зоне касания: на этом расстоянии от камеры и ближе — цвет ближних точек, м")]
        [Min(0f)] public float depthNearDistance = 0.4f;
        [Tooltip("Карта глубины: на этом расстоянии от камеры и дальше — цвет дальних точек, м")]
        [Min(0.1f)] public float depthFarDistance = 2.2f;
        [Tooltip("Изгиб карты глубины: 1 — равномерно, больше 1 — темнеет быстрее, меньше 1 — дольше светлое")]
        [Range(0.2f, 4f)] public float depthCurve = 1.3f;
        [Tooltip("Насколько грани, повёрнутые к камере боком, темнее (0 — не темнеют)")]
        [Range(0f, 1f)] public float depthSideShade = 0.45f;
        [Tooltip("Мягкость слияния двух зон касания (0 — по максимуму, 1 — сумма, без шва)")]
        [Range(0f, 1f)] public float zoneMerge = 1f;
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

        [Header("Рука: подъём, поза и поиск")]
        [Tooltip("Рука начинает искать опору, когда поднялась выше этой доли")]
        [Range(0f, 1f)] public float searchStartPresence = 0.6f;
        [Tooltip("Время, за которое рука прижимается к найденной опоре, с")]
        [Min(0.01f)] public float settleInTime = 0.55f;
        [Tooltip("Время, за которое рука отпускает опору и возвращается к поиску, с")]
        [Min(0.01f)] public float settleOutTime = 0.42f;
        [Tooltip("Доля прижатия, после которой рука стоит на точке и не идёт за камерой")]
        [Range(0.5f, 1f)] public float gluedSettle = 0.92f;
        [Tooltip("Скорость поворота кисти, градусов в секунду")]
        [Min(10f)] public float handTurnSpeed = 240f;
        [Tooltip("Опущенная рука относительно плеча: вправо (зеркально для левой), вверх, вперёд, м")]
        public Vector3 loweredOffset = new(0.12f, -0.48f, 0.2f);
        [Tooltip("Куда сгибается локоть при работе: вправо (зеркально для левой), вверх, вперёд от плеча, м")]
        public Vector3 elbowPole = new(0.35f, -0.6f, -0.05f);
        [Tooltip("Рука убрана (режим выключен): положение кисти от плеча, м")]
        public Vector3 parkedOffset = new(0.2f, -0.7f, -0.16f);
        [Tooltip("Рука убрана: куда смотрит локоть от плеча, м")]
        public Vector3 parkedPole = new(0.4f, -0.9f, -0.2f);
        [Tooltip("Сжатие пальцев убранной руки")]
        [Range(0f, 1f)] public float parkedCurl = 0.45f;
        [Tooltip("Когда кисть ближе к камере, чем minCameraDistance: какая доля недостачи уходит вперёд")]
        [Range(0f, 1f)] public float nearPushForward = 0.35f;
        [Tooltip("То же: какая доля уходит в сторону, от лица")]
        [Range(0f, 1f)] public float nearPushSide = 0.6f;
        [Tooltip("Зазор кисти от поверхности, когда опора в пределах palmReach, м")]
        [Min(0f)] public float palmGapNear = 0.06f;
        [Tooltip("Зазор кисти от поверхности, когда опора дальше palmReach, м")]
        [Min(0f)] public float palmGapFar = 0.045f;
        [Tooltip("Рука в поиске: скорость кругов (обороты примерно, рад/с)")]
        [Min(0f)] public float searchSpeed = 1.85f;
        [Tooltip("Рука в поиске: радиус круга, м")]
        [Min(0f)] public float searchRadius = 0.09f;
        [Tooltip("Рука в поиске: насколько в сторону от центра взгляда, м")]
        [Min(0f)] public float searchSide = 0.32f;
        [Tooltip("Рука в поиске: размах влево и вправо, м")]
        [Min(0f)] public float searchSwing = 0.16f;
        [Tooltip("Рука в поиске: расстояние перед камерой, м")]
        [Min(0.1f)] public float searchForward = 0.62f;

        [Header("Пальцы")]
        [Tooltip("Постоянная времени сгибания пальцев, с")]
        [Min(0.01f)] public float fingerSmooth = 0.08f;
        [Tooltip("Сжатие пальцев в поиске")]
        [Range(0f, 1f)] public float idleCurl = 0.16f;
        [Tooltip("Большой палец у стены: сжатие вдоль стены")]
        [Range(0f, 1f)] public float thumbCurlAlong = 0.22f;
        [Tooltip("Большой палец у стены: сжатие, когда игрок смотрит в стену")]
        [Range(0f, 1f)] public float thumbCurlFacing = 0.06f;
        [Tooltip("Остальные пальцы у стены: сжатие вдоль стены")]
        [Range(0f, 1f)] public float fingerCurlAlong = 0.2f;
        [Tooltip("Остальные пальцы у стены: сжатие, когда игрок смотрит в стену")]
        [Range(0f, 1f)] public float fingerCurlFacing = 0f;

        [Header("Касание и скольжение")]
        [Tooltip("Стеной считается грань с |normal.y| не больше этого (0 — строго вертикальная)")]
        [Range(0f, 1f)] public float wallMaxNormalY = 0.42f;
        [Tooltip("Предмет можно трогать, если normal.y грани не меньше этого (-1 — со всех сторон, 0 — не снизу)")]
        [Range(-1f, 1f)] public float objectMinNormalY = -0.5f;
        [Tooltip("Если обе руки на одной грани и точки ближе minAnchorSeparation, вторая не берёт: угол «та же грань», градусов")]
        [Range(0f, 90f)] public float sameFaceAngle = 28f;
        [Tooltip("Кисть берёт новую точку только дальше этого от оси взгляда в свою сторону, м")]
        [Range(0f, 0.5f)] public float handSideMargin = 0.02f;
        [Tooltip("Рука не проходит сквозь твёрдое между камерой и кистью: зазор перед поверхностью, м")]
        [Min(0f)] public float keepOffWallsGap = 0.07f;
        [Tooltip("Скольжение: самый большой поворот грани на шаге, градусов")]
        [Range(1f, 90f)] public float slideAngle = 50f;
        [Tooltip("Скольжение: дальность луча к стене, м")]
        [Min(0.1f)] public float slideProbeDistance = 0.8f;
        [Tooltip("Скольжение: на сколько луч отстоит от стены перед броском, м")]
        [Min(0.05f)] public float slideProbeLift = 0.35f;
        [Tooltip("Угол: точка считается следующей гранью (поворот), градусов")]
        [Range(1f, 90f)] public float cornerTurnAngle = 32f;
        [Tooltip("Проверка угла: на сколько вперёд от точки касания смотрим, м")]
        [Min(0.05f)] public float cornerProbeAhead = 0.45f;
        [Tooltip("Проверка угла: на сколько луч отстоит от стены, м")]
        [Min(0.05f)] public float cornerProbeOut = 0.35f;
        [Tooltip("Проверка угла: дальность луча, м")]
        [Min(0.1f)] public float cornerProbeDistance = 0.9f;
        [Tooltip("Проверка угла: допустимый поворот грани, градусов")]
        [Range(1f, 90f)] public float cornerProbeAngle = 22f;
        [Tooltip("Взгляд: сколько шагов вдоль стены в каждую сторону пробуется")]
        [Range(1, 30)] public int seekSteps = 10;

        [Header("Игрок у стены")]
        [Tooltip("Хват начинает тянуть игрока назад, когда он отходит от стены быстрее этого, м/с")]
        [Min(0f)] public float gripMinLeave = 0.2f;
        [Tooltip("Угол в тоннеле доворачивает камеру, если игрок идёт вперёд быстрее этой доли")]
        [Range(0f, 1f)] public float cornerMinForward = 0.35f;
        [Tooltip("Один поворот камеры доворачивает не больше, градусов")]
        [Range(10f, 180f)] public float cornerMaxTurn = 80f;
        [Tooltip("Передача хвата: дальность луча при сдвиге точки, м")]
        [Min(0.1f)] public float handoverProbeDistance = 0.6f;
        [Tooltip("Передача хвата: насколько может отличаться грань новой точки, градусов")]
        [Range(1f, 90f)] public float handoverMaxNormalAngle = 40f;

        [Header("Что считается стеной")]
        [Tooltip("Если проект не говорит иначе: твёрдое без rigidbody не ниже этого считается стеной, м")]
        [Min(0.1f)] public float wallMinHeight = 1.35f;
        [Tooltip("Если проект не говорит иначе: и не уже этого, м")]
        [Min(0.1f)] public float wallMinWidth = 0.85f;

        [Header("Мягкая физика: детали")]
        [Tooltip("Сколько раз за кадр рука выталкивается из твёрдого")]
        [Range(1, 8)] public int softPasses = 3;
        [Tooltip("Длина предплечья в физике, м")]
        [Min(0.05f)] public float forearmLength = 0.24f;

        [Header("Оболочка стен (строится при загрузке сцены, правка работает со следующего запуска)")]
        [Tooltip("Нижний край ленты касания над полом, м")]
        [Min(0f)] public float shellBandLow = 0.7f;
        [Tooltip("Верхний край ленты касания над полом, м")]
        [Min(0.2f)] public float shellBandHigh = 1.9f;
        [Tooltip("Шаг лучей вдоль стены, м (меньше — точнее и тяжелее)")]
        [Min(0.05f)] public float shellStep = 0.25f;
        [Tooltip("Перепад глубины между соседями, после которого лента рвётся (проём, колонна), м")]
        [Min(0.01f)] public float shellMaxJump = 0.2f;
        [Tooltip("Комнаты и развилки: лучи начинаются на столько внутри стены, м")]
        [Min(0.05f)] public float shellInset = 0.6f;
        [Tooltip("Строить упрощённую коллизию игрока (стены, пол, потолок)")]
        public bool shellCollision = true;
        [Tooltip("Коллизия игрока: прямые вертикальные стены по основной плоскости стены, косые фаски внизу и вверху не мешают подойти в угол. Пол ровный поперёк тоннеля")]
        public bool collisionStraightWalls = true;
        [Tooltip("Коллизия игрока: на этой высоте над полом меряется основная плоскость стены (между нижней и верхней фаской), м")]
        [Min(0.2f)] public float collisionWallHeight = 1.2f;

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
