using CarX.Gameplay;
using UnityEngine;
using UnityEngine.AI;

namespace CarX.NPC
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ResidentModelPose))]
    public sealed class ResidentPatrol : MonoBehaviour
    {
        [SerializeField] Vector3[] _localStops;
        [SerializeField, Min(0.1f)] float _walkSpeed = 1.35f;

        ResidentModelPose _pose;
        NavMeshAgent _agent;
        NavMeshPath _route;
        NpcDialogue _dialogue;
        PlayerPistol _player;
        Transform _level;
        int _lastStop = -1;
        float _nextWalk;
        bool _onRoute;

        void Start()
        {
            if (_localStops is not { Length: > 1 } ||
                !NavMesh.SamplePosition(transform.position, out var home, 1.25f, NavMesh.AllAreas))
            {
                enabled = false;
                return;
            }

            _level = transform.parent;
            _pose = GetComponent<ResidentModelPose>();
            _route = new NavMeshPath();
            transform.position = home.position;
            _agent = gameObject.AddComponent<NavMeshAgent>();
            _agent.speed = _walkSpeed;
            _agent.radius = 0.28f;
            _agent.height = 1.75f;
            _agent.acceleration = 3.5f;
            _agent.angularSpeed = 170f;
            _agent.stoppingDistance = 0.25f;

            if (TryGetComponent(out _dialogue))
            {
                foreach (var player in FindObjectsByType<PlayerPistol>())
                {
                    if (player.gameObject.scene != gameObject.scene)
                        continue;

                    _player = player;
                    break;
                }
            }
        }

        void Update()
        {
            if (!_agent.isOnNavMesh)
            {
                _pose.SetWalkSpeed(0f);
                return;
            }

            bool pause = WaitForConversation();
            if (_agent.isStopped != pause)
            {
                _agent.isStopped = pause;
                _agent.updateRotation = !pause;
            }

            _pose.SetWalkSpeed(_agent.velocity.magnitude);
            if (pause)
                return;

            if (_onRoute && !_agent.pathPending &&
                (!_agent.hasPath || _agent.remainingDistance <= _agent.stoppingDistance))
            {
                _agent.ResetPath();
                _onRoute = false;
                _nextWalk = Time.time + Random.Range(1.2f, 3.4f);
            }

            if (!_onRoute && Time.time >= _nextWalk && !ChooseStop())
                _nextWalk = Time.time + 2f;
        }

        bool WaitForConversation()
        {
            if (_dialogue == null)
                return false;

            var camera = _player != null ? _player.ViewCamera : null;
            if (camera == null)
                return _dialogue.IsAnswering;

            Vector3 target = camera.transform.position;
            Vector3 eye = transform.position + Vector3.up * 1.45f;
            Vector3 direction = target - eye;
            if (!_dialogue.IsAnswering)
            {
                float distance = _dialogue.InteractionDistance + (_agent.isStopped ? 0.5f : 0f);
                if (!_dialogue.CanTalk || direction.sqrMagnitude > distance * distance)
                    return false;

                int layers = Physics.DefaultRaycastLayers & ~(1 << _player.gameObject.layer);
                if (Physics.Linecast(target, eye, out var hit, layers, QueryTriggerInteraction.Ignore) &&
                    !hit.transform.IsChildOf(transform))
                    return false;
            }

            Vector3 facing = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (facing.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(facing),
                    _agent.angularSpeed * Time.deltaTime);

            return true;
        }

        bool ChooseStop()
        {
            int first = Random.Range(0, _localStops.Length);
            for (int offset = 0; offset < _localStops.Length; offset++)
            {
                int index = (first + offset) % _localStops.Length;
                Vector3 destination = _level != null ? _level.TransformPoint(_localStops[index]) : _localStops[index];
                if (index == _lastStop || (destination - transform.position).sqrMagnitude < 2f ||
                    !NavMesh.SamplePosition(destination, out var stop, 1.25f, _agent.areaMask) ||
                    !_agent.CalculatePath(stop.position, _route) || _route.status != NavMeshPathStatus.PathComplete ||
                    !_agent.SetPath(_route))
                    continue;

                _lastStop = index;
                _onRoute = true;
                return true;
            }
            return false;
        }
    }
}
