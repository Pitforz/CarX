using UnityEngine;
using UnityEngine.Rendering;

namespace CarX.Gameplay
{
    sealed class PlayerKeyView
    {
        internal const float Duration = 1.92f;
        const float PinchTime = 0.24f;
        const float LiftTime = 0.58f;
        const float LowerTime = 1.51f;

        static readonly Vector3 Reaching = new Vector3(-0.23f, -0.37f, 0.36f);
        static readonly Vector3 Picked = new Vector3(-0.145f, -0.225f, 0.40f);
        static readonly Vector3 Raised = new Vector3(-0.12f, -0.106f, 0.34f);

        readonly Transform _hand;
        readonly Transform _key;
        readonly AnimationClip _pinch;
        readonly float _bladeLength;
        readonly PistolWallClearance _clearance;
        float _elapsed = Duration;

        internal bool IsPlaying => _elapsed < Duration;

        internal PlayerKeyView(GameObject prefab, Camera camera, Transform player, Mesh mesh, Material material)
        {
            _hand = Object.Instantiate(prefab, camera.transform, false).transform;
            _hand.name = "Key Inspection";
            var animation = _hand.GetComponent<Animation>();
            _pinch = animation.clip;
            animation.enabled = false;

            _key = new GameObject("Key").transform;
            _key.SetParent(_hand.Find("Key Grip"), false);
            _key.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = _key.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.renderingLayerMask = PlayerPistol.WeaponRenderingLayer | Flashlight.WeaponSpillRenderingLayer;
            _bladeLength = mesh.bounds.max.x;
            _clearance = new PistolWallClearance(player, _hand);
            _hand.gameObject.SetActive(false);
        }

        internal void Play(float holsterDelay)
        {
            _elapsed = -holsterDelay;
            _hand.gameObject.SetActive(false);
        }

        internal void Advance(float deltaTime)
        {
            if (!IsPlaying)
                return;

            _elapsed += deltaTime;
            if (_elapsed < 0f)
                return;
            if (!IsPlaying)
            {
                Stop();
                return;
            }

            float close = Ease(0f, PinchTime);
            float lift = Ease(PinchTime, LiftTime);
            float lower = Ease(LowerTime, Duration);
            float examine = Ease(LiftTime, 1.04f) * (1f - Ease(1.16f, LowerTime));
            Vector3 position = Vector3.Lerp(Reaching, Picked, close);
            position = Vector3.Lerp(position, Raised, lift);
            position += new Vector3(0.012f, 0.009f, 0.014f) * examine;
            position = Vector3.Lerp(position, Reaching, lower);
            Quaternion rotation = Quaternion.Euler(17f * (1f - lift) + 19f * lower,
                -23f * examine, -13f * (1f - lift) + 12f * examine - 14f * lower);

            _hand.SetLocalPositionAndRotation(position, rotation);
            _hand.gameObject.SetActive(true);
            _key.gameObject.SetActive(_elapsed >= PinchTime);
            _pinch.SampleAnimation(_hand.gameObject, Mathf.Min(_elapsed, _pinch.length));
        }

        float Ease(float start, float end) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, end, _elapsed));

        internal void ApplyClearance()
        {
            if (_hand != null && _hand.gameObject.activeInHierarchy && Time.deltaTime > 0f)
                _clearance.Apply(_key.TransformPoint(new Vector3(_bladeLength, 0f, 0f)), _key.position, Time.deltaTime);
        }

        internal void Stop()
        {
            _elapsed = Duration;
            if (_hand != null)
                _hand.gameObject.SetActive(false);
        }

        internal void Destroy()
        {
            if (_hand != null)
                Object.Destroy(_hand.gameObject);
        }
    }
}
