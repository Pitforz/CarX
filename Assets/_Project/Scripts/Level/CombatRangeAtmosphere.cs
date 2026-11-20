using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace CarX.Level
{
    [DisallowMultipleComponent]
    public sealed class CombatRangeAtmosphere : MonoBehaviour
    {
        [SerializeField] Bounds _hall = new(new Vector3(0f, 2.2f, 18f), new Vector3(18.6f, 5f, 36.6f));
        [SerializeField] Bounds _cell = new(new Vector3(11.45f, 1.5f, 26.95f), new Vector3(4.8f, 3.1f, 4.8f));
        [SerializeField, Min(0.01f)] float _blendDistance = 2f;
        [SerializeField, Min(0f)] float _fogDensity = 0.009f;

        Volume _volume;
        VolumeProfile _profile;
        Camera _camera;
        float _previousFogDensity;

        void Awake()
        {
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.Add<Vignette>().intensity.Override(0f);
            var grading = _profile.Add<ColorAdjustments>();
            grading.contrast.Override(2f);
            grading.postExposure.Override(0.5f);
            _profile.Add<LiftGammaGain>().lift.Override(new Vector4(1f, 1f, 1f, 0.025f));

            _volume = gameObject.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 30f;
            _volume.weight = 0f;
            _volume.sharedProfile = _profile;
        }

        void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += BeginCamera;
            RenderPipelineManager.endCameraRendering += EndCamera;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            RenderPipelineManager.endCameraRendering -= EndCamera;
            RestoreAtmosphere();
        }

        void OnDestroy()
        {
            if (_volume != null)
                Destroy(_volume);
            if (_profile == null)
                return;

            foreach (var component in _profile.components)
                Destroy(component);
            Destroy(_profile);
        }

        void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (_camera != null || SceneManager.GetActiveScene() != gameObject.scene ||
                (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView))
                return;

            Vector3 localPosition = transform.InverseTransformPoint(camera.transform.position);
            float distance = Mathf.Sqrt(Mathf.Min(_hall.SqrDistance(localPosition), _cell.SqrDistance(localPosition)));
            float weight = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(1f - distance / _blendDistance));
            if (weight <= 0f)
                return;

            // Apply the range settings only for the camera that is inside the range.
            _camera = camera;
            _volume.weight = weight;
            _previousFogDensity = RenderSettings.fogDensity;
            RenderSettings.fogDensity = Mathf.Lerp(_previousFogDensity, Mathf.Min(_fogDensity, _previousFogDensity), weight);
        }

        void EndCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera == _camera)
                RestoreAtmosphere();
        }

        void RestoreAtmosphere()
        {
            if (_volume != null)
                _volume.weight = 0f;
            if (_camera != null)
                RenderSettings.fogDensity = _previousFogDensity;
            _camera = null;
        }
    }
}
