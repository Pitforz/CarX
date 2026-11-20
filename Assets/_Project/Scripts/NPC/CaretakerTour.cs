using System.Collections;
using CarX.Gameplay;
using UnityEngine;
using UnityEngine.AI;

namespace CarX.NPC
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NpcDialogue))]
    public sealed class CaretakerTour : MonoBehaviour
    {
        const float FollowDistance = 4.5f;
        const float WalkSpeed = 1.6f;
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
        [SerializeField] Transform _entry;
        [SerializeField] Transform _fork;
        [SerializeField] Transform _leftTunnel;
        [SerializeField] Transform _engineer;
        [SerializeField] Transform _leftShoulder;
        [SerializeField] Transform _rightShoulder;
        [SerializeField] AudioClip[] _mainMarkClips;
        [SerializeField] AudioClip[] _mainAlexeyClips;
        [SerializeField] FollowUp[] _followUps;

        NavMeshPath _path;
        NavMeshAgent _agent;
        AudioSource _voice;
        AudioSource _playerVoice;
        NpcSpeechState _speech;
        Transform _player;
        PlayerStatusHud _hud;
        Coroutine _tour;
        Transform _pointAt;
        Transform _pointingShoulder;
        bool _walking;
        bool _waitingForPlayer;
        bool _runningTour;
        bool _hasCompletedIntroduction;
        float _walkPhase;

        internal bool IsRunning => _tour != null;
        internal int FollowUpCount => _hasCompletedIntroduction ? (_followUps?.Length ?? 0) : 0;
        internal string GetFollowUpQuestion(int index) => _followUps[index].Question;

        void Awake()
        {
            _speech = NpcSpeechState.GetOrAdd(this);

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
            if (!isActiveAndEnabled || IsRunning)
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
            if (!isActiveAndEnabled || IsRunning || index < 0 || index >= FollowUpCount)
                return false;

            var followUp = _followUps[index];
            if (followUp.QuestionVoice != null)
                _playerVoice = DialogueVoice.GetPlayerSource(_playerVoice, player);
            if (followUp.AnswerVoice != null && _voice == null)
                _voice = DialogueVoice.CreateSource(transform, "Caretaker Voice", minDistance: 1.5f, maxDistance: 21f);

            _player = player;
            _hud = hud;
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
            yield return SpeakPlayer(4, "Алексею Александровичу.");
            yield return SpeakCaretaker(5, "Эмигрант?");
            yield return SpeakPlayer(5, "Подростком переехал.");
            yield return SpeakCaretaker(6, "А я Марк. За порядком присматриваю.");
            yield return SpeakPlayer(6, "Можешь рассказать про это место?");
            yield return SpeakCaretaker(7, "Конечно, живём на двух этажах.");
            yield return SpeakCaretaker(8, "Наверху люди, внизу — кто как устроился.");

            _hud.SetStorySubtitle(null, Color.white);
            if (_entry != null)
            {
                StartWalk(_entry.position);
                yield return WaitForArrival();
            }

            if (_fork != null)
                StartWalk(_fork.position);
            yield return SpeakCaretaker(9, "Вот это наши катакомбы. Не самое лучшее место на земле.");
            yield return SpeakCaretaker(10, "Сырость, вонь, плесень, крысы. Негусто. Но людей тут всегда достаточно.");
            yield return WaitForArrival();

            PointAt(_leftTunnel, _leftShoulder);
            yield return SpeakCaretaker(11, "Вон там, видишь? Спят. Ха-ха, не лучший мотель они выбрали.");
            PointAt(null, null);
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

            PointAt(_engineer, _rightShoulder);
            yield return SpeakCaretaker(20, "Кстати, в наших тоннелях инженер живёт, Дени зовут. Мастер на все руки.");
            yield return SpeakCaretaker(21, "Ты его сразу узнаешь, он в красной куртке будет. Он точно что-то видел.");
            PointAt(null, null);
            yield return SpeakCaretaker(22, "А ещё, может, починит тебе что-нибудь. Не за бесплатно, конечно.");
            yield return SpeakPlayer(10, "Ага. Спасибо тебе.");
            _hasCompletedIntroduction = true;
            Finish();
        }

        IEnumerator SpeakPlayer(int number, string text) =>
            Speak(text, Color.white, _playerVoice, DialogueVoice.GetClip(_mainAlexeyClips, number - 1));

        IEnumerator SpeakCaretaker(int number, string text) =>
            Speak(text, CaretakerColor, _voice, DialogueVoice.GetClip(_mainMarkClips, number - 1), npcSpeaking: true);

        IEnumerator Speak(string text, Color color, AudioSource voice, AudioClip clip, bool npcSpeaking = false)
        {
            while (_waitingForPlayer)
                yield return null;

            _speech.SetTalking(npcSpeaking);
            _hud.SetStorySubtitle(text, color);
            if (voice != null && clip != null)
                yield return DialogueVoice.Play(voice, clip);
            else
            {
                float remaining = Mathf.Clamp(1f + text.Length / 17f, 2.4f, 8f);
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
            if (!_animateTour)
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
                _agent.speed = WalkSpeed;
                _agent.radius = 0.32f;
                _agent.height = 1.8f;
                _agent.stoppingDistance = 0.15f;
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

            _agent.isStopped = false;
            _walking = true;
        }

        void Update()
        {
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
                    _waitingForPlayer = towardPlayer.sqrMagnitude > FollowDistance * FollowDistance;
                    _agent.isStopped = _waitingForPlayer;
                    if (!_waitingForPlayer)
                        facing = _agent.steeringTarget - transform.position;
                }
            }

            facing.y = 0f;
            if (facing.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation,
                    Quaternion.LookRotation(facing), 175f * Time.deltaTime);

            float swing = 0f;
            if (_walking && !_waitingForPlayer)
            {
                _walkPhase += Time.deltaTime * 6.5f;
                swing = Mathf.Sin(_walkPhase) * 14f;
            }

            PoseShoulder(_leftShoulder, swing);
            PoseShoulder(_rightShoulder, -swing);
        }

        void PointAt(Transform target, Transform shoulder)
        {
            _pointAt = target;
            _pointingShoulder = shoulder;
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
            PointAt(null, null);
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

        void OnDisable() => Cancel();

        void OnDestroy()
        {
            if (_playerVoice != null)
                Destroy(_playerVoice.gameObject);
        }
    }
}
