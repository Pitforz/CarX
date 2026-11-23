using System.Collections.Generic;
using UnityEngine;

namespace CarX.NPC
{
    [DisallowMultipleComponent]
    public sealed class ResidentModelPose : MonoBehaviour
    {
        const int Torso = 0, Head = 1, LeftShoulder = 2, LeftElbow = 3, LeftWrist = 4,
            RightShoulder = 5, RightElbow = 6, RightWrist = 7, LeftHip = 8, LeftKnee = 9,
            RightHip = 10, RightKnee = 11, Chest = 12, LeftAnkle = 13, RightAnkle = 14;

        [SerializeField] bool _relaxedPreview;
        [SerializeField] TextAsset _ambientAnimation;
        [SerializeField] TextAsset _standingAnimation;
        [SerializeField] TextAsset _lookAroundAnimation;
        [SerializeField, Min(0f)] float _idleHandClearance;
        [SerializeField] Vector3 _mouthCenter;
        [SerializeField] bool _seated;
        [SerializeField] bool _sleeping;

        [System.Serializable]
        sealed class AmbientAnimation
        {
            public float duration;
            public float strideLength;
            public float contact;
            public Vector3[] pelvis;
            public Quaternion[] rotations;
        }

        static readonly Dictionary<TextAsset, AmbientAnimation> Animations = new();
        readonly Transform[] _bones = new Transform[15];
        readonly List<Mesh> _meshes = new();
        readonly List<(SkinnedMeshRenderer renderer, int firstShape)> _handMeshes = new();
        Camera _listener;
        bool _attention;
        float _nextAttentionPoll;
        bool _meleeDriven;
        bool _holdingKnife;
        bool _poseInitialized;
        float _gait;
        float _stride;
        float _animatedSpeed;
        float _guard;
        float _leftGrip = 0.32f;
        float _rightGrip = 0.32f;
        float _headYaw;
        float _headPitch;
        float _glanceUntil;
        AmbientAnimation _ambient;
        AmbientAnimation _standing;
        AmbientAnimation _lookAround;
        float _lookAroundTime;
        float _lookAroundWeight;
        float _nextLookAround;
        AmbientAnimation _attack;
        Vector3 _pelvisRest;
        float _ambientTime;
        float _standingTime;
        float _walkBlend;
        float _walkSpeed;

        void Awake()
        {
            _ambient = LoadAnimation(_ambientAnimation);
            _standing = LoadAnimation(_standingAnimation);
            _lookAround = LoadAnimation(_lookAroundAnimation);
            if (_lookAround != null)
            {
                _lookAroundTime = _lookAround.duration;
                _nextLookAround = Time.time + Random.Range(5f, 9f);
            }
            var pelvis = new GameObject("Pelvis Pose").transform;
            pelvis.SetParent(transform, false);
            _pelvisRest = Vector3.up * 0.95f;
            pelvis.localPosition = _pelvisRest;
            _bones[Torso] = pelvis;
            AddBone(Chest, Torso, "Chest", new Vector3(0f, 1.12f, -0.02f));
            AddBone(Head, Chest, "Head", new Vector3(0f, 1.56f, -0.03f));
            BuildSide(-1f, LeftShoulder, LeftHip, LeftAnkle);
            BuildSide(1f, RightShoulder, RightHip, RightAnkle);

            foreach (var filter in GetComponentsInChildren<MeshFilter>())
                if (filter.TryGetComponent<MeshRenderer>(out var source) && filter.sharedMesh != null && filter.sharedMesh.isReadable)
                    Skin(filter, source);
            PoseMelee(0f, -1f, 0f, 0f, false, false);
            ApplyAmbientAnimation(0f);
        }

        void BuildSide(float side, int shoulder, int hip, int ankle)
        {
            AddBone(shoulder, Chest, "Shoulder", new Vector3(side * 0.17f, 1.42f, -0.07f));
            AddBone(shoulder + 1, shoulder, "Elbow", new Vector3(side * (_relaxedPreview ? 0.49f : 0.46f), 1.41f, -0.09f));
            AddBone(shoulder + 2, shoulder + 1, "Wrist", new Vector3(side * (_relaxedPreview ? 0.82f : 0.73f), 1.40f, -0.07f));
            AddBone(hip, Torso, "Hip", new Vector3(side * 0.09f, 0.95f, -0.015f));
            AddBone(hip + 1, hip, "Knee", new Vector3(side * 0.10f, 0.52f, -0.03f));
            AddBone(ankle, hip + 1, "Ankle", new Vector3(side * 0.09f, 0.085f, -0.055f));
        }

        void AddBone(int index, int parent, string boneName, Vector3 modelPosition)
        {
            var bone = new GameObject(boneName).transform;
            bone.SetPositionAndRotation(transform.TransformPoint(modelPosition), transform.rotation);
            bone.SetParent(_bones[parent], true);
            _bones[index] = bone;
        }

        void Skin(MeshFilter filter, MeshRenderer source)
        {
            var mesh = Instantiate(filter.sharedMesh);
            var vertices = mesh.vertices;
            var weights = new BoneWeight[vertices.Length];
            Matrix4x4 toModel = transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            for (int i = 0; i < vertices.Length; i++)
                weights[i] = Weights(toModel.MultiplyPoint3x4(vertices[i]));
            int firstGripShape = _relaxedPreview ? -1 : AddGripShapes(mesh, vertices, toModel);
            if (_mouthCenter != Vector3.zero)
                AddSpeechShapes(mesh, vertices, toModel);

            var bindPoses = new Matrix4x4[_bones.Length];
            for (int i = 0; i < _bones.Length; i++)
                bindPoses[i] = _bones[i].worldToLocalMatrix * filter.transform.localToWorldMatrix;
            mesh.bindposes = bindPoses;
            mesh.boneWeights = weights;
            _meshes.Add(mesh);

            var skin = new GameObject("Skinned " + filter.name) { layer = source.gameObject.layer };
            skin.transform.SetParent(filter.transform, false);
            var renderer = skin.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.sharedMaterials = source.sharedMaterials;
            renderer.bones = _bones;
            renderer.rootBone = transform;
            renderer.localBounds = new Bounds(Vector3.up * 0.9f, new Vector3(2.2f, 2.5f, 2.2f));
            renderer.renderingLayerMask = source.renderingLayerMask;
            renderer.shadowCastingMode = source.shadowCastingMode;
            renderer.receiveShadows = source.receiveShadows;
            if (firstGripShape >= 0)
                _handMeshes.Add((renderer, firstGripShape));
            source.enabled = false;
        }

        void AddSpeechShapes(Mesh mesh, Vector3[] vertices, Matrix4x4 toModel)
        {
            var open = new Vector3[vertices.Length];
            var round = new Vector3[vertices.Length];
            var spread = new Vector3[vertices.Length];
            var closed = new Vector3[vertices.Length];
            Matrix4x4 toMesh = toModel.inverse;
            bool hasMouth = false;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 p = toModel.MultiplyPoint3x4(vertices[i]) - _mouthCenter;
                float mask = (1f - EaseRange(0.019f, 0.045f, Mathf.Abs(p.x))) *
                    (1f - EaseRange(0.009f, 0.025f, Mathf.Abs(p.y))) *
                    EaseRange(-0.029f, -0.004f, p.z);
                if (mask <= 0f)
                    continue;

                hasMouth = true;
                float lowerLip = 1f - EaseRange(-0.003f, 0.004f, p.y);
                open[i] = toMesh.MultiplyVector(new Vector3(-p.x * 0.05f,
                    Mathf.Lerp(0.002f, -0.017f, lowerLip), -0.003f * lowerLip) * mask);
                round[i] = toMesh.MultiplyVector(new Vector3(-0.35f * p.x, -0.14f * p.y, 0.005f) * mask);
                spread[i] = toMesh.MultiplyVector(new Vector3(0.22f * p.x, 0.08f * Mathf.Abs(p.x), -0.002f) * mask);
                closed[i] = toMesh.MultiplyVector(new Vector3(0f, -0.28f * p.y, -0.002f) * mask);
            }

            if (!hasMouth)
                return;
            mesh.AddBlendShapeFrame("CarX_MouthOpen", 100f, open, null, null);
            mesh.AddBlendShapeFrame("CarX_MouthRound", 100f, round, null, null);
            mesh.AddBlendShapeFrame("CarX_MouthSpread", 100f, spread, null, null);
            mesh.AddBlendShapeFrame("CarX_MouthClosed", 100f, closed, null, null);
        }

        static float EaseRange(float from, float to, float value) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, value));

        static int AddGripShapes(Mesh mesh, Vector3[] vertices, Matrix4x4 toModel)
        {
            Vector3[] left = null;
            Vector3[] right = null;
            Matrix4x4 toMesh = toModel.inverse;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 point = toModel.MultiplyPoint3x4(vertices[i]);
                if (Mathf.Abs(point.x) < 0.77f || point.y < 1.27f || point.y > 1.49f)
                    continue;

                left ??= new Vector3[vertices.Length];
                right ??= new Vector3[vertices.Length];
                Vector3 delta = toMesh.MultiplyVector(CloseHand(point) - point);
                if (point.x < 0f)
                    left[i] = delta;
                else
                    right[i] = delta;
            }

            if (left == null)
                return -1;

            int firstShape = mesh.blendShapeCount;
            mesh.AddBlendShapeFrame("Left grip", 100f, left, null, null);
            mesh.AddBlendShapeFrame("Right grip", 100f, right, null, null);
            return firstShape;
        }

        static Vector3 CloseHand(Vector3 point)
        {
            float side = Mathf.Sign(point.x);
            float length = Mathf.Abs(point.x);
            // The OBJ is authored with both hands flat. Curl from the knuckles down toward the palm.
            float littleFinger = 1f - Mathf.InverseLerp(-0.112f, -0.089f, point.z);
            float knuckle = Mathf.Lerp(0.821f, 0.803f, littleFinger);
            const float knuckleHeight = 1.389f;
            Vector2 finger = new Vector2(length, point.y);
            // Curl the tip first in the bind pose, then carry it through the bends of the inner joints.
            finger = BendFinger(finger, knuckle + 0.075f, knuckleHeight, -37f);
            finger = BendFinger(finger, knuckle + 0.041f, knuckleHeight, -72f);
            finger = BendFinger(finger, knuckle, knuckleHeight, -52f);

            float thumb = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.047f, -0.018f, point.z));
            float fold = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.77f, 0.833f, length));
            Vector3 curled = new Vector3(finger.x * side, finger.y, point.z);
            Vector3 tuckedThumb = point + new Vector3(-side * 0.012f, -0.018f, -0.036f) * fold;
            return Vector3.Lerp(curled, tuckedThumb, thumb);
        }

        static Vector2 BendFinger(Vector2 point, float jointX, float jointY, float degrees)
        {
            float angle = degrees * Mathf.Deg2Rad * Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(jointX - 0.006f, jointX + 0.010f, point.x));
            float sine = Mathf.Sin(angle);
            float cosine = Mathf.Cos(angle);
            float x = point.x - jointX;
            float y = point.y - jointY;
            return new Vector2(jointX + x * cosine - y * sine, jointY + x * sine + y * cosine);
        }

        BoneWeight Weights(Vector3 point)
        {
            float x = Mathf.Abs(point.x);
            if (point.y > 1.51f)
                return Blend(Chest, Head, Mathf.InverseLerp(1.51f, 1.625f, point.y));
            if (x > 0.135f && point.y > 1.25f)
            {
                int shoulder = point.x < 0f ? LeftShoulder : RightShoulder;
                if (x < 0.39f)
                    return Blend(Chest, shoulder, Mathf.InverseLerp(0.135f, 0.25f, x));
                float elbowOffset = _relaxedPreview ? 0.03f : 0f;
                float wristOffset = _relaxedPreview ? 0.09f : 0f;
                if (x < 0.65f + elbowOffset)
                    return Blend(shoulder, shoulder + 1, Mathf.InverseLerp(0.4f + elbowOffset, 0.51f + elbowOffset, x));
                return Blend(shoulder + 1, shoulder + 2, Mathf.InverseLerp(0.68f + wristOffset, 0.76f + wristOffset, x));
            }
            if (point.y > 1f)
                return Blend(Torso, Chest, Mathf.InverseLerp(1f, 1.25f, point.y));

            int hip = point.x < 0f ? LeftHip : RightHip;
            if (point.y < 0.15f)
                return Blend(hip + 1, point.x < 0f ? LeftAnkle : RightAnkle,
                    1f - Mathf.InverseLerp(0.06f, 0.15f, point.y));
            if (point.y > 0.64f)
                return Blend(Torso, hip, 1f - Mathf.InverseLerp(0.78f, 1f, point.y));
            return Blend(hip, hip + 1, 1f - Mathf.InverseLerp(0.45f, 0.59f, point.y));
        }

        static BoneWeight Blend(int a, int b, float blend)
        {
            if (blend > 0.5f)
                return new BoneWeight { boneIndex0 = b, weight0 = blend, boneIndex1 = a, weight1 = 1f - blend };
            return new BoneWeight { boneIndex0 = a, weight0 = 1f - blend, boneIndex1 = b, weight1 = blend };
        }

        internal void EnableMelee(Transform placeholder, GameObject knifeModel)
        {
            _meleeDriven = true;
            _holdingKnife = placeholder != null;
            if (!_holdingKnife)
                return;
            var wrist = _bones[RightWrist];
            if (knifeModel != null)
            {
                placeholder.gameObject.SetActive(false);
                var knife = Instantiate(knifeModel, wrist, false).transform;
                knife.SetLocalPositionAndRotation(new Vector3(0.081f, -0.029f, -0.006f), Quaternion.Euler(0f, 8f, -7f));
            }
            else
            {
                placeholder.SetParent(wrist, true);
                placeholder.SetLocalPositionAndRotation(new Vector3(0.16f, 0.01f, 0.015f), Quaternion.Euler(0f, 90f, -12f));
                placeholder.localScale = new Vector3(0.055f, 0.029f, 0.285f);
            }
        }

        internal void SetMeleeAnimations(TextAsset walk, TextAsset attack, TextAsset standing)
        {
            _ambient = LoadAnimation(walk);
            _attack = LoadAnimation(attack);
            _standing = LoadAnimation(standing);
        }

        AmbientAnimation LoadAnimation(TextAsset asset)
        {
            if (asset == null)
                return null;
            if (!Animations.TryGetValue(asset, out var animation))
            {
                animation = JsonUtility.FromJson<AmbientAnimation>(asset.text);
                if (animation == null || animation.duration <= 0f || animation.pelvis is not { Length: > 1 } ||
                    animation.rotations?.Length != animation.pelvis.Length * _bones.Length)
                    return null;
                Animations.Add(asset, animation);
            }
            return animation;
        }

        internal void PoseMelee(float speed, float attackElapsed, float windup, float recovery, bool leftStrike, bool engaged)
        {
            float dt = Time.deltaTime;
            _animatedSpeed = Mathf.Lerp(_animatedSpeed, speed, 1f - Mathf.Exp(-11f * dt));
            _stride = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.075f, 0.95f, _animatedSpeed));
            float run = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.35f, 3.25f, _animatedSpeed));
            float strideLength = Mathf.Lerp(0.95f, 2.02f, run);
            _gait = Mathf.Repeat(_gait + dt * (2f * Mathf.PI * _animatedSpeed / strideLength), 2f * Mathf.PI);
            float swing = Mathf.Cos(_gait) * _stride;
            _guard = Mathf.MoveTowards(_guard, engaged ? 1f : 0f, dt * 4.5f);
            float strike = 0f;
            if (attackElapsed >= 0f)
            {
                float prepare = windup * 0.52f;
                strike = attackElapsed < prepare ? Mathf.SmoothStep(0f, -0.45f, attackElapsed / prepare)
                    : attackElapsed < windup ? Mathf.SmoothStep(-0.45f, 1f, (attackElapsed - prepare) / (windup - prepare))
                    : Mathf.SmoothStep(1f, 0f, (attackElapsed - windup) / recovery);
            }

            float turn = strike * (leftStrike ? 1f : -1f);
            ApplyRotation(Chest, Quaternion.Euler(strike * 8f + run * 10f, turn * 16f + swing * 7f,
                -turn * 3f + swing * 2.5f));
            PoseFistArm(LeftShoulder, -1f, -swing, run, leftStrike ? strike : 0f);
            if (_holdingKnife)
                PoseKnifeArm(swing, run, attackElapsed, windup, recovery);
            else
                PoseFistArm(RightShoulder, 1f, swing, run, leftStrike ? 0f : strike);

            PoseLeg(LeftHip, LeftAnkle, _gait, run);
            PoseLeg(RightHip, RightAnkle, _gait + Mathf.PI, run);

            float handClose = Mathf.Lerp(Mathf.Lerp(0.32f, 0.79f, _stride), 1f, Mathf.Max(_guard, Mathf.Abs(strike)));
            _leftGrip = Mathf.MoveTowards(_leftGrip, handClose, dt * 6f);
            _rightGrip = Mathf.MoveTowards(_rightGrip, _holdingKnife ? 1f : handClose, dt * 6f);
            foreach (var (renderer, firstShape) in _handMeshes)
            {
                renderer.SetBlendShapeWeight(firstShape, _leftGrip * 100f);
                renderer.SetBlendShapeWeight(firstShape + 1, _rightGrip * 100f);
            }
            _poseInitialized = true;

            if (_meleeDriven)
            {
                if (_attack != null && attackElapsed >= 0f)
                    ApplyAttackAnimation(attackElapsed, windup, recovery);
                else
                {
                    _walkSpeed = speed;
                    ApplyAmbientAnimation(dt);
                }
            }
        }

        void PoseLeg(int hip, int ankle, float phase, float run)
        {
            float thigh = -Mathf.Cos(phase) * Mathf.Lerp(19f, 35f, run) * _stride;
            float lift = Mathf.Max(0f, -Mathf.Sin(phase));
            float knee = (5f + lift * lift * Mathf.Lerp(37f, 70f, run)) * _stride;
            ApplyRotation(hip, Quaternion.Euler(thigh, 0f, 0f));
            ApplyRotation(hip + 1, Quaternion.Euler(knee, 0f, 0f));
            ApplyRotation(ankle, Quaternion.Euler(-(thigh + knee) * Mathf.Lerp(0.85f, 0.34f, lift), 0f, 0f));
        }

        Vector3 ArmRest(float side, float swing, float run)
        {
            float clearance = _relaxedPreview ? 0.2f : 0.14f;
            float forward = _relaxedPreview && side > 0f ? 0.03f : 0.07f;
            var loose = new Vector3(side * clearance, -1f, forward + Mathf.Sin(Time.time * 1.5f) * 0.018f);
            Vector3 swinging = Quaternion.Euler(-swing * Mathf.Lerp(18f, 29f, run), 0f, 0f) * loose;
            var guard = new Vector3(side * 0.24f, -0.78f, 0.26f);
            return Vector3.Lerp(swinging, guard, _guard * (1f - _stride * 0.84f));
        }

        float ElbowRest(float run)
        {
            float bend = Mathf.Lerp(13f, Mathf.Lerp(39f, 79f, run), _stride);
            return Mathf.Lerp(bend, 103f, _guard * (1f - _stride));
        }

        void PoseFistArm(int shoulder, float side, float swing, float run, float strike)
        {
            Vector3 direction = ArmRest(side, swing, run);
            float bend = ElbowRest(run);
            if (strike < 0f)
            {
                direction = Vector3.Lerp(direction, new Vector3(side * 0.35f, -0.68f, -0.39f), -2f * strike);
                bend = Mathf.Lerp(bend, 100f, -2f * strike);
            }
            else
            {
                direction = Vector3.Lerp(direction, new Vector3(-side * 0.12f, -0.13f, 1f), strike);
                bend = Mathf.Lerp(bend, 8f, strike);
            }

            ApplyRotation(shoulder, ArmRotation(side, direction));
            ApplyRotation(shoulder + 1, Quaternion.Euler(0f, -side * bend, 0f));
            ApplyRotation(shoulder + 2, Quaternion.Euler(side * strike * 28f, 0f, 0f));
        }

        void PoseKnifeArm(float swing, float run, float elapsed, float windup, float recovery)
        {
            Vector3 rest = ArmRest(1f, swing * 0.73f, run);
            float elbowRest = -Mathf.Max(28f, ElbowRest(run) * 0.72f);
            var chamber = new Vector3(0.49f, -0.57f, -0.32f);
            var strike = new Vector3(-0.18f, -0.22f, 0.96f);
            var followThrough = new Vector3(-0.41f, -0.63f, 0.58f);
            Vector3 direction = rest;
            float elbow = elbowRest;
            float wrist = 0f;
            if (elapsed >= 0f)
            {
                float prepare = windup * 0.52f;
                float followEnd = windup + recovery * 0.42f;
                if (elapsed < prepare)
                    BlendPose(rest, chamber, elbowRest, -68f, 0f, -20f, elapsed / prepare);
                else if (elapsed < windup)
                    BlendPose(chamber, strike, -68f, -10f, -20f, 13f, Mathf.InverseLerp(prepare, windup, elapsed));
                else if (elapsed < followEnd)
                    BlendPose(strike, followThrough, -10f, -29f, 13f, 24f, Mathf.InverseLerp(windup, followEnd, elapsed));
                else
                    BlendPose(followThrough, rest, -29f, elbowRest, 24f, 0f, Mathf.InverseLerp(followEnd, windup + recovery, elapsed));
            }

            ApplyRotation(RightShoulder, ArmRotation(1f, direction));
            ApplyRotation(RightElbow, Quaternion.Euler(0f, elbow, 0f));
            ApplyRotation(RightWrist, Quaternion.Euler(wrist, 0f, 0f));

            void BlendPose(Vector3 from, Vector3 to, float fromElbow, float toElbow, float fromWrist, float toWrist, float t)
            {
                float blend = Mathf.SmoothStep(0f, 1f, t);
                direction = Vector3.Lerp(from, to, blend);
                elbow = Mathf.Lerp(fromElbow, toElbow, blend);
                wrist = Mathf.Lerp(fromWrist, toWrist, blend);
            }
        }

        static Quaternion ArmRotation(float side, Vector3 direction)
        {
            Vector3 armAxis = direction.normalized * side;
            Vector3 bend = Vector3.ProjectOnPlane(Vector3.forward + Vector3.up * 0.42f, armAxis);
            if (bend.sqrMagnitude < 0.001f)
                bend = Vector3.ProjectOnPlane(Vector3.up, armAxis);
            bend.Normalize();
            return Quaternion.LookRotation(bend, Vector3.Cross(bend, armAxis));
        }

        void ApplyRotation(int index, Quaternion target)
        {
            Transform joint = _bones[index];
            float response = _poseInitialized ? 1f - Mathf.Exp(-31f * Time.deltaTime) : 1f;
            joint.localRotation = Quaternion.Slerp(joint.localRotation, target, response);
        }

        void LateUpdate()
        {
            if (_meleeDriven || Time.deltaTime <= 0f)
                return;
            PoseMelee(0f, -1f, 0f, 0f, false, false);
            Quaternion headPose = ApplyAmbientAnimation(Time.deltaTime);
            if (_sleeping)
                return;
            if (_idleHandClearance > 0f)
            {
                float clearance = _idleHandClearance * (1f - EaseRange(0.04f, 0.45f, _walkSpeed));
                SpreadIdleHand(LeftShoulder, -clearance);
                SpreadIdleHand(RightShoulder, clearance);
            }
            Vector3 eye = _bones[Head].position;
            if (Time.time >= _nextAttentionPoll)
            {
                _nextAttentionPoll = Time.time + NpcIdleMotion.AttentionPoll;
                if (_listener == null || !_listener.isActiveAndEnabled)
                    _listener = Camera.main;
                _attention = NpcIdleMotion.CanSeePlayer(transform, _listener, eye);
            }

            Vector3 look = _attention && _listener != null
                ? transform.InverseTransformDirection(_listener.transform.position - eye) : Vector3.forward;
            float yaw = Mathf.Clamp(Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg, -60f, 60f);
            float pitch = Mathf.Clamp(-Mathf.Atan2(look.y, new Vector2(look.x, look.z).magnitude) * Mathf.Rad2Deg, -15f, 16f);
            float blend = 1f - Mathf.Exp(-7f * Time.deltaTime);
            _headYaw = Mathf.Lerp(_headYaw, yaw, blend);
            _headPitch = Mathf.Lerp(_headPitch, Time.time < _glanceUntil ? 20f : pitch, blend);
            _bones[Head].localRotation = Quaternion.Euler(_headPitch, _headYaw, 0f) * headPose;
        }

        internal void SetWalkSpeed(float speed) => _walkSpeed = speed;

        void SpreadIdleHand(int shoulder, float clearance)
        {
            if (Mathf.Abs(clearance) < 0.001f)
                return;
            var hand = _bones[shoulder + 2];
            var rotation = hand.rotation;
            Vector3 outward = transform.right * Mathf.Sign(clearance);
            Vector3 target = hand.position + transform.right * clearance + transform.forward * Mathf.Abs(clearance) * 0.22f;
            HumanoidLimbPose.Reach(_bones[shoulder], _bones[shoulder + 1], hand, target,
                outward - transform.forward * 0.25f);
            hand.rotation = rotation;
        }

        Quaternion ApplyAmbientAnimation(float dt)
        {
            if (_ambient == null && _standing == null)
                return Quaternion.identity;

            _bones[Torso].localPosition = _pelvisRest;
            _bones[Torso].localRotation = Quaternion.identity;
            _bones[Head].localRotation = Quaternion.identity;

            if (_standing != null)
            {
                _standingTime = Mathf.Repeat(_standingTime + dt, _standing.duration);
                float standWeight = 1f - Mathf.Max(_guard, Mathf.InverseLerp(0.04f, 0.35f, _walkSpeed));
                ApplyAnimationFrame(_standing, _standingTime / _standing.duration, standWeight);
            }
            if (_ambient == null)
            {
                ApplyIdleGlance(dt);
                return _bones[Head].localRotation;
            }

            float walkWeight = Mathf.InverseLerp(0.04f, 0.35f, _walkSpeed);
            if (_meleeDriven)
                walkWeight *= 1f - Mathf.InverseLerp(2.2f, 3.1f, _walkSpeed);
            bool resting = _seated || _sleeping;
            float blend = resting ? 1f : Mathf.MoveTowards(_walkBlend, walkWeight, dt * 4f);
            _walkBlend = blend;
            float playbackSpeed = resting ? 1f : _walkSpeed * _ambient.duration / Mathf.Max(0.1f, _ambient.strideLength);
            _ambientTime = Mathf.Repeat(_ambientTime + playbackSpeed * dt, _ambient.duration);
            Quaternion headPose = ApplyAnimationFrame(_ambient, _ambientTime / _ambient.duration, blend);
            if (_sleeping)
                RaiseMattressHand();
            else if (_seated)
            {
                LiftSeatedHand(LeftShoulder, -1f);
                LiftSeatedHand(RightShoulder, 1f);
            }
            return headPose;
        }

        void ApplyIdleGlance(float dt)
        {
            if (_lookAround == null)
                return;

            float duration = _lookAround.duration;
            if (_lookAroundTime >= duration && !_attention && Time.time >= _nextLookAround)
            {
                _lookAroundTime = 0f;
                _nextLookAround = Time.time + Random.Range(9f, 16f);
            }

            _lookAroundTime = Mathf.Min(_lookAroundTime + dt, duration);
            float fade = Mathf.Clamp01(Mathf.Min(_lookAroundTime, duration - _lookAroundTime) / 0.35f);
            _lookAroundWeight = Mathf.MoveTowards(_lookAroundWeight, _attention ? 0f : 1f, dt / 0.3f);
            float blend = Mathf.SmoothStep(0f, 1f, fade) * _lookAroundWeight;
            if (blend > 0f)
                ApplyAnimationFrame(_lookAround, _lookAroundTime / duration, blend);
        }

        void RaiseMattressHand()
        {
            var hand = _bones[LeftWrist];
            Quaternion wrist = hand.rotation;
            Vector3 target = hand.position + transform.up * 0.095f;
            HumanoidLimbPose.Reach(_bones[LeftShoulder], _bones[LeftElbow], hand, target,
                -transform.right + transform.up * 0.3f);
            hand.rotation = wrist;
        }

        void LiftSeatedHand(int shoulder, float side)
        {
            Transform hand = _bones[shoulder + 2];
            Quaternion animated = hand.rotation;
            Vector3 outward = transform.right * side;
            Vector3 target = hand.position + transform.up * 0.11f - transform.forward * 0.09f + outward * 0.045f;
            HumanoidLimbPose.Reach(_bones[shoulder], _bones[shoulder + 1], hand, target,
                outward - transform.forward * 0.12f);

            Vector3 fingers = animated * (Vector3.right * side);
            Vector3 level = Vector3.ProjectOnPlane(fingers, transform.up);
            hand.rotation = Quaternion.FromToRotation(fingers,
                Vector3.Lerp(fingers, level.normalized, 0.75f)) * animated;
        }

        void ApplyAttackAnimation(float elapsed, float windup, float recovery)
        {
            // The authored contact lands on the existing damage frame.
            float contact = Mathf.Clamp(_attack.contact, 0.05f, 0.95f);
            float progress = elapsed <= windup ? Mathf.Lerp(0f, contact, elapsed / windup)
                : Mathf.Lerp(contact, 1f, (elapsed - windup) / recovery);
            float blend = Mathf.Clamp01(Mathf.Min(elapsed / 0.12f, (windup + recovery - elapsed) / 0.14f));
            _bones[Torso].localPosition = _pelvisRest;
            _bones[Torso].localRotation = Quaternion.identity;
            _bones[Head].localRotation = Quaternion.identity;
            ApplyAnimationFrame(_attack, progress, Mathf.SmoothStep(0f, 1f, blend));
        }

        Quaternion ApplyAnimationFrame(AmbientAnimation animation, float progress, float blend)
        {
            float frame = Mathf.Clamp01(progress) * (animation.pelvis.Length - 1);
            int index = Mathf.Min((int)frame, animation.pelvis.Length - 2);
            float fraction = frame - index;
            _bones[Torso].localPosition = Vector3.Lerp(_bones[Torso].localPosition,
                _pelvisRest + Vector3.Lerp(animation.pelvis[index], animation.pelvis[index + 1], fraction), blend);
            int jointCount = _bones.Length;
            for (int joint = 0; joint < jointCount; joint++)
            {
                Quaternion rotation = Quaternion.Slerp(animation.rotations[index * jointCount + joint],
                    animation.rotations[(index + 1) * jointCount + joint], fraction);
                _bones[joint].localRotation = Quaternion.Slerp(_bones[joint].localRotation, rotation, blend);
            }
            return _bones[Head].localRotation;
        }

        internal void GlanceDown() => _glanceUntil = Time.time + 0.95f;

        void OnDestroy()
        {
            foreach (var mesh in _meshes)
                Destroy(mesh);
        }
    }
}
