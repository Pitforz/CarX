using UnityEngine;
using UnityEngine.AI;

namespace CarX.NPC
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DeniDialogue))]
    public sealed class DeniIdleVariation : MonoBehaviour
    {
        enum Phase { Waiting, SteppingOut, Performing, Returning }

        static readonly int Locomotion = Animator.StringToHash("Base Layer.Locomotion");
        static readonly int WarriorIdle = Animator.StringToHash("Base Layer.Warrior Idle");
        static readonly int MoveSpeed = Animator.StringToHash("MoveSpeed");

        const float WalkSpeed = 0.9f;
        const float FadeDuration = 0.32f;
        const float ModelGroundOffset = 0.86f;

        [SerializeField] Animator _animator;
        [SerializeField] RuntimeAnimatorController _controller;
        [SerializeField] AnimationClip _warriorClip;

        readonly Collider[] _overlaps = new Collider[16];

        DeniDialogue _dialogue;
        Camera _listener;
        Phase _phase;
        Vector3 _home;
        Quaternion _homeRotation;
        Vector3 _idleSpot;
        float _nextIdle;
        float _phaseEnds;

        void Start()
        {
            if (_animator == null || _controller == null || _warriorClip == null || !_animator.isHuman)
            {
                enabled = false;
                return;
            }

            _dialogue = GetComponent<DeniDialogue>();
            _animator.runtimeAnimatorController = _controller;
            _animator.applyRootMotion = false;
            _nextIdle = Time.time + Random.Range(18f, 30f);
        }

        void Update()
        {
            if (_dialogue.IsSpeaking || _dialogue.KeyReady)
            {
                if (_phase == Phase.Performing || _phase == Phase.SteppingOut)
                    ReturnHome();

                SetSpeed(0f);
                _nextIdle = Mathf.Max(_nextIdle, Time.time + 8f);
                return;
            }

            switch (_phase)
            {
                case Phase.Waiting:
                    if (Time.time >= _nextIdle)
                        TryStartVariation();
                    break;
                case Phase.SteppingOut:
                    if (StepTowards(_idleSpot) && FaceHome())
                    {
                        if (HasRoomForArms(_idleSpot))
                            PlayVariation();
                        else
                            ReturnHome();
                    }
                    else if (Time.time >= _phaseEnds)
                        ReturnHome();
                    break;
                case Phase.Performing:
                    if (Time.time >= _phaseEnds)
                        ReturnHome();
                    break;
                case Phase.Returning:
                    if (Time.time >= _phaseEnds && StepTowards(_home) && FaceHome())
                        _phase = Phase.Waiting;
                    break;
            }
        }

        void TryStartVariation()
        {
            _nextIdle = Time.time + 3f;
            if (_listener == null)
                _listener = Camera.main;

            Vector3 eye = transform.position + Vector3.up * 0.72f;
            if (_listener == null || (eye - _listener.transform.position).sqrMagnitude < 2.7f * 2.7f ||
                !NpcIdleMotion.CanSeePlayer(transform, _listener, eye) || !TryFindIdleSpot(out _idleSpot))
                return;

            _home = transform.position;
            _homeRotation = transform.rotation;
            if (_idleSpot == _home)
            {
                PlayVariation();
                return;
            }

            _phase = Phase.SteppingOut;
            _phaseEnds = Time.time + 5f;
        }

        bool TryFindIdleSpot(out Vector3 spot)
        {
            spot = transform.position;
            if (HasRoomForArms(spot))
                return true;

            if (!NavMesh.SamplePosition(spot - Vector3.up * ModelGroundOffset, out var start, 0.45f, NavMesh.AllAreas))
                return false;

            Vector3 offsetFromFloor = spot - start.position;
            for (int ring = 1; ring <= 3; ring++)
            {
                for (int direction = 0; direction < 8; direction++)
                {
                    Vector3 offset = Quaternion.AngleAxis(direction * 45f, Vector3.up) *
                        transform.right * (ring * 0.45f);
                    if (!NavMesh.SamplePosition(start.position + offset, out var candidate, 0.25f, NavMesh.AllAreas) ||
                        NavMesh.Raycast(start.position, candidate.position, out _, NavMesh.AllAreas))
                        continue;

                    Vector3 position = candidate.position + offsetFromFloor;
                    if (HasRoomForArms(position) && IsWalkSpaceClear(Vector3.Lerp(spot, position, 0.5f)))
                    {
                        spot = position;
                        return true;
                    }
                }
            }

            return false;
        }

        bool HasRoomForArms(Vector3 position)
        {
            // The clip reaches out with both arms. Reserve space for the full motion.
            int count = Physics.OverlapBoxNonAlloc(position + Vector3.up * 0.34f,
                new Vector3(0.94f, 0.52f, 0.94f), _overlaps, transform.rotation,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            return HasNoOtherColliders(count);
        }

        bool IsWalkSpaceClear(Vector3 position)
        {
            int count = Physics.OverlapCapsuleNonAlloc(position - Vector3.up * 0.48f,
                position + Vector3.up * 0.62f, 0.28f, _overlaps,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            return HasNoOtherColliders(count);
        }

        bool HasNoOtherColliders(int count)
        {
            if (count >= _overlaps.Length)
                return false;

            for (int i = 0; i < count; i++)
                if (!_overlaps[i].transform.IsChildOf(transform))
                    return false;

            return true;
        }

        bool StepTowards(Vector3 destination)
        {
            Vector3 current = transform.position;
            if ((destination - current).sqrMagnitude < 0.0001f)
            {
                SetSpeed(0f);
                return true;
            }

            Vector3 next = Vector3.MoveTowards(current, destination, WalkSpeed * Time.deltaTime);
            if (!IsWalkSpaceClear(next))
            {
                SetSpeed(0f);
                return false;
            }

            Vector3 direction = Vector3.ProjectOnPlane(destination - current, Vector3.up);
            if (direction.sqrMagnitude > 0.0001f)
                TurnTowards(Quaternion.LookRotation(direction));

            transform.position = next;
            SetSpeed(WalkSpeed);
            return false;
        }

        bool FaceHome()
        {
            TurnTowards(_homeRotation);
            return Quaternion.Angle(transform.rotation, _homeRotation) < 1f;
        }

        void TurnTowards(Quaternion rotation) =>
            transform.rotation = Quaternion.RotateTowards(transform.rotation, rotation, 200f * Time.deltaTime);

        void SetSpeed(float speed) => _animator.SetFloat(MoveSpeed, speed, 0.14f, Time.deltaTime);

        void PlayVariation()
        {
            _phase = Phase.Performing;
            _phaseEnds = Time.time + Mathf.Max(FadeDuration, _warriorClip.length - FadeDuration);
            _animator.CrossFadeInFixedTime(WarriorIdle, FadeDuration, 0);
        }

        void ReturnHome()
        {
            if (_phase == Phase.Performing)
                _animator.CrossFadeInFixedTime(Locomotion, FadeDuration, 0);

            _phase = Phase.Returning;
            _phaseEnds = Time.time + FadeDuration;
            _nextIdle = Time.time + Random.Range(40f, 65f);
        }

        internal void CancelForRelocation()
        {
            if (!isActiveAndEnabled || _dialogue == null)
                return;

            _phase = Phase.Waiting;
            _animator.SetFloat(MoveSpeed, 0f);
            _animator.CrossFadeInFixedTime(Locomotion, FadeDuration, 0);
            _nextIdle = Time.time + Random.Range(18f, 30f);
        }
    }
}
