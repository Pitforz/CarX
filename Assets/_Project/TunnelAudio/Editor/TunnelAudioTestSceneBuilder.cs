using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CarX.TunnelAudio.EditorTools
{
    public static class TunnelAudioTestSceneBuilder
    {
        public const string ScenePath = "Assets/_Project/TunnelAudio/Scenes/TunnelAudio_Test.unity";
        public const string ClipsPath = "Assets/_Project/TunnelAudio/TunnelAudioClips.asset";
        public const string MixerPath = "Assets/_Project/TunnelAudio/Runtime/TunnelAudio.mixer";

        [MenuItem("CarX/Tunnel Audio/Build Test Scene", priority = 50)]
        static void BuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
        }

        public static void Build()
        {
            Directory.CreateDirectory("Assets/_Project/TunnelAudio/Scenes");
            Directory.CreateDirectory("Assets/_Project/TunnelAudio/Runtime");
            var clips = AssetDatabase.LoadAssetAtPath<TunnelAudioClips>(ClipsPath);
            if (clips == null)
            {
                clips = ScriptableObject.CreateInstance<TunnelAudioClips>();
                AssetDatabase.CreateAsset(clips, ClipsPath);
            }
            var mixer = LoadOrCreateMixer();
            var pipe = mixer != null ? FirstGroup(mixer, "Pipe") : null;
            var street = mixer != null ? FirstGroup(mixer, "Street") : null;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.03f, 0.032f, 0.035f);

            var lit = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            lit.color = new Color(0.22f, 0.21f, 0.2f);

            var root = new GameObject("Tunnel Shell").transform;
            Block(root, "Floor", new Vector3(1f, -0.1f, 9f), new Vector3(8f, 0.2f, 20f), lit);
            Block(root, "Ceiling", new Vector3(1f, 2.55f, 9f), new Vector3(8f, 0.2f, 20f), lit);
            Block(root, "Wall West", new Vector3(-1.35f, 1.25f, 9f), new Vector3(0.25f, 2.5f, 20f), lit);
            Block(root, "Wall East A", new Vector3(1.35f, 1.25f, 2.6f), new Vector3(0.25f, 2.5f, 5.2f), lit);
            Block(root, "Wall East B", new Vector3(1.35f, 1.25f, 13.5f), new Vector3(0.25f, 2.5f, 9f), lit);
            Block(root, "Alcove North", new Vector3(2.8f, 1.25f, 5.2f), new Vector3(3f, 2.5f, 0.25f), lit);
            Block(root, "Alcove South", new Vector3(2.8f, 1.25f, 8.8f), new Vector3(3f, 2.5f, 0.25f), lit);
            Block(root, "Alcove East", new Vector3(4.3f, 1.25f, 7f), new Vector3(0.25f, 2.5f, 3.6f), lit);
            Block(root, "Cap Near", new Vector3(0f, 1.25f, -1f), new Vector3(2.7f, 2.5f, 0.25f), lit);
            Block(root, "Cap Far", new Vector3(0f, 1.25f, 19f), new Vector3(2.7f, 2.5f, 0.25f), lit);

            var rig = new GameObject("Listener");
            rig.transform.position = new Vector3(0f, 0.9f, 1.2f);
            var body = rig.AddComponent<CharacterController>();
            body.height = 1.8f;
            body.radius = 0.28f;
            body.center = new Vector3(0f, 0f, 0f);
            var walk = rig.AddComponent<TunnelWalkRig>();

            var ears = new GameObject("Ears");
            ears.transform.SetParent(rig.transform, false);
            ears.transform.localPosition = new Vector3(0f, 0.65f, 0f);
            var camera = ears.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.01f, 0.011f, 0.012f);
            camera.nearClipPlane = 0.08f;
            ears.AddComponent<AudioListener>();
            ears.AddComponent<UniversalAdditionalCameraData>();
            walk.view = ears.transform;

            var audio = new GameObject("Tunnel Audio");
            var director = audio.AddComponent<TunnelAudioDirector>();
            director.clips = clips;
            director.pipeGroup = pipe;
            director.streetGroup = street;

            Section(new Vector3(0f, 1.2f, 4f), new Vector3(2.2f, 2.3f, 8f), TunnelPipeKind.WetNarrow, true, true, false, false);
            Section(new Vector3(0f, 1.2f, 14f), new Vector3(2.2f, 2.3f, 9f), TunnelPipeKind.Hall, false, false, true, true);

            Culvert(new Vector3(0f, 2.35f, 4f), CulvertProfile.Cars, new[]
            {
                new Vector3(-8f, 5f, 4f), new Vector3(-2f, 5f, 4f), new Vector3(4f, 5f, 4f), new Vector3(10f, 5f, 4f),
            });
            Culvert(new Vector3(0f, 2.35f, 11f), CulvertProfile.Heavy, new[]
            {
                new Vector3(-9f, 5.2f, 11f), new Vector3(0f, 5.2f, 11f), new Vector3(9f, 5.2f, 11f),
            });
            Culvert(new Vector3(0f, 2.35f, 15f), CulvertProfile.Silence, null);
            Culvert(new Vector3(0f, 2.35f, 17.2f), CulvertProfile.Casino, null);

            var cry = new GameObject("Living Cry Point");
            cry.transform.position = new Vector3(0f, 1.3f, 6f);
            var crySound = cry.AddComponent<LivingSound>();
            crySound.placement = LivingPlacement.Point;
            crySound.minPause = 7f;

            var nest = new GameObject("Living Random Volume");
            nest.transform.position = new Vector3(0f, 1.1f, 14f);
            var nestBox = nest.AddComponent<BoxCollider>();
            nestBox.isTrigger = true;
            nestBox.size = new Vector3(1.6f, 1.6f, 4f);
            var nestSound = nest.AddComponent<LivingSound>();
            nestSound.placement = LivingPlacement.RandomInVolume;
            nestSound.volume = nestBox;
            nestSound.minPause = 9f;

            var npc = new GameObject("Example NPC Anchor");
            var pacer = npc.AddComponent<TunnelPacer>();
            pacer.from = new Vector3(-0.4f, 0f, 12f);
            pacer.to = new Vector3(0.4f, 0f, 16f);
            pacer.speed = 0.35f;
            var follow = new GameObject("Living Follow NPC");
            var followSound = follow.AddComponent<LivingSound>();
            followSound.placement = LivingPlacement.FollowTransform;
            followSound.follow = npc.transform;
            followSound.minPause = 8f;

            var light = new GameObject("Fill");
            var lamp = light.AddComponent<Light>();
            lamp.type = LightType.Directional;
            lamp.intensity = 0.35f;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("Tunnel audio test scene: " + ScenePath);
        }

        static void Section(Vector3 position, Vector3 size, TunnelPipeKind kind, bool drips, bool falls, bool rats, bool trash)
        {
            var go = new GameObject("Section " + kind);
            go.transform.position = position;
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;
            var section = go.AddComponent<TunnelSection>();
            section.kind = kind;
            section.drips = drips;
            section.falls = falls;
            section.rats = rats;
            section.trash = trash;
        }

        static void Culvert(Vector3 position, CulvertProfile profile, Vector3[] path)
        {
            var go = new GameObject("Culvert " + profile);
            go.transform.position = position;
            var marker = go.AddComponent<CulvertOutlet>();
            marker.profile = profile;
            if (path == null || path.Length == 0) return;
            var lineGo = new GameObject("Traffic " + profile);
            lineGo.transform.SetParent(go.transform, false);
            var line = lineGo.AddComponent<TrafficLine>();
            line.points = new Transform[path.Length];
            for (int i = 0; i < path.Length; i++)
            {
                var point = new GameObject("Point " + i);
                point.transform.position = path[i];
                point.transform.SetParent(lineGo.transform, true);
                line.points[i] = point.transform;
            }
            marker.traffic = line;
        }

        static void Block(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.position = position;
            box.transform.localScale = scale;
            box.GetComponent<Renderer>().sharedMaterial = material;
        }

        public static void EnsureBuses()
        {
            var mixer = LoadOrCreateMixer();
            if (mixer == null) return;
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var director = UnityEngine.Object.FindFirstObjectByType<TunnelAudioDirector>();
            if (director != null)
            {
                director.pipeGroup = FirstGroup(mixer, "Pipe");
                director.streetGroup = FirstGroup(mixer, "Street");
                EditorUtility.SetDirty(director);
            }
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("Pipe " + (director != null && director.pipeGroup != null) + " Street " + (director != null && director.streetGroup != null));
        }

        static AudioMixer LoadOrCreateMixer()
        {
            var existing = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            if (existing != null)
            {
                AddBuses(existing);
                return existing;
            }
            var type = Type.GetType("UnityEditor.Audio.AudioMixerController, UnityEditor.CoreModule")
                ?? Type.GetType("UnityEditor.Audio.AudioMixerController, UnityEditor");
            if (type == null)
            {
                Debug.LogWarning("Tunnel audio: mixer type was not found, sources stay on the default bus");
                return null;
            }
            var create = type.GetMethod("CreateMixerControllerAtPath", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (create == null) return null;
            var mixer = create.Invoke(null, new object[] { MixerPath }) as AudioMixer;
            if (mixer == null) return null;
            AddBuses(mixer);
            return mixer;
        }

        static void AddBuses(AudioMixer mixer)
        {
            if (FirstGroup(mixer, "Pipe") != null && FirstGroup(mixer, "Street") != null) return;
            var type = mixer.GetType();
            var master = type.GetProperty("masterGroup", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(mixer);
            var make = type.GetMethod("CreateNewGroup", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var adopt = type.GetMethod("AddChildToParent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (master == null || make == null || adopt == null)
            {
                Debug.LogWarning("Tunnel audio: could not add Pipe and Street groups");
                return;
            }
            foreach (var groupName in new[] { "Pipe", "Street" })
            {
                if (FirstGroup(mixer, groupName) != null) continue;
                var group = make.Invoke(mixer, new object[] { groupName, false });
                adopt.Invoke(mixer, new object[] { group, master });
            }
            EditorUtility.SetDirty(mixer);
            AssetDatabase.SaveAssets();
        }

        static AudioMixerGroup FirstGroup(AudioMixer mixer, string groupName)
        {
            var found = mixer.FindMatchingGroups(groupName);
            return found != null && found.Length > 0 ? found[0] : null;
        }
    }
}
