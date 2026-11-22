using System.Collections;
using System.Collections.Generic;
using CarX.Gameplay;
using UnityEngine;
using UnityEngine.AI;

namespace CarX.NPC
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(90)]
    [RequireComponent(typeof(NpcDialogue))]
    public sealed class CaretakerTour : MonoBehaviour
    {
        internal const int IntroductionQuestion = -1;
        const float ResumeDistanceMargin = 0.65f;
        static readonly int MoveSpeed = Animator.StringToHash("MoveSpeed");
        static readonly Color CaretakerColor = new Color32(255, 209, 139, 255);

        [System.Serializable]
        sealed class FollowUp
        {
            [SerializeField] string _question;
            [SerializeField, TextArea] string _answer;
            [SerializeField] AudioClip _questionVoice;
            [SerializeField] AudioClip _answerVoice;

            internal string Question => _question;
            internal string Answer => _answer;
            internal AudioClip QuestionVoice => _questionVoice;
            internal AudioClip AnswerVoice => _answerVoice;
        }

        [SerializeField, Tooltip("Enable walking and gestures after assigning the route in the scene.")]
        bool _animateTour;
        [SerializeField] RuntimeAnimatorController _tourLocomotion;
        [SerializeField, Min(0.1f)] float _walkSpeed = 1.6f;
        [SerializeField, Min(1f)] float _followDistance = 4.5f;
        [SerializeField, Min(0f)] float _arrivalAudienceDistance;
        [SerializeField] Transform _entry;
        [SerializeField] Transform _fork;
        [SerializeField] Transform _leftTunnel;
        [SerializeField] Transform _afterToolsWaitingPoint;
        [SerializeField] Transform _leftShoulder;
        [SerializeField] Transform _rightShoulder;
        [SerializeField] AudioClip[] _mainMarkClips;
        [SerializeField] AudioClip[] _mainAlexeyClips;
        [SerializeField] FollowUp[] _followUps;

        readonly HashSet<int> _askedFollowUps = new();
        NavMeshPath _path;
        NavMeshAgent _agent;
        Animator _tourAnimator;
        AudioSource _voice;
        AudioSource _playerVoice;
        NpcSpeechState _speech;
        Transform _player;
        PlayerStatusHud _hud;
        Coroutine _tour;
        Transform _pointAt;
        Transform _pointingShoulder;
        Transform _gestureTarget;
        PointingArm _leftArm, _rightArm, _pointingArm;
        Quaternion _upperArmPose, _forearmPose;
        bool _armPoseApplied;
        bool _walking;
        bool _waitingForPlayer;
        bool _runningTour;
        bool _hasCompletedIntroduction;
        bool _movedAfterTools;
        float _walkPhase;
        float _pointWeight;

        internal bool IsRunning => _tour != null;
        internal bool CanStartIntroduction => !_hasCompletedIntroduction;
        internal int FollowUpCount => _hasCompletedIntroduction ? (_followUps?.Length ?? 0) : 0;
        internal bool HasAvailableQuestion => CanStartIntroduction || _askedFollowUps.Count < FollowUpCount;
        internal bool CanAskFollowUp(int index) => index >= 0 && index < FollowUpCount && !_askedFollowUps.Contains(index);
        internal string GetFollowUpQuestion(int index) => _followUps[index].Question;

        internal void MoveToAfterToolsPoint()
        {
            if (_afterToolsWaitingPoint == null || _movedAfterTools)
                return;

            _movedAfterTools = true;
            StopWalking();
            if (_agent != null)
                _agent.enabled = false;
            transform.SetPositionAndRotation(_afterToolsWaitingPoint.position, _afterToolsWaitingPoint.rotation);
        }

        void Awake()
        {
            _speech = NpcSpeechState.GetOrAdd(this);

            if (_animateTour && _tourLocomotion != null)
            {
                var animator = GetComponentInChildren<Animator>();
                if (animator != null && animator.isHuman)
                {
                    _tourAnimator = animator;
                    animator.runtimeAnimatorController = _tourLocomotion;
                    animator.applyRootMotion = false;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    _leftArm = new PointingArm(animator, right: false);
                    _rightArm = new PointingArm(animator, right: true);
                }
            }

            if (_mainMarkClips == null || _mainMarkClips.Length == 0)
                return;

            const float voiceRange = 21f;
            _voice = DialogueVoice.CreateSource(transform, "Caretaker Voice", minDistance: 0.1f, maxDistance: voiceRange);
            _voice.rolloffMode = AudioRolloffMode.Custom;
            _voice.SetCustomCurve(AudioSourceCurveType.CustomRolloff, new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(3f / voiceRange, 1f),
                new Keyframe(4f / voiceRange, 0.94f),
                new Keyframe(6f / voiceRange, 0.55f),
                new Keyframe(10f / voiceRange, 0.17f),
                new Keyframe(16f / voiceRange, 0.055f),
                new Keyframe(20f / voiceRange, 0.025f),
                new Keyframe(1f, 0f)));
        }

        internal bool TryStart(Transform player, PlayerStatusHud hud)
        {
            if (!isActiveAndEnabled || IsRunning || !CanStartIntroduction)
                return false;

            if (_mainAlexeyClips != null && _mainAlexeyClips.Length > 0)
                _playerVoice = DialogueVoice.GetPlayerSource(_playerVoice, player);

            _player = player;
            _hud = hud;
            _runningTour = true;
            _tour = StartCoroutine(RunTour());
            return true;
        }

        internal bool TryStartFollowUp(int index, Transform player, PlayerStatusHud hud)
        {
            if (!isActiveAndEnabled || IsRunning || !CanAskFollowUp(index))
                return false;

            var followUp = _followUps[index];
            if (followUp.QuestionVoice != null)
                _playerVoice = DialogueVoice.GetPlayerSource(_playerVoice, player);
            if (followUp.AnswerVoice != null && _voice == null)
                _voice = DialogueVoice.CreateSource(transform, "Caretaker Voice", minDistance: 1.5f, maxDistance: 21f);

            _player = player;
            _hud = hud;
            _askedFollowUps.Add(index);
            _tour = StartCoroutine(RunFollowUp(followUp));
            return true;
        }

        IEnumerator RunFollowUp(FollowUp followUp)
        {
            string speaker = GetComponent<NpcDialogue>().DisplayName;
            yield return Speak($"Алексей: {followUp.Question}", Color.white, _playerVoice, followUp.QuestionVoice);
            yield return Speak($"{speaker}: {followUp.Answer}", CaretakerColor, _voice, followUp.AnswerVoice,
                npcSpeaking: true);
            Finish();
        }

        IEnumerator RunTour()
        {
            yield return SpeakPlayer(1, "Что это за место?");
            yield return SpeakCaretaker(1, "Это катакомбы. А ты вообще кто? К нам нечасто приходят люди с оружием.");
            yield return SpeakCaretaker(2, "А если и приходят, то только чтобы кого-то повязать.");
            yield return SpeakPlayer(2, "Нет, я сейчас не на службе.");
            yield return SpeakCaretaker(3, "Так всё-таки полицейский?");
            yield return SpeakPlayer(3, "Младший офицер полиции.");
            yield return SpeakCaretaker(4, "Большая честь. Чем обязан, офицеру…");
            yield return SpeakPlayer(4, "Алексей… Алексей Александрович.", "Alexey/Mark_FullName");
            yield return SpeakCaretaker(5, "Эмигрант?");
            yield return SpeakPlayer(5, "Подростком переехал.");
            yield return SpeakCaretaker(6, "А я Марк. За порядком присматриваю.");
            yield return SpeakPlayer(6, "Можешь рассказать про это место?");
            bool walksDuringAnswer = _tourAnimator != null && _entry == null && _fork != null;
            if (walksDuringAnswer)
                StartWalk(_fork.position);
            yield return SpeakCaretaker(7, "Конечно, живём на двух этажах.");
            yield return SpeakCaretaker(8, "Наверху люди, внизу — кто как устроился.");

            _hud.SetStorySubtitle(null, Color.white);
            if (_entry != null)
            {
                StartWalk(_entry.position);
                yield return WaitForArrival();
            }

            if (_fork != null && !walksDuringAnswer)
                StartWalk(_fork.position);
            yield return SpeakCaretaker(9, "Вот это наши катакомбы. Не самое лучшее место на земле.");
            yield return SpeakCaretaker(10, "Сырость, вонь, плесень, крысы. Негусто. Но людей тут всегда достаточно.");
            yield return WaitForArrival();
            if (_arrivalAudienceDistance > 0f && _fork != null &&
                (transform.position - _fork.position).sqrMagnitude < 1f)
            {
                while (_player != null && PlayerDistanceSquared() > _arrivalAudienceDistance * _arrivalAudienceDistance)
                    yield return null;
            }

            if (!_movedAfterTools)
                PointAt(_leftTunnel, right: _tourAnimator != null);
            yield return SpeakCaretaker(11, "Вон там, видишь? Спят. Ха-ха, не лучший мотель они выбрали.");
            PointAt(null);
            yield return SpeakCaretaker(12, "А вообще не каждый тут такой, кем кажется.");
            yield return SpeakCaretaker(13, "Некоторые сюда попадают против своей воли.");
            yield return SpeakCaretaker(14, "Несправедливое наказание, проиграл суд, банкрот, потерял семью, сгорел дом.");
            yield return SpeakCaretaker(15, "Утрата всегда бьёт по человеку. Он не видит выхода и в конце концов сдаётся.");
            yield return SpeakCaretaker(16, "Это не случайность, а закономерность. Сплошное домино.");
            yield return SpeakCaretaker(17, "А ты сам зачем сюда пришёл? Кого-то ищешь?");

            yield return SpeakPlayer(7, "Да, отца. Высокий, полный, уже в возрасте.");
            yield return SpeakPlayer(8, "Зовут Александр. Не видел его?");
            yield return SpeakCaretaker(18, "Да. Дней пять-шесть назад заходил сюда. Больше его не видел.");
            yield return SpeakPlayer(9, "А не знаешь, кто мог видеть?");
            yield return SpeakCaretaker(19, "Тут каждый мог его видеть. Поспрашивай, может, что нового узнаешь.");

            yield return SpeakCaretaker(20, "Кстати, в наших тоннелях инженер живёт, Дени зовут. Мастер на все руки.");
            yield return SpeakCaretaker(21, "Ты его сразу узнаешь, он в красной куртке будет. Он точно что-то видел.");
            yield return SpeakCaretaker(22, "А ещё, может, починит тебе что-нибудь. Не за бесплатно, конечно.");
            yield return SpeakPlayer(10, "Ага. Спасибо тебе.");
            _hasCompletedIntroduction = true;
            Finish();
        }

        IEnumerator SpeakPlayer(int number, string text, string fallbackVoice = null) =>
            Speak(text, Color.white, _playerVoice,
                DialogueVoice.GetClip(_mainAlexeyClips, number - 1) ?? DialogueVoice.GetStoryClip(fallbackVoice));

        IEnumerator SpeakCaretaker(int number, string text) =>
            Speak(text, CaretakerColor, _voice, DialogueVoice.GetClip(_mainMarkClips, number - 1), npcSpeaking: true);

        IEnumerator Speak(string text, Color color, AudioSource voice, AudioClip clip, bool npcSpeaking = false)
        {
            while (_waitingForPlayer)
                yield return null;

            _speech.SetTalking(npcSpeaking);
            _hud.SetStorySubtitle(text, color);
            float duration = Mathf.Clamp(1f + text.Length / 17f, 2.4f, 8f);
            bool voiced = voice != null && clip != null;
            if (npcSpeaking)
                _speech.BeginLine(NpcSpeechState.SpokenText(text), duration, voiced ? voice : null, voiced ? clip : null);
            if (voiced)
                yield return DialogueVoice.Play(voice, clip);
            else
            {
                float remaining = duration;
                while (remaining > 0f)
                {
                    yield return null;
                    if (!_waitingForPlayer)
                        remaining -= Time.deltaTime;
                }
            }

            _speech.SetTalking(false);
            _hud.SetStorySubtitle(null, color);
        }

        IEnumerator WaitForArrival()
        {
            while (_walking)
                yield return null;
        }

        void StartWalk(Vector3 destination)
        {
            if (!_animateTour || _movedAfterTools)
                return;

            if (_agent == null)
            {
                if (!NavMesh.SamplePosition(transform.position, out var start, 1.5f, NavMesh.AllAreas))
                {
                    Debug.LogWarning("CarX: the caretaker has no nearby NavMesh.", this);
                    return;
                }

                transform.position = start.position;
                _agent = gameObject.AddComponent<NavMeshAgent>();
                _agent.speed = _walkSpeed;
                _agent.radius = 0.32f;
                _agent.height = 1.8f;
                if (_tourAnimator != null)
                    _agent.acceleration = 4f;
                _agent.stoppingDistance = 0.2f;
                _agent.updateRotation = false;
                _path = new NavMeshPath();
            }

            if (!_agent.isOnNavMesh ||
                !NavMesh.SamplePosition(destination, out var end, 1.5f, _agent.areaMask) ||
                !_agent.CalculatePath(end.position, _path) ||
                _path.status != NavMeshPathStatus.PathComplete ||
                !_agent.SetPath(_path))
            {
                Debug.LogWarning($"CarX: the caretaker could not reach {destination}.", this);
                return;
            }

            _agent.isStopped = _tourAnimator != null;
            _walking = true;
        }

        void Update()
        {
            RestoreArmPose();
            if (!IsRunning || Time.deltaTime <= 0f)
                return;

            if (_player == null)
            {
                Cancel();
                return;
            }

            if (!_runningTour)
                return;

            if (!_animateTour)
            {
                if (_walking)
                    StopWalking();
                return;
            }

            Vector3 towardPlayer = _player.position - transform.position;
            Vector3 facing = _pointAt != null ? _pointAt.position - transform.position : towardPlayer;
            if (_walking)
            {
                if (!_agent.isOnNavMesh || (!_agent.pathPending &&
                    (!_agent.hasPath || _agent.pathStatus != NavMeshPathStatus.PathComplete ||
                     _agent.remainingDistance <= _agent.stoppingDistance)))
                {
                    StopWalking();
                }
                else
                {
                    float followDistance = _waitingForPlayer ? _followDistance - ResumeDistanceMargin : _followDistance;
                    _waitingForPlayer = PlayerDistanceSquared() > followDistance * followDistance;
                    if (!_waitingForPlayer)
                        facing = _agent.steeringTarget - transform.position;

                    Vector3 horizontalFacing = Vector3.ProjectOnPlane(facing, Vector3.up);
                    bool turningTowardsRoute = _tourAnimator != null && _agent.isStopped &&
                        horizontalFacing.sqrMagnitude > 0.01f &&
                        Vector3.Dot(transform.forward, horizontalFacing.normalized) < 0.75f;
                    _agent.isStopped = _waitingForPlayer || turningTowardsRoute;
                }
            }

            facing.y = 0f;
            if (facing.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation,
                    Quaternion.LookRotation(facing), 175f * Time.deltaTime);

            if (_tourAnimator != null)
            {
                float speed = _walking && !_waitingForPlayer ? _agent.velocity.magnitude : 0f;
                _tourAnimator.SetFloat(MoveSpeed, speed, 0.16f, Time.deltaTime);
                return;
            }

            float swing = 0f;
            if (_walking && !_waitingForPlayer)
            {
                _walkPhase += Time.deltaTime * 6.5f;
                swing = Mathf.Sin(_walkPhase) * 14f;
            }

            PoseShoulder(_leftShoulder, swing);
            PoseShoulder(_rightShoulder, -swing);
        }

        float PlayerDistanceSquared()
        {
            Vector3 offset = _player.position - transform.position;
            offset.y = 0f;
            return offset.sqrMagnitude;
        }

        void PointAt(Transform target, bool right = false)
        {
            _pointAt = target;
            _pointingShoulder = right ? _rightShoulder : _leftShoulder;
            if (_tourAnimator == null || target == null)
                return;

            RestoreArmPose();
            _pointingArm = right ? _rightArm : _leftArm;
            _gestureTarget = target;
            _pointWeight = 0f;
        }

        void LateUpdate()
        {
            if (_gestureTarget == null || !_pointingArm.IsValid)
                return;

            bool pointing = _pointAt != null;
            _pointWeight = Mathf.MoveTowards(_pointWeight, pointing ? 1f : 0f,
                Time.deltaTime / (pointing ? 0.32f : 0.28f));
            if (_pointWeight <= 0f)
                return;

            var arm = _pointingArm;
            _upperArmPose = arm.Upper.localRotation;
            _forearmPose = arm.Forearm.localRotation;
            _armPoseApplied = true;

            Vector3 shoulder = arm.Upper.position;
            float reach = Vector3.Distance(shoulder, arm.Forearm.position) +
                Vector3.Distance(arm.Forearm.position, arm.Hand.position);
            Vector3 direction = (_gestureTarget.position - shoulder).normalized;
            Vector3 elbowBend = -transform.up + transform.right * (arm.Right ? 0.45f : -0.45f);
            HumanoidLimbPose.Reach(arm.Upper, arm.Forearm, arm.Hand, shoulder + direction * (reach * 0.94f),
                elbowBend, Mathf.SmoothStep(0f, 1f, _pointWeight));
        }

        void RestoreArmPose()
        {
            if (!_armPoseApplied)
                return;

            _pointingArm.Upper.localRotation = _upperArmPose;
            _pointingArm.Forearm.localRotation = _forearmPose;
            _armPoseApplied = false;
        }

        void PoseShoulder(Transform shoulder, float swing)
        {
            if (shoulder == null)
                return;

            Quaternion pose = Quaternion.Euler(swing, 0f, 0f);
            if (shoulder == _pointingShoulder && _pointAt != null)
            {
                Vector3 direction = _pointAt.position - shoulder.position;
                direction.y = 0f;
                pose = Quaternion.FromToRotation(Vector3.down, transform.InverseTransformDirection(direction));
            }

            shoulder.localRotation = Quaternion.RotateTowards(shoulder.localRotation, pose, 260f * Time.deltaTime);
        }

        void StopWalking()
        {
            _walking = false;
            _waitingForPlayer = false;
            if (_agent != null && _agent.isOnNavMesh)
            {
                _agent.isStopped = true;
                _agent.ResetPath();
            }
        }

        internal void Skip()
        {
            if (!IsRunning)
                return;

            if (_runningTour)
                _hasCompletedIntroduction = true;
            Cancel();
        }

        internal void Cancel()
        {
            if (!IsRunning)
                return;

            StopCoroutine(_tour);
            Finish();
        }

        void Finish()
        {
            _tour = null;
            if (_voice != null)
                _voice.Stop();
            if (_playerVoice != null)
                _playerVoice.Stop();
            if (_speech != null)
                _speech.SetTalking(false);
            StopWalking();
            PointAt(null);
            if (_tourAnimator != null)
                _tourAnimator.SetFloat(MoveSpeed, 0f);
            if (_runningTour && _animateTour)
            {
                if (_leftShoulder != null)
                    _leftShoulder.localRotation = Quaternion.identity;
                if (_rightShoulder != null)
                    _rightShoulder.localRotation = Quaternion.identity;
            }
            _runningTour = false;
            if (_hud != null)
                _hud.SetStorySubtitle(null, Color.white);
            _hud = null;
            _player = null;
        }

        void OnDisable()
        {
            Cancel();
            RestoreArmPose();
            _gestureTarget = null;
            _pointWeight = 0f;
        }

        void OnDestroy()
        {
            if (_playerVoice != null)
                Destroy(_playerVoice.gameObject);
        }

        readonly struct PointingArm
        {
            internal readonly Transform Upper, Forearm, Hand;
            internal readonly bool Right;
            internal bool IsValid => Upper != null && Forearm != null && Hand != null;

            internal PointingArm(Animator animator, bool right)
            {
                Right = right;
                Upper = animator.GetBoneTransform(right ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm);
                Forearm = animator.GetBoneTransform(right ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
                Hand = animator.GetBoneTransform(right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
            }
        }
    }
}
