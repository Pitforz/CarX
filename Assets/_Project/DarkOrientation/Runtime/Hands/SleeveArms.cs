using UnityEngine;
using UnityEngine.Rendering;

namespace CarX.DarkOrientation
{
    /// <summary>
    /// Both arms of the free "FPS Arms Pack" with-sleeve model, on one skeleton.
    /// The skinned meshes draw in their own materials (skin and sleeve). A baked copy of each
    /// mesh is what the dark-orientation mask samples, because that pass uses an unskinned shader.
    /// The pack may be used in a personal or commercial project and must not be resold.
    /// </summary>
    public sealed class SleeveArms : MonoBehaviour
    {
        const float TargetArmLength = 0.56f;

        public HandRig Left { get; private set; }
        public HandRig Right { get; private set; }

        SkinnedMeshRenderer[] _skins = System.Array.Empty<SkinnedMeshRenderer>();
        MeshRenderer[] _masks = System.Array.Empty<MeshRenderer>();
        Mesh[] _baked = System.Array.Empty<Mesh>();
        Vector3 _leftShoulderLocal;
        Vector3 _rightShoulderLocal;
        bool _ready;

        public static SleeveArms TryCreate(DarkOrientationSettings settings, Transform parent)
        {
            var prefab = settings != null ? settings.sleeveArmsPrefab : null;
            if (prefab == null)
                prefab = Resources.Load<GameObject>("CarX.DarkOrientation/SleeveArms");
            if (prefab == null) return null;

            var go = Instantiate(prefab, parent);
            go.name = "Sleeve Arms";
            var arms = go.AddComponent<SleeveArms>();
            if (!arms.Setup())
            {
                Debug.LogWarning("CarX DarkOrientation: sleeve arms are missing bones, using procedural hands");
                Destroy(go);
                return null;
            }
            return arms;
        }

        public void Place(Vector3 rightShoulder, Vector3 leftShoulder, Quaternion yaw)
        {
            if (!_ready) return;
            Vector3 scale = transform.localScale;
            Vector3 posR = rightShoulder - yaw * Vector3.Scale(_rightShoulderLocal, scale);
            Vector3 posL = leftShoulder - yaw * Vector3.Scale(_leftShoulderLocal, scale);
            transform.SetPositionAndRotation((posR + posL) * 0.5f, yaw);
        }

        public void SetShown(bool shown)
        {
            for (int i = 0; i < _skins.Length; i++)
                if (_skins[i] != null) _skins[i].enabled = shown;
        }

        public void Bake()
        {
            for (int i = 0; i < _skins.Length; i++)
            {
                if (_skins[i] == null || _baked[i] == null) continue;
                _skins[i].BakeMesh(_baked[i], true);
            }
        }

        public void CollectMasks(System.Collections.Generic.List<Renderer> into)
        {
            for (int i = 0; i < _masks.Length; i++)
                if (_masks[i] != null) into.Add(_masks[i]);
        }

        void OnDestroy()
        {
            for (int i = 0; i < _baked.Length; i++)
                if (_baked[i] != null) Destroy(_baked[i]);
        }

        bool Setup()
        {
            var leftArm = Find("LeftArm");
            var leftFore = Find("LeftForeArm_");
            var leftHand = Find("LeftHand");
            var rightArm = Find("RightArm");
            var rightFore = Find("RightForeArm_");
            var rightHand = Find("RightHand");
            if (leftArm == null || leftFore == null || leftHand == null || rightArm == null || rightFore == null || rightHand == null)
                return false;

            Left = MakeRig(HandSide.Left, leftArm, leftFore, leftHand, "LeftHand");
            Right = MakeRig(HandSide.Right, rightArm, rightFore, rightHand, "RightHand");

            float length = Vector3.Distance(rightArm.position, rightFore.position) + Vector3.Distance(rightFore.position, rightHand.position);
            if (length < 0.25f || length > 0.9f)
                transform.localScale *= TargetArmLength / Mathf.Max(length, 1e-4f);

            _leftShoulderLocal = transform.InverseTransformPoint(leftArm.position);
            _rightShoulderLocal = transform.InverseTransformPoint(rightArm.position);
            Left.Bind();
            Right.Bind();

            _skins = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            _masks = new MeshRenderer[_skins.Length];
            _baked = new Mesh[_skins.Length];
            for (int i = 0; i < _skins.Length; i++)
            {
                var skin = _skins[i];
                skin.updateWhenOffscreen = true;
                skin.shadowCastingMode = ShadowCastingMode.Off;
                skin.allowOcclusionWhenDynamic = false;
                skin.skinnedMotionVectors = false;
                InstanceLit(skin);

                var holder = new GameObject(skin.name + " Mask");
                holder.transform.SetParent(skin.transform, false);
                var mesh = new Mesh { name = skin.name + " Bake" };
                holder.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mask = holder.AddComponent<MeshRenderer>();
                mask.sharedMaterials = skin.sharedMaterials;
                mask.enabled = false;
                mask.shadowCastingMode = ShadowCastingMode.Off;
                mask.receiveShadows = false;
                mask.lightProbeUsage = LightProbeUsage.Off;
                mask.reflectionProbeUsage = ReflectionProbeUsage.Off;
                mask.allowOcclusionWhenDynamic = false;
                _masks[i] = mask;
                _baked[i] = mesh;
            }

            _ready = Left.IsBound && Right.IsBound;
            return _ready;
        }

        HandRig MakeRig(HandSide side, Transform upper, Transform fore, Transform hand, string prefix)
        {
            var go = new GameObject(side + " Rig");
            go.transform.SetParent(transform, false);
            var rig = go.AddComponent<HandRig>();
            rig.side = side;
            rig.upperArm = upper;
            rig.forearm = fore;
            rig.hand = hand;
            rig.thumb = Chain(prefix + "Thumb");
            rig.index = Chain(prefix + "Index");
            rig.middle = Chain(prefix + "Middle");
            rig.ring = Chain(prefix + "Ring");
            rig.little = Chain(prefix + "Pinky");
            rig.renderers = new Renderer[0];
            return rig;
        }

        FingerChain Chain(string prefix)
        {
            var joints = new[] { Find(prefix + "1"), Find(prefix + "2"), Find(prefix + "3") };
            int count = 0;
            while (count < joints.Length && joints[count] != null) count++;
            if (count == 0) return new FingerChain();
            if (count < joints.Length)
            {
                var trimmed = new Transform[count];
                System.Array.Copy(joints, trimmed, count);
                joints = trimmed;
            }
            return new FingerChain { joints = joints, tip = Find(prefix + "4") };
        }

        Transform Find(string boneName)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == boneName) return t;
            return null;
        }

        static void InstanceLit(SkinnedMeshRenderer skin)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var sources = skin.sharedMaterials;
            var mats = new Material[sources.Length];
            for (int i = 0; i < sources.Length; i++)
            {
                var src = sources[i];
                Material mat;
                if (shader != null && (src == null || src.shader == null || src.shader.name.IndexOf("Universal", System.StringComparison.Ordinal) < 0))
                {
                    mat = new Material(shader);
                    var albedo = src != null ? src.mainTexture : null;
                    if (albedo == null && src != null) albedo = src.GetTexture("_BaseMap");
                    var normal = src != null ? src.GetTexture("_BumpMap") : null;
                    if (normal == null && src != null) normal = src.GetTexture("_NormalMap");
                    mat.SetTexture("_BaseMap", albedo);
                    if (normal != null)
                    {
                        mat.SetTexture("_BumpMap", normal);
                        mat.EnableKeyword("_NORMALMAP");
                    }
                }
                else
                {
                    mat = src != null ? new Material(src) : (shader != null ? new Material(shader) : null);
                }
                if (mat == null) continue;
                mat.name = (src != null ? src.name : skin.name) + " Runtime";
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.22f);
                if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
                mats[i] = mat;
            }
            skin.sharedMaterials = mats;
        }
    }
}
