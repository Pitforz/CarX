using System.Collections.Generic;
using System.Text;
using CarX.Level;
using UnityEngine;
using UnityEngine.Audio;

namespace CarX.TunnelAudio
{
    /// <summary>
    /// Reads a tunnel graph and places sound points on top of it.
    /// It does not change nodes, edges, or generated meshes, so the graph can be updated on its own.
    /// Drop it into any scene that has a TunnelNetwork and use "Расставить звук по графу".
    /// </summary>
    [AddComponentMenu("CarX/Tunnel Audio/Graph Binder")]
    [DisallowMultipleComponent]
    public sealed class TunnelAudioGraphBinder : MonoBehaviour
    {
        public const string DemoScenePath = "Assets/_Project/Scenes/TunnelGraph_Demo.unity";
        public const string ClipsPath = "Assets/_Project/TunnelAudio/TunnelAudioClips.asset";
        public const string MixerPath = "Assets/_Project/TunnelAudio/Runtime/TunnelAudio.mixer";

        [Tooltip("Граф, который только читаем")]
        public TunnelNetwork network;
        public TunnelAudioClips clips;
        public AudioMixer mixer;
        public TunnelAudioDirector director;

        [Header("Расстановка")]
        public int seed = 4181;
        [Min(0)] public int mannequins = 18;
        [Tooltip("Примерный шаг между манекенами, метры вдоль хода")]
        [Min(6f)] public float mannequinSpacing = 16f;
        public bool exampleZones = true;
        [Tooltip("Если в сцене ещё нет уха, повесить слушатель на камеру")]
        public bool listenerIfMissing = true;

        const string PointsName = "Точки звука";

        sealed class GrateSpot
        {
            public Vector3 ceiling;
            public Vector3 street;
            public Vector3 along;
            public ShaftType type;
            public CulvertProfile profile;
        }

        readonly List<GrateSpot> _grates = new List<GrateSpot>();
        readonly List<Vector3> _people = new List<Vector3>();
        readonly StringBuilder _log = new StringBuilder();
        Transform _points;
        System.Random _rng;
        float _walked;
        float _nextPerson;
        int _peopleLeft;
        int _sections;
        int _placedPeople;
        bool _hasBounds;
        bool _hasSpawn;
        Vector3 _min, _max, _spawnFloor, _spawnForward;

        [ContextMenu("Расставить звук по графу")]
        public void RebuildFromMenu()
        {
            Debug.Log(Rebuild(), this);
        }

        public string Rebuild()
        {
            if (Application.isPlaying)
                return "Остановите Play. Точки пишутся в сцену, чтобы их можно было править.";
            _log.Clear();
            if (network == null) network = GetComponentInParent<TunnelNetwork>();
            if (network == null) network = UnityEngine.Object.FindFirstObjectByType<TunnelNetwork>();
            if (network == null) return "В сцене нет TunnelNetwork.";

            ResolveAssets();
            EnsureDirector();
            network.EnsureLayout();
            _points = FreshPoints();
            _grates.Clear();
            _people.Clear();
            _sections = 0;
            _placedPeople = 0;
            _hasBounds = false;
            _hasSpawn = false;
            _rng = new System.Random(seed);

            CollectGrates();
            AssignProfiles();
            for (int i = 0; i < _grates.Count; i++) SpawnGrate(_grates[i], i + 1);

            _walked = 0f;
            _nextPerson = 5f + (float)_rng.NextDouble() * 7f;
            _peopleLeft = Mathf.Max(0, mannequins);
            PlaceChains();
            PlaceChambers();
            PlaceRooms();
            if (exampleZones) PlaceZones();
            EnsureEar();
            MarkEdited();

            int cars = 0, heavy = 0, silence = 0, casino = 0, holes = 0;
            for (int i = 0; i < _grates.Count; i++)
            {
                var g = _grates[i];
                if (g.type == ShaftType.Manhole) holes++;
                else if (g.profile == CulvertProfile.Casino) casino++;
                else if (g.profile == CulvertProfile.Heavy) heavy++;
                else if (g.profile == CulvertProfile.Cars) cars++;
                else silence++;
            }
            _log.Insert(0, "Звук разложен по графу, сам граф не менялся. Решёток " + _grates.Count
                + " (казино " + casino + ", легковые " + cars + ", тяжёлые " + heavy + ", тишина " + silence
                + ", люки " + holes + "), участков " + _sections + ", манекенов " + _placedPeople + ".\n");
            return _log.ToString().TrimEnd();
        }

        void ResolveAssets()
        {
#if UNITY_EDITOR
            if (clips == null)
                clips = UnityEditor.AssetDatabase.LoadAssetAtPath<TunnelAudioClips>(ClipsPath);
            if (mixer == null)
                mixer = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
#endif
        }

        void EnsureDirector()
        {
            if (director == null) director = GetComponent<TunnelAudioDirector>();
            if (director == null) director = gameObject.AddComponent<TunnelAudioDirector>();
            if (director.clips == null) director.clips = clips;
            if (mixer == null) return;
            if (director.pipeGroup == null) director.pipeGroup = Group("Pipe");
            if (director.streetGroup == null) director.streetGroup = Group("Street");
        }

        AudioMixerGroup Group(string groupName)
        {
            var found = mixer.FindMatchingGroups(groupName);
            if (found == null) return null;
            for (int i = 0; i < found.Length; i++)
                if (found[i] != null && found[i].name == groupName) return found[i];
            return found.Length > 0 ? found[0] : null;
        }

        Transform FreshPoints()
        {
            var existing = transform.Find(PointsName);
            if (existing != null) Kill(existing.gameObject);
            var root = new GameObject(PointsName);
            root.transform.SetParent(transform, false);
            return root.transform;
        }

        void CollectGrates()
        {
            foreach (var edge in network.edges)
            {
                if (edge == null || edge.shafts == null) continue;
                foreach (var spec in edge.shafts)
                {
                    if (spec == null) continue;
                    Vector3 axis, dir;
                    CarX.Level.TunnelSection shape;
                    if (!network.EdgeFrame(edge, spec.distance, out axis, out dir, out shape)) continue;
                    float height = shape != null ? shape.Height : 2.4f;
                    Vector3 right = Vector3.Cross(Vector3.up, dir);
                    if (right.sqrMagnitude < 1e-6f) right = Vector3.right;
                    else right.Normalize();
                    Vector3 local = axis + Vector3.up * height + right * spec.lateral;
                    Vector3 along = network.transform.TransformDirection(dir);
                    AddGrate(network.World(local), along, spec);
                }
            }
            foreach (var node in network.nodes)
            {
                if (node == null || !node.chamberShaft || node.shaft == null) continue;
                Vector3 along = network.transform.forward;
                TunnelNetwork.ChamberInfo chamber;
                if (network.chambers.TryGetValue(node.id, out chamber))
                    along = network.transform.TransformDirection(Quaternion.Euler(0f, chamber.yaw, 0f) * Vector3.forward);
                float rise = Mathf.Max(2f, node.chamberHeight);
                AddGrate(network.World(node.position + Vector3.up * rise), along, node.shaft);
            }
        }

        void AddGrate(Vector3 ceiling, Vector3 along, ShaftSpec spec)
        {
            for (int i = 0; i < _grates.Count; i++)
                if (Flat(_grates[i].ceiling, ceiling) < 2.25f) return;
            if (along.sqrMagnitude < 1e-6f) along = transform.forward;
            along.Normalize();
            _grates.Add(new GrateSpot
            {
                ceiling = ceiling,
                street = ceiling + network.transform.up * (spec.height + 0.4f),
                along = along,
                type = spec.type,
                profile = CulvertProfile.Silence,
            });
            Enc(ceiling);
        }

        void AssignProfiles()
        {
            var open = new List<int>();
            for (int i = 0; i < _grates.Count; i++)
            {
                if (_grates[i].type == ShaftType.Manhole) _grates[i].profile = CulvertProfile.Silence;
                else open.Add(i);
            }
            if (open.Count == 0) return;
            int casino = open[0];
            int best = int.MaxValue;
            for (int i = 0; i < open.Count; i++)
            {
                int hash = StableHash(_grates[open[i]].ceiling, seed);
                if (hash < best) { best = hash; casino = open[i]; }
            }
            bool anyCars = false;
            for (int i = 0; i < open.Count; i++)
            {
                int index = open[i];
                if (index == casino)
                {
                    _grates[index].profile = CulvertProfile.Casino;
                    continue;
                }
                int bucket = StableHash(_grates[index].ceiling, seed + 17) % 10;
                if (bucket == 0) _grates[index].profile = CulvertProfile.Silence;
                else if (bucket <= 2) _grates[index].profile = CulvertProfile.Heavy;
                else
                {
                    _grates[index].profile = CulvertProfile.Cars;
                    anyCars = true;
                }
            }
            if (anyCars) return;
            for (int i = 0; i < open.Count; i++)
            {
                if (open[i] == casino) continue;
                _grates[open[i]].profile = CulvertProfile.Cars;
                break;
            }
        }

        void SpawnGrate(GrateSpot spot, int index)
        {
            bool hole = spot.type == ShaftType.Manhole;
            string name = hole ? "Люк " + index.ToString("00") : ProfileName(spot.profile, index);
            var go = new GameObject(name);
            go.transform.SetParent(_points, false);
            go.transform.position = spot.ceiling - network.transform.up * 0.22f;
            go.transform.rotation = Quaternion.LookRotation(spot.along, network.transform.up);
            var culvert = go.AddComponent<CulvertOutlet>();
            culvert.profile = spot.profile;
            culvert.hearDistance = hole ? 1.6f : 2.4f;
            culvert.bedVolume = spot.profile == CulvertProfile.Casino ? 0.42f : hole ? 0.18f : 0.35f;
            if (spot.profile == CulvertProfile.Cars || spot.profile == CulvertProfile.Heavy)
                culvert.traffic = SpawnTraffic(go.transform, spot);
            var color = ProfileColor(spot.profile);
            AddMarker(go, name, color, 0.55f);
            AddSphere(go, "Маркер", color, 0.18f);
            _log.Append(name).Append(" @ ").Append(go.transform.position.ToString("0.0")).Append('\n');
        }

        TrafficLine SpawnTraffic(Transform parent, GrateSpot spot)
        {
            bool heavy = spot.profile == CulvertProfile.Heavy;
            var line = new GameObject(heavy ? "Проезд тяжёлых" : "Проезд машин");
            line.transform.SetParent(parent, false);
            var traffic = line.AddComponent<TrafficLine>();
            traffic.heavy = heavy;
            traffic.followDirector = true;
            traffic.speed = heavy ? 6.5f : 9.5f;
            var pts = new Transform[4];
            for (int i = 0; i < 4; i++)
            {
                var point = new GameObject("Точка " + (i + 1));
                point.transform.SetParent(line.transform, false);
                point.transform.position = spot.street + spot.along * Mathf.Lerp(-16f, 16f, i / 3f);
                pts[i] = point.transform;
            }
            traffic.points = pts;
            return traffic;
        }

        void PlaceChains()
        {
            int chain = 0;
            foreach (var c in network.chains)
            {
                if (c == null || c.type == null || c.samples == null || c.samples.Count < 2) continue;
                var shape = c.type.section;
                float height = shape != null ? shape.Height : 2.4f;
                float width = shape != null ? shape.HalfSpan * 2f : 2.4f;
                chain++;
                var kind = KindFor(chain);
                var floors = new List<Vector3>(c.samples.Count);
                var acc = new float[c.samples.Count];
                for (int i = 0; i < c.samples.Count; i++)
                {
                    Vector3 floor = network.World(c.samples[i]);
                    floors.Add(floor);
                    Enc(floor);
                    if (!_hasSpawn)
                    {
                        _hasSpawn = true;
                        _spawnFloor = floor;
                        Vector3 ahead = i + 1 < c.samples.Count ? c.samples[i + 1] - c.samples[i] : Vector3.forward;
                        _spawnForward = network.transform.TransformDirection(ahead);
                    }
                    if (i == 0) continue;
                    acc[i] = acc[i - 1] + Vector3.Distance(floors[i - 1], floor);
                }
                PlaceChunks(floors, acc, width, height, kind, chain);
                OfferPeople(floors, acc, width * 0.5f);
            }
        }

        static TunnelPipeKind KindFor(int chain)
        {
            switch ((chain - 1) % 4)
            {
                case 0: return TunnelPipeKind.WetNarrow;
                case 1: return TunnelPipeKind.Hall;
                case 2: return TunnelPipeKind.DryWide;
                default: return TunnelPipeKind.DeadEnd;
            }
        }

        void PlaceChunks(List<Vector3> floors, float[] acc, float width, float height, TunnelPipeKind kind, int chain)
        {
            float total = acc[acc.Length - 1];
            if (total < 3.5f) return;
            float cursor = 0f;
            int chunk = 0;
            while (cursor < total - 3f && chunk < 80)
            {
                float end = Mathf.Min(total, cursor + 14f);
                if (total - end < 3.5f) end = total;
                SpawnSpan(At(floors, acc, cursor), At(floors, acc, end), width, height, kind,
                    "Участок " + KindLabel(kind) + " " + chain + "-" + chunk);
                if (end >= total - 0.05f) break;
                cursor = end;
                chunk++;
            }
        }

        void OfferPeople(List<Vector3> floors, float[] acc, float halfWidth)
        {
            for (int i = 1; i < floors.Count && _peopleLeft > 0; i++)
            {
                float seg = acc[i] - acc[i - 1];
                float prev = _walked;
                _walked += seg;
                while (_peopleLeft > 0 && _nextPerson <= _walked && seg > 0.05f)
                {
                    float u = Mathf.Clamp01((_nextPerson - prev) / seg);
                    Vector3 floor = Vector3.Lerp(floors[i - 1], floors[i], u);
                    Vector3 fwd = floors[i] - floors[i - 1];
                    TryPerson(floor, fwd, halfWidth);
                    _nextPerson += mannequinSpacing * (0.65f + (float)_rng.NextDouble() * 0.7f);
                }
            }
        }

        void TryPerson(Vector3 floor, Vector3 forward, float halfWidth)
        {
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            if (right.sqrMagnitude < 1e-4f) right = Vector3.right;
            else right.Normalize();
            float side = ((float)_rng.NextDouble() * 2f - 1f) * halfWidth * 0.55f;
            Vector3 at = floor + right * side;
            if (NearFlat(at, 3.5f)) return;
            SpawnPerson(at, forward);
        }

        bool NearFlat(Vector3 point, float radius)
        {
            float limit = radius * radius;
            for (int i = 0; i < _grates.Count; i++)
                if (Flat(_grates[i].ceiling, point) < limit) return true;
            for (int i = 0; i < _people.Count; i++)
                if (Flat(_people[i], point) < limit) return true;
            return false;
        }

        void SpawnPerson(Vector3 floor, Vector3 forward)
        {
            _peopleLeft--;
            _placedPeople++;
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "Манекен " + _placedPeople.ToString("00");
            go.transform.SetParent(_points, false);
            go.transform.position = floor + network.transform.up * 0.9f;
            Vector3 flat = Vector3.ProjectOnPlane(forward, Vector3.up);
            if (flat.sqrMagnitude > 0.01f)
                go.transform.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
            go.transform.localScale = new Vector3(0.42f, 0.86f, 0.42f);
            Kill(go.GetComponent<Collider>());
            var renderer = go.GetComponent<Renderer>();
            var mat = Mat("mannequin", new Color(0.62f, 0.34f, 0.28f));
            if (renderer != null && mat != null)
            {
                renderer.sharedMaterial = mat;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            var living = go.AddComponent<LivingSound>();
            living.placement = LivingPlacement.Point;
            living.chance = 0.72f;
            living.minPause = Mathf.Lerp(4.5f, 9.5f, (float)_rng.NextDouble());
            living.volumeScale = 0.75f;
            living.rareChance = Mathf.Lerp(0.04f, 0.12f, (float)_rng.NextDouble());
            AddMarker(go, go.name, new Color(1f, 0.55f, 0.38f, 1f), 0.6f);
            _people.Add(floor);
        }

        void PlaceChambers()
        {
            foreach (var pair in network.chambers)
            {
                var info = pair.Value;
                if (info == null || info.node == null) continue;
                var node = info.node;
                float height = Mathf.Max(2.2f, node.chamberHeight);
                var size = new Vector3(Mathf.Max(2.2f, node.chamberSize.x), height, Mathf.Max(2.2f, node.chamberSize.y));
                Quaternion rotation = network.transform.rotation * Quaternion.Euler(0f, info.yaw, 0f);
                Vector3 floor = network.World(node.position);
                Enc(floor);
                SpawnBox(floor + network.transform.up * (height * 0.5f), rotation, size, TunnelPipeKind.Hall,
                    "Участок камера", false, false, true, true);
                if (_peopleLeft > 0)
                    TryPerson(floor + rotation * Vector3.right * (size.x * 0.22f), rotation * Vector3.forward, size.x * 0.5f);
            }
        }

        void PlaceRooms()
        {
            foreach (var pair in network.rooms)
            {
                var info = pair.Value;
                if (info == null || info.node == null || info.corners == null || info.corners.Count < 3) continue;
                float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
                for (int i = 0; i < info.corners.Count; i++)
                {
                    var corner = info.corners[i];
                    minX = Mathf.Min(minX, corner.x);
                    maxX = Mathf.Max(maxX, corner.x);
                    minZ = Mathf.Min(minZ, corner.y);
                    maxZ = Mathf.Max(maxZ, corner.y);
                }
                float height = info.height > 0.5f ? info.height : 3.2f;
                Vector3 local = info.node.position + new Vector3((minX + maxX) * 0.5f, height * 0.5f, (minZ + maxZ) * 0.5f);
                var size = new Vector3(Mathf.Max(2f, maxX - minX), height, Mathf.Max(2f, maxZ - minZ));
                Vector3 floor = network.World(info.node.position);
                Enc(floor);
                SpawnBox(network.World(local), network.transform.rotation, size, TunnelPipeKind.Hall,
                    "Участок комната", false, false, true, true);
                if (_peopleLeft > 0)
                    TryPerson(floor, network.transform.forward, Mathf.Min(size.x, size.z) * 0.25f);
            }
        }

        void PlaceZones()
        {
            if (!_hasBounds) return;
            float spanX = Mathf.Max(1f, _max.x - _min.x);
            float spanZ = Mathf.Max(1f, _max.z - _min.z);
            float vx = Mathf.Clamp(spanX * 0.28f, 12f, 36f);
            float vz = Mathf.Clamp(spanZ * 0.28f, 12f, 36f);
            float vy = Mathf.Max(8f, (_max.y - _min.y) + 8f);
            float midY = (_min.y + _max.y) * 0.5f + 1.5f;
            var size = new Vector3(vx, vy, vz);
            SpawnZone("Зона тише", new Vector3(_min.x + vx * 0.5f, midY, _min.z + vz * 0.5f), size, 0,
                new Color(0.45f, 0.65f, 1f, 0.9f), rates =>
                {
                    rates.master = 0.4f;
                    rates.voiceVolume = 0.55f;
                    rates.carVolume = 0.5f;
                    rates.heavyVolume = 0.5f;
                    rates.volumesReady = true;
                });
            SpawnZone("Зона оживлённее", new Vector3(_max.x - vx * 0.5f, midY, _max.z - vz * 0.5f), size, 0,
                new Color(1f, 0.62f, 0.25f, 0.9f), rates =>
                {
                    rates.master = 1.8f;
                    rates.lifeVolume = 1f;
                    rates.volumesReady = true;
                });
            SpawnZone("Зона сырости", new Vector3((_min.x + _max.x) * 0.5f, midY, (_min.z + _max.z) * 0.5f),
                new Vector3(18f, vy, 18f), 2, new Color(0.4f, 0.85f, 0.7f, 0.9f), rates =>
                {
                    rates.dripGap = new Vector2(0.22f, 1.1f);
                    rates.fallGap = new Vector2(25f, 60f);
                    rates.dripVolume = 1f;
                    rates.volumesReady = true;
                });
        }

        void SpawnZone(string name, Vector3 center, Vector3 size, int priority, Color color, System.Action<TunnelAudioRates> edit)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_points, false);
            go.transform.position = center;
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;
            var zone = go.AddComponent<TunnelAudioZone>();
            zone.priority = priority;
            zone.replaceRates = true;
            zone.gizmoColor = color;
            zone.rates = new TunnelAudioRates();
            edit(zone.rates);
            AddMarker(go, name, color, 0.8f);
        }

        void SpawnSpan(Vector3 a, Vector3 b, float width, float height, TunnelPipeKind kind, string name)
        {
            Vector3 delta = b - a;
            float length = delta.magnitude;
            if (length < 3f) return;
            Vector3 fwd = delta / length;
            if (Mathf.Abs(fwd.y) > 0.96f) return;
            var go = new GameObject(name);
            go.transform.SetParent(_points, false);
            go.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            go.transform.position = (a + b) * 0.5f + go.transform.up * (height * 0.5f);
            bool drips = kind == TunnelPipeKind.WetNarrow || kind == TunnelPipeKind.DeadEnd;
            bool falls = kind == TunnelPipeKind.DeadEnd;
            bool rats = kind == TunnelPipeKind.Hall;
            bool trash = kind == TunnelPipeKind.Hall || kind == TunnelPipeKind.DryWide;
            FinishSection(go, new Vector3(Mathf.Max(1.3f, width * 0.94f), Mathf.Max(1.5f, height * 0.94f), length),
                kind, drips, falls, rats, trash);
        }

        void SpawnBox(Vector3 center, Quaternion rotation, Vector3 size, TunnelPipeKind kind, string name, bool drips, bool falls, bool rats, bool trash)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_points, false);
            go.transform.SetPositionAndRotation(center, rotation);
            FinishSection(go, size, kind, drips, falls, rats, trash);
        }

        void FinishSection(GameObject go, Vector3 size, TunnelPipeKind kind, bool drips, bool falls, bool rats, bool trash)
        {
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;
            var section = go.AddComponent<TunnelSection>();
            section.kind = kind;
            section.drips = drips;
            section.falls = falls;
            section.rats = rats;
            section.trash = trash;
            section.eventVolume = 0.7f;
            AddMarker(go, go.name, Color.white, 0.2f);
            _sections++;
        }

        void EnsureEar()
        {
            if (!listenerIfMissing) return;
            if (UnityEngine.Object.FindFirstObjectByType<AudioListener>() != null)
            {
                _log.Append("Ухо в сцене уже есть, второй слушатель не ставился.\n");
                return;
            }
            var camera = UnityEngine.Object.FindFirstObjectByType<Camera>();
            if (camera != null)
            {
                camera.gameObject.AddComponent<AudioListener>();
                _log.Append("На камеру ").Append(camera.name).Append(" добавлен AudioListener.\n");
                return;
            }
            if (!_hasSpawn) return;
            var rig = new GameObject("Слушатель звука");
            rig.transform.SetParent(transform, false);
            rig.transform.position = _spawnFloor + Vector3.up * 0.9f;
            Vector3 flat = Vector3.ProjectOnPlane(_spawnForward, Vector3.up);
            if (flat.sqrMagnitude > 0.01f)
                rig.transform.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
            var body = rig.AddComponent<CharacterController>();
            body.height = 1.8f;
            body.radius = 0.28f;
            var walk = rig.AddComponent<TunnelWalkRig>();
            var ears = new GameObject("Ears");
            ears.transform.SetParent(rig.transform, false);
            ears.transform.localPosition = new Vector3(0f, 0.65f, 0f);
            var view = ears.AddComponent<Camera>();
            view.nearClipPlane = 0.08f;
            ears.AddComponent<AudioListener>();
            walk.view = ears.transform;
            var urp = System.Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
            if (urp != null) ears.AddComponent(urp);
            _log.Append("Своего игрока в сцене не было: поставлен слушатель WASD у входа.\n");
        }

        void AddMarker(GameObject go, string label, Color color, float radius)
        {
            var marker = go.AddComponent<TunnelAudioMarker>();
            marker.label = label;
            marker.color = color;
            marker.radius = radius;
        }

        void AddSphere(GameObject parent, string name, Color color, float scale)
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = name;
            sphere.transform.SetParent(parent.transform, false);
            sphere.transform.localScale = Vector3.one * scale;
            Kill(sphere.GetComponent<Collider>());
            var renderer = sphere.GetComponent<Renderer>();
            var mat = Mat(ColorKey(color), color);
            if (renderer != null && mat != null)
            {
                renderer.sharedMaterial = mat;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        Material Mat(string key, Color color)
        {
#if UNITY_EDITOR
            const string folder = "Assets/_Project/TunnelAudio/Markers";
            if (!UnityEditor.AssetDatabase.IsValidFolder(folder))
                UnityEditor.AssetDatabase.CreateFolder("Assets/_Project/TunnelAudio", "Markers");
            string path = folder + "/" + key + ".mat";
            var mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) return null;
            mat = new Material(shader) { name = key };
            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            UnityEditor.AssetDatabase.CreateAsset(mat, path);
            return mat;
#else
            return null;
#endif
        }

        static string ColorKey(Color color)
        {
            return "marker-" + Mathf.RoundToInt(color.r * 255f) + "-" + Mathf.RoundToInt(color.g * 255f) + "-" + Mathf.RoundToInt(color.b * 255f);
        }

        static string ProfileName(CulvertProfile profile, int index)
        {
            switch (profile)
            {
                case CulvertProfile.Casino: return "Решётка казино";
                case CulvertProfile.Heavy: return "Решётка тяжёлые " + index.ToString("00");
                case CulvertProfile.Silence: return "Решётка тишина " + index.ToString("00");
                default: return "Решётка легковые " + index.ToString("00");
            }
        }

        static Color ProfileColor(CulvertProfile profile)
        {
            switch (profile)
            {
                case CulvertProfile.Casino: return new Color(0.9f, 0.28f, 0.75f);
                case CulvertProfile.Heavy: return new Color(1f, 0.46f, 0.12f);
                case CulvertProfile.Silence: return new Color(0.62f, 0.64f, 0.68f);
                default: return new Color(0.25f, 0.78f, 1f);
            }
        }

        static string KindLabel(TunnelPipeKind kind)
        {
            switch (kind)
            {
                case TunnelPipeKind.WetNarrow: return "мокрый";
                case TunnelPipeKind.Hall: return "зал";
                case TunnelPipeKind.DeadEnd: return "тупик";
                default: return "сухой";
            }
        }

        static Vector3 At(List<Vector3> floors, float[] acc, float distance)
        {
            for (int i = 1; i < floors.Count; i++)
            {
                if (acc[i] + 0.0001f < distance) continue;
                float span = acc[i] - acc[i - 1];
                float u = span > 1e-4f ? (distance - acc[i - 1]) / span : 0f;
                return Vector3.Lerp(floors[i - 1], floors[i], Mathf.Clamp01(u));
            }
            return floors[floors.Count - 1];
        }

        void Enc(Vector3 point)
        {
            if (!_hasBounds) { _min = _max = point; _hasBounds = true; return; }
            _min = Vector3.Min(_min, point);
            _max = Vector3.Max(_max, point);
        }

        static float Flat(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0f;
            return (a - b).sqrMagnitude;
        }

        static int StableHash(Vector3 point, int salt)
        {
            unchecked
            {
                int hash = salt * 374761393;
                hash = hash * 31 + Mathf.RoundToInt(point.x * 10f);
                hash = hash * 31 + Mathf.RoundToInt(point.y * 10f);
                hash = hash * 31 + Mathf.RoundToInt(point.z * 10f);
                hash ^= hash >> 13;
                hash *= 1274126177;
                return hash & int.MaxValue;
            }
        }

        static void Kill(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
            else UnityEngine.Object.DestroyImmediate(obj);
        }

        void MarkEdited()
        {
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
            if (director != null) UnityEditor.EditorUtility.SetDirty(director);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("CarX/Tunnel Audio/Attach To Tunnel Graph Demo", priority = 51)]
        static void AttachDemoMenu()
        {
            if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Debug.Log(AttachToScene(DemoScenePath));
        }

        [UnityEditor.MenuItem("CarX/Tunnel Audio/Attach To Open Scene", priority = 52)]
        static void AttachOpenMenu()
        {
            Debug.Log(AttachToOpenScene());
        }

        public static string AttachToScene(string scenePath)
        {
            var active = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            if (active.path != scenePath)
            {
                if (active.isDirty) UnityEditor.SceneManagement.EditorSceneManager.SaveScene(active);
                active = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
            }
            string report = AttachToOpenScene();
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(active);
            UnityEditor.AssetDatabase.SaveAssets();
            return report;
        }

        public static string AttachToOpenScene()
        {
            var network = UnityEngine.Object.FindFirstObjectByType<TunnelNetwork>();
            if (network == null) return "В открытой сцене нет TunnelNetwork.";
            var binder = UnityEngine.Object.FindFirstObjectByType<TunnelAudioGraphBinder>();
            if (binder == null)
            {
                var go = new GameObject("Tunnel Audio");
                binder = go.AddComponent<TunnelAudioGraphBinder>();
            }
            binder.network = network;
            return binder.Rebuild();
        }
#endif
    }
}

#if UNITY_EDITOR
namespace CarX.TunnelAudio
{
    using UnityEngine;

    [UnityEditor.CustomEditor(typeof(TunnelAudioGraphBinder))]
    sealed class TunnelAudioGraphBinderEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (!GUILayout.Button("Расставить звук по графу")) return;
            var binder = (TunnelAudioGraphBinder)target;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (binder != null) Debug.Log(binder.Rebuild(), binder);
            };
        }
    }
}
#endif
