using UnityEngine;

namespace CarX.Gameplay
{
    sealed class PistolWallClearance
    {
        const float MuzzleRadius = 0.055f;
        const float GripRadius = 0.078f;
        const float WallMargin = 0.012f;
        const float HaltReturnTime = 0.16f;
        const int RetreatPoseSteps = 4;
        const int SearchSteps = 5;

        readonly Transform _player;
        readonly Transform _view;
        readonly Transform _camera;
        readonly Renderer[] _renderers;
        readonly RaycastHit[] _hits = new RaycastHit[16];
        readonly Collider[] _overlaps = new Collider[16];

        float _retreat;
        float _returnVelocity;
        bool _hidden;

        internal PistolWallClearance(Transform player, Transform view)
        {
            _player = player;
            _view = view;
            _camera = view.parent;
            _renderers = view.GetComponentsInChildren<Renderer>(true);
        }

        internal void Apply(Vector3 muzzle, Vector3 grip, float deltaTime)
        {
            // Work with the freshly animated pose, before any wall correction has been applied.
            Vector3 muzzleLocal = _view.InverseTransformPoint(muzzle);
            Vector3 gripLocal = _view.InverseTransformPoint(grip);
            Vector3 position = _view.localPosition;
            Quaternion rotation = _view.localRotation;
            Vector3 tuckedPosition = new Vector3(position.x * 0.25f, position.y - 0.19f, position.z - 0.31f);
            Vector3 cameraOrigin = _camera.position;

            if (!CameraHasRoom(cameraOrigin))
            {
                HideTucked();
                return;
            }

            float requiredRetreat = 0f;
            if (!PoseFits(0f))
            {
                // Check more than one tucked pose. A floor, shelf, or corner can also block the lowest one.
                int step = 1;
                while (step <= RetreatPoseSteps && !PoseFits(step / (float)RetreatPoseSteps))
                    step++;

                if (step > RetreatPoseSteps)
                {
                    HideTucked();
                    return;
                }

                float blocked = (step - 1) / (float)RetreatPoseSteps;
                float clear = step / (float)RetreatPoseSteps;
                for (int i = 0; i < SearchSteps; i++)
                {
                    float middle = (blocked + clear) * 0.5f;
                    if (PoseFits(middle))
                        clear = middle;
                    else
                        blocked = middle;
                }
                requiredRetreat = clear;
            }

            // Never smooth into a wall. Ease back to the aimed pose after the space clears.
            if (_retreat <= requiredRetreat)
            {
                _retreat = requiredRetreat;
                _returnVelocity = 0f;
            }
            else
            {
                _retreat = Mathf.SmoothDamp(_retreat, requiredRetreat, ref _returnVelocity,
                    HaltReturnTime, Mathf.Infinity, deltaTime);
                if (_retreat - requiredRetreat < 0.001f || !PoseFits(_retreat))
                {
                    _retreat = requiredRetreat;
                    _returnVelocity = 0f;
                }
            }

            SetViewPose(_retreat);
            SetHidden(false);

            Vector3 PosePosition(float amount) => Vector3.Lerp(position, tuckedPosition, amount);
            Quaternion PoseRotation(float amount) => rotation * Quaternion.AngleAxis(55f * amount, Vector3.right);

            bool PoseFits(float amount)
            {
                Vector3 localPosition = PosePosition(amount);
                Quaternion localRotation = PoseRotation(amount);
                Vector3 muzzlePosition = _camera.TransformPoint(localPosition + localRotation * muzzleLocal);
                Vector3 gripPosition = _camera.TransformPoint(localPosition + localRotation * gripLocal);
                return PathIsClear(cameraOrigin, muzzlePosition, MuzzleRadius) &&
                    PathIsClear(cameraOrigin, gripPosition, GripRadius) &&
                    PathIsClear(gripPosition, muzzlePosition, MuzzleRadius);
            }

            void SetViewPose(float amount)
            {
                _view.SetLocalPositionAndRotation(PosePosition(amount), PoseRotation(amount));
            }

            void HideTucked()
            {
                _retreat = 1f;
                _returnVelocity = 0f;
                SetViewPose(_retreat);
                SetHidden(true);
            }
        }

        bool CameraHasRoom(Vector3 origin)
        {
            // A sphere cast will not report a collider the cast already overlaps at its origin.
            int count = Physics.OverlapSphereNonAlloc(origin, GripRadius + WallMargin, _overlaps,
                Physics.AllLayers, QueryTriggerInteraction.Ignore);
            if (count == _overlaps.Length)
                return false;
            for (int i = 0; i < count; i++)
                if (IsObstacle(_overlaps[i]))
                    return false;
            return true;
        }

        bool PathIsClear(Vector3 from, Vector3 to, float radius)
        {
            Vector3 offset = to - from;
            float distance = offset.magnitude;
            if (distance < 0.001f)
                return true;

            int count = Physics.SphereCastNonAlloc(from, radius + WallMargin, offset / distance,
                _hits, distance, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            if (count == _hits.Length)
                return false;
            for (int i = 0; i < count; i++)
                if (IsObstacle(_hits[i].collider))
                    return false;
            return true;
        }

        bool IsObstacle(Collider collider) =>
            !collider.transform.IsChildOf(_player) && !collider.transform.IsChildOf(_view);

        void SetHidden(bool hidden)
        {
            if (_hidden == hidden)
                return;
            _hidden = hidden;
            foreach (var renderer in _renderers)
                renderer.forceRenderingOff = hidden;
        }
    }
}
