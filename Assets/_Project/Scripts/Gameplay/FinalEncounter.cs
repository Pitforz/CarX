using System.Collections;
using CarX.NPC;
using StarterAssets;
using UnityEngine;
using UnityEngine.AI;

namespace CarX.Gameplay
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BanditSquad))]
    [DefaultExecutionOrder(205)]
    public sealed class FinalEncounter : MonoBehaviour
    {
        [SerializeField] HostileNpc _banditPrefab;
        [SerializeField] GameObject _bossModel;
        [SerializeField] GameObject _fatherModel;
        [SerializeField] GameObject _fatherPistol;
        [SerializeField] RuntimeAnimatorController _fatherLocomotion;
        [SerializeField] Material _actorMaterial;
        [SerializeField] AudioClip _executionShot;
        [SerializeField] LowerFloorDoor _hallDoor;
        [SerializeField] bool _storyEndings;
        [SerializeField] Bounds _entrance = new(new Vector3(0f, 1.5f, 1.6f), new Vector3(16f, 4f, 3f));
        [SerializeField] Vector3 _center = new(0f, 0f, 18f);
        [SerializeField, Min(0.5f)] float _centerRadius = 1.35f;
        [SerializeField] Vector3 _bossPosition = new(0f, 0f, 25.8f);
        [SerializeField] Vector3 _bossGuardPosition = new(-3.2f, 0f, 25.3f);
        [SerializeField] Vector3 _rearLeftPosition = new(-3f, 0f, 2.6f);
        [SerializeField] Vector3 _rearRightPosition = new(3f, 0f, 2.6f);
        [SerializeField] Vector3 _fatherEntrance = new(12.4f, 0f, 28.35f);
        [SerializeField] Vector3 _escortEntrance = new(10.25f, 0f, 27.45f);
        [SerializeField] Vector3 _fatherBodyPosition = new(12.65f, 0f, 26.9f);
        [SerializeField] Vector3 _escortFightEntry = new(8.1f, 0f, 26.95f);
        [SerializeField] Vector3 _fatherPosition = new(2.25f, 0f, 24.6f);
        [SerializeField] Vector3 _escortPosition = new(4.3f, 0f, 26.1f);
        [SerializeField] Vector3 _fatherCellPeek = new(9.45f, 0f, 27.35f);
        [SerializeField] Vector3 _fatherCellCover = new(10.55f, 0f, 28.65f);
        [Tooltip("Optional holding passage threshold in encounter space.")]
        [SerializeField] Vector3 _cellThreshold;
        [SerializeField, Min(1f)] float _holsterTime = 8f;
        [SerializeField, Min(1f)] float _redrawHolsterTime = 10f;
        [SerializeField, Min(1f)] float _walkTime = 20f;
        [SerializeField, Min(1f)] float _choiceTime = 60f;
        [SerializeField, Min(0f)] float _openingDelay = 1f;
        [SerializeField, Min(0f)] float _rearOpeningCoverTime;

        static readonly Color BossColor = new Color32(255, 219, 45, 255);
        static readonly Color GuardColor = new Color32(225, 47, 47, 255);
        static readonly Color FatherColor = new Color32(65, 206, 81, 255);
        const float EarlyFightHitChance = 0.8f;
        const float PersuasionFightHitChance = 1f;
        const float FatherEscapeDelay = 1f;
        const float FatherDialogueRaiseTime = 0.90f;
        const float FatherRunSpeed = 5.4f;
        const float FatherPeekSpeed = 2.6f;
        const float FatherPeekLookTime = 0.32f;
        const float FatherPeekShotTime = 0.15f;
        const float FatherCoverSearchRadius = 12f;
        const float FatherSafeDistance = 6f;
        const float FatherRescueWindow = 1f;
        const float FatherRescueFirstShotAt = 0.18f;
        const float FatherEscapeAcceleration = 18f;
        const float HallRetreatDistance = 5f;
        const float EndingApproachDistance = 2.15f;
        const string FatherDeathLine = "Алексей: Пап… я столько хотел тебе сказать.";
        const string FatherSurvivalLine = "Алексей: Пап, я нашёл тебя. Пойдём домой.";
        const string FatherCombatWarning = "Александр: Лёша, в сторону!";

        enum Stage
        {
            AwaitingPlayer, Arrival, Holster, WalkToCenter, Dialogue, Choice, ChoiceCinematic, CombatTutorial,
            FatherRescue, FightOpening, Fight, Finished
        }

        HostileNpc[] _enemies;
        Stage _stage;
        PlayerHealth _player;
        PlayerPistol _pistol;
        PlayerMenus _menus;
        PlayerStatusHud _hud;
        FirstPersonController _movement;
        HostileNpc _boss;
        HostileNpc _bossGuard;
        HostileNpc _escort;
        HostileNpc _rearGuard;
        HostileNpc _rearRightGuard;
        HostileNpc _father;
        BanditPistol _fatherGun;
        BanditCover _fatherSupportCover;
        AudioSource _shotAudio;
        AudioSource _dialogueVoice;
        HostileNpc _dialogueSpeaker;
        float _spokenUntil;
        float _deadline;
        float _redrawDeadline = float.PositiveInfinity;
        int _shownSeconds;
        Vector3 _openingTarget;
        float? _openingHitChance;
        bool _stressBeforeEncounter;
        bool _encounterStressStarted;
        bool _readyForChoice;
        bool _fatherAtMark;
        bool _escortStaged;
        bool _escortThreateningFather;
        bool _fatherWasExecuted;
        bool _fatherHelpsInFight;
        bool _fatherSavedInRescue;
        bool _awaitingEnding;
        int _endingChoice;

        Vector3 PlayerChest => _player.transform.position + Vector3.up * 1.4f;
        bool BossIsSpeaking => _dialogueSpeaker == _boss && (_dialogueVoice != null && _dialogueVoice.clip != null
            ? _dialogueVoice.isPlaying || (AudioListener.pause && !_dialogueVoice.ignoreListenerPause)
            : Time.time < _spokenUntil);
        Transform EncounterView => _movement.CinemachineCameraTarget != null
            ? _movement.CinemachineCameraTarget.transform : _pistol.ViewCamera.transform;
        Quaternion EncounterLookCenter => _movement.CinemachineCameraTarget != null
            ? _movement.EncounterLookCenterRotation : EncounterView.rotation;

        void Start()
        {
            foreach (var candidate in FindObjectsByType<PlayerHealth>())
            {
                if (candidate.gameObject.scene != gameObject.scene)
                    continue;

                if (!candidate.TryGetComponent(out _pistol) || !candidate.TryGetComponent(out _menus) ||
                    !candidate.TryGetComponent(out _hud) || !candidate.TryGetComponent(out _movement))
                    continue;

                _player = candidate;
                break;
            }

            if (_player == null || _banditPrefab == null)
            {
                Debug.LogError("Final Encounter requires a bandit prefab and a player in the same scene.", this);
                enabled = false;
                return;
            }

            _boss = Spawn(_bossPosition, BossColor, "Mafia Boss", _bossModel);
            _bossGuard = Spawn(_bossGuardPosition, GuardColor, "Boss Guard");
            _escort = Spawn(_escortEntrance, GuardColor, "Escort Guard");
            _rearGuard = Spawn(_rearLeftPosition, GuardColor, "Entrance Guard Left");
            _rearRightGuard = Spawn(_rearRightPosition, GuardColor, "Entrance Guard Right");
            _enemies = new[]
            {
                _boss, _escort, _rearGuard,
                _bossGuard,
                _rearRightGuard
            };
            _father = Spawn(_fatherEntrance, FatherColor, "Alexander", _fatherModel);
            _father.UseScriptedMovement();
            _father.EncounterAnimation?.KeepIdleStanceWithPistol();
            _fatherGun = _father.GetComponent<BanditPistol>();

            _shotAudio = gameObject.AddComponent<AudioSource>();
            _shotAudio.playOnAwake = false;
            _shotAudio.spatialBlend = 0f;
            _shotAudio.ignoreListenerPause = true;
            _pistol.ShotFired += OnPlayerShot;
            _pistol.ShotLineFired += OnPlayerShotLine;
        }

        void Update()
        {
            if (_player.IsDead)
            {
                if (_stage != Stage.Finished)
                    Finish();
                return;
            }

            if (_menus.IsPaused)
                return;

            if (_awaitingEnding)
            {
                TryEndAtFather();
                return;
            }

            if (TryExecuteRetreat())
                return;

            switch (_stage)
            {
                case Stage.AwaitingPlayer:
                    if (_entrance.Contains(transform.InverseTransformPoint(_player.transform.position)))
                        GreetPlayer();
                    break;
                case Stage.Arrival:
                    if (Time.time >= _deadline)
                        DemandHolster();
                    break;
                case Stage.Holster:
                    if (!_pistol.IsEquipped)
                        DemandApproach();
                    else if (Time.time >= _deadline)
                        ExecutePlayer(_rearGuard);
                    break;
                case Stage.WalkToCenter:
                    if (CheckRedrawnWeapon())
                        break;
                    if (ReachedCenter() && !_menus.BlocksGameplayInput)
                        BeginDialogue();
                    else if (Time.time >= _deadline)
                        ExecutePlayer(_rearGuard);
                    break;
                case Stage.Dialogue:
                    if (!CheckRedrawnWeapon() && _readyForChoice && !_pistol.IsEquipped &&
                        !_movement.IsSettlingEncounterEntry)
                        OpenChoice();
                    break;
                case Stage.Choice:
                    UpdateChoice();
                    break;
                case Stage.FightOpening:
                    if (Time.time >= _deadline)
                        ReleaseGuards(_openingHitChance);
                    else
                        AimGuardsAtOpeningTarget(smooth: true);
                    break;
                case Stage.Fight:
                    foreach (var enemy in _enemies)
                        if (enemy != null && enemy.IsAlive)
                            return;
                    Finish(wonFight: true);
                    if (_endingChoice != 0)
                        _awaitingEnding = true;
                    else if (_fatherWasExecuted)
                        ShowVoicedSubtitle("Алексей: Камера справа. Надо найти отца.", 5f, "Alexey/Final_FindFather");
                    break;
            }

            if (_stage is Stage.Arrival or Stage.Holster or Stage.WalkToCenter or Stage.Dialogue or
                Stage.Choice or Stage.ChoiceCinematic)
                FaceStagedGuards();

            bool fatherLookingAtPlayer = _fatherAtMark && _father.IsAlive && (_stage is Stage.Dialogue or Stage.Choice);
            _father.EncounterAnimation?.LookAt(fatherLookingAtPlayer ? PlayerChest + Vector3.up * 0.24f : null);
        }

        void FaceStagedGuards()
        {
            foreach (var enemy in _enemies)
            {
                if (enemy == null || !enemy.IsAlive)
                    continue;

                if (enemy == _escort && _escortStaged)
                {
                    if (_escortThreateningFather)
                        enemy.AimForEncounter(FatherTargetPoint(_father), smooth: true);
                }
                else
                    enemy.FaceForEncounter(PlayerChest, smooth: true);
            }
        }

        void LateUpdate()
        {
            if (_dialogueSpeaker != null && _dialogueVoice != null && _dialogueVoice.isPlaying)
                _dialogueVoice.transform.position = _dialogueSpeaker.HeadPosition;
        }

        HostileNpc Spawn(Vector3 localPosition, Color tint, string actorName, GameObject model = null)
        {
            Vector3 position = transform.TransformPoint(localPosition);
            Vector3 facing = Vector3.ProjectOnPlane(transform.TransformPoint(_center) - position, Vector3.up);
            Quaternion rotation = facing.sqrMagnitude > 0.001f ? Quaternion.LookRotation(facing) : transform.rotation;
            var actor = Instantiate(_banditPrefab, position, rotation, transform);
            actor.name = actorName;
            actor.StageForEncounter(tint, _actorMaterial);
            actor.AttachCombatModel(model, _fatherPistol, _fatherLocomotion);
            actor.SetStagedWeaponVisible(false);
            return actor;
        }

        void GreetPlayer()
        {
            _stage = Stage.Arrival;
            float duration = ShowVoicedSubtitle(
                "Босс: Сына Александра я ждал. Не так скоро. Хочешь увидеть отца — входи. Один.",
                4.5f, "Boss/Arrival", _boss);
            _deadline = Time.time + duration;
        }

        void DemandHolster()
        {
            StartCombatStress();
            if (!_pistol.IsEquipped)
            {
                DemandApproach();
                return;
            }

            _stage = Stage.Holster;
            _deadline = Time.time + _holsterTime;
            ShowVoicedSubtitle("Босс: Покажи сначала, что умеешь слушать. Ствол вниз. [1 — убрать пистолет]",
                _holsterTime, "Boss/Holster", _boss);
        }

        void DemandApproach()
        {
            _stage = Stage.WalkToCenter;
            _deadline = Time.time + _walkTime;
            ShowApproachInstruction("Видишь? Уже договорились. ");
        }

        void ShowApproachInstruction(string prefix = "")
        {
            float remaining = Mathf.Max(0f, _deadline - Time.time);
            string clipPath = string.IsNullOrEmpty(prefix) ? "Boss/ApproachRepeat" : "Boss/ApproachFirst";
            ShowVoicedSubtitle($"Босс: {prefix}Подойди к центру зала. [{Mathf.CeilToInt(remaining)} сек.]",
                remaining, clipPath, _boss);
        }

        bool CheckRedrawnWeapon()
        {
            if (!_pistol.IsEquipped)
            {
                if (!float.IsPositiveInfinity(_redrawDeadline))
                {
                    if (_stage == Stage.WalkToCenter)
                        ShowApproachInstruction();
                    else
                        ShowDialogueSubtitle(string.Empty, 0f, null, null);
                }
                _redrawDeadline = float.PositiveInfinity;
                return false;
            }

            if (float.IsPositiveInfinity(_redrawDeadline))
            {
                _redrawDeadline = Time.time + _redrawHolsterTime;
                ShowVoicedSubtitle($"Босс: Я сказал, убери пистолет. [1 — убрать, {_redrawHolsterTime:0} сек.]",
                    _redrawHolsterTime, "Boss/RedrawnWeapon", _boss);
            }
            if (Time.time < _redrawDeadline)
                return false;

            ExecutePlayer(_rearGuard);
            return true;
        }

        bool ReachedCenter()
        {
            Vector3 offset = transform.InverseTransformPoint(_player.transform.position) - _center;
            return Mathf.Abs(offset.y) <= 1.4f && new Vector2(offset.x, offset.z).sqrMagnitude <=
                _centerRadius * _centerRadius;
        }

        void BeginDialogue()
        {
            _stage = Stage.Dialogue;
            _menus.LockEncounterInput(_boss.transform.position + Vector3.up * 1.65f);
            StartCoroutine(PlayDialogue());
        }

        IEnumerator PlayDialogue()
        {
            yield return new WaitWhile(() => _movement.IsSettlingEncounterEntry);
            yield return Line("Алексей: Выведи его.", 1.7f, "Alexey/Final_BringHimOut");
            yield return Line("Босс: Выводи его сюда!", 1.8f, "Boss/BringHimOut", _boss);
            yield return BringOutFather();
            yield return Line("Александр: Лёша…", 1.7f, "Alexander/Lyosha", _father);
            yield return Line("Алексей: Я здесь. Стой спокойно.", 2.2f, "Alexey/Final_ImHere");
            yield return Line("Босс: Деньги проиграл. Груз возил. Потом решил, что полиция тебя от нас спрячет.",
                4.9f, "Boss/GamblingDebt", _boss);
            yield return Line("Алексей: Я читал бумаги. Отпусти его.", 2.6f, "Alexey/Final_ReleaseHim");
            yield return Line("Босс: Он хотел сдать нас. Просто убить его — слишком легко.",
                3.6f, "Boss/Betrayal", _boss);
            yield return Line("Александр: Что ты хочешь?", 1.9f, "Alexander/WhatDoYouWant", _father);
            yield return Line("Босс: Убей сына — выйдешь отсюда.", 2.8f, "Boss/ShootYourSon", _boss);
            yield return PassPistolToFather();
            yield return Line("Алексей: Пап!", 1.3f, "Alexey/Final_Dad");
            yield return Line("Александр: Ты правда меня выпустишь?", 2.5f, "Alexander/YouWillLetMeGo", _father);
            yield return Line("Босс: Дверь видишь? Через неё и уйдёшь.", 2.8f, "Boss/Door", _boss);
            yield return Line("Александр: Лёша, я… Они меня здесь…", 2.7f, "Alexander/TheyKeptMeHere", _father);
            _father.AimForEncounter(PlayerChest, raiseDuration: FatherDialogueRaiseTime);
            yield return new WaitForSeconds(FatherDialogueRaiseTime);
            PrepareChoice();
        }

        void PrepareChoice()
        {
            _father.SetStagedWeaponVisible(true);
            _father.AimForEncounter(PlayerChest);
            _readyForChoice = true;
        }

        void OpenChoice()
        {
            if (!_menus.ShowEncounterChoice(SelectEncounterChoice))
            {
                Debug.LogError("Final Encounter could not open the choice screen.", this);
                Finish();
                return;
            }

            _stage = Stage.Choice;
            _deadline = _menus.UnpausedTime + _choiceTime;
            _shownSeconds = -1;
            UpdateChoice();
        }

        IEnumerator BringOutFather()
        {
            Vector3 fromFather = transform.TransformPoint(_fatherEntrance);
            Vector3 fromEscort = transform.TransformPoint(_escortEntrance);
            Vector3 toFather = transform.TransformPoint(_fatherPosition);
            Vector3 toEscort = transform.TransformPoint(_escortPosition);
            bool usePassage = _cellThreshold != Vector3.zero;
            Vector3 threshold = transform.TransformPoint(_cellThreshold);
            _father.transform.SetPositionAndRotation(fromFather,
                Quaternion.LookRotation((usePassage ? threshold : toFather) - fromFather));
            _escort.transform.SetPositionAndRotation(fromEscort,
                Quaternion.LookRotation((usePassage ? threshold : toEscort) - fromEscort));
            _father.gameObject.SetActive(true);
            _escort.gameObject.SetActive(true);
            _escortStaged = true;
            _escort.FaceForEncounter(FatherTargetPoint(_father));
            yield return AnimateEscortPistol(drawing: true);
            _escortThreateningFather = true;

            float walkSpeed = usePassage ? 2.8f : 2.1f;
            var fatherRoute = usePassage ? new[] { fromFather, threshold, toFather } : new[] { fromFather, toFather };
            var escortRoute = usePassage ? new[] { fromEscort, threshold, toEscort } : new[] { fromEscort, toEscort };
            float fatherLength = RouteLength(fatherRoute);
            float escortLength = RouteLength(escortRoute);
            float duration = Mathf.Max(fatherLength, escortLength) / walkSpeed;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                float progress = WalkProgress(elapsed / duration);
                float fatherDistance = progress * fatherLength;
                if (usePassage)
                    _father.FaceForEncounter(PointOnRoute(fatherRoute, fatherDistance + 0.3f), smooth: true);
                _father.transform.position = PointOnRoute(fatherRoute, fatherDistance);
                _escort.transform.position = PointOnRoute(escortRoute, progress * escortLength);
                yield return null;
            }

            _father.transform.position = toFather;
            _escort.transform.position = toEscort;
            _escortThreateningFather = false;
            _escort.SpeakAtForEncounter(FatherTargetPoint(_father));
            yield return TurnFatherTowards(PlayerChest, 0.3f);
            _fatherAtMark = true;
            yield return AnimateEscortPistol(drawing: false);
            _escortStaged = false;
        }

        static float RouteLength(Vector3[] points)
        {
            float length = 0f;
            for (int i = 1; i < points.Length; i++)
                length += Vector3.Distance(points[i - 1], points[i]);
            return length;
        }

        static Vector3 PointOnRoute(Vector3[] points, float distance)
        {
            for (int i = 1; i < points.Length; i++)
            {
                Vector3 a = points[i - 1], b = points[i];
                float length = Vector3.Distance(a, b);
                if (length > 0.001f && distance <= length)
                    return Vector3.Lerp(a, b, distance / length);
                distance -= length;
            }
            return points[points.Length - 1];
        }

        IEnumerator PassPistolToFather()
        {
            var giver = _escort.EncounterAnimation;
            var receiver = _father.EncounterAnimation;
            if (giver == null || receiver == null)
            {
                _father.SetStagedWeaponVisible(true);
                yield break;
            }

            _escortStaged = true;
            Transform escort = _escort.transform;
            Transform father = _father.transform;
            Vector3 from = escort.position;
            Vector3 mark = father.position + father.right * 0.88f + father.forward * 0.35f;
            mark.y = from.y;
            Vector3 behind = father.position + father.right * 0.70f - father.forward * 1.27f;
            behind.y = from.y;
            Quaternion escortStart = escort.rotation;
            Quaternion escortFacing = Quaternion.LookRotation(Vector3.ProjectOnPlane(father.position - mark, Vector3.up));
            float routeLength = Vector3.Distance(from, behind) + Vector3.Distance(behind, mark);
            float approachTime = Mathf.Max(0.66f, routeLength / 2.45f);
            for (float t = 0f; t < 1f; t += Time.deltaTime / approachTime)
            {
                MoveEscortAlongHandoff(Ease(t));
                if (t >= 0.56f)
                    PoseEscortDraw(giver, Mathf.InverseLerp(0.56f, 1f, t));
                yield return null;
            }
            escort.SetPositionAndRotation(mark, escortFacing);
            PoseEscortDraw(giver, 1f);

            Pose lowered = receiver.LoweredPistolPose;
            Vector3 position = lowered.position + father.right * 0.16f + father.forward * 0.15f + father.up * 0.11f;
            var offered = new Pose(position, father.rotation * Quaternion.Euler(64f, -22f, -12f));
            Pose giverLow = giver.LoweredPistolPose;
            const float offerTime = 0.28f;
            for (float t = 0f; t < 1f; t += Time.deltaTime / offerTime)
            {
                giver.StageGrip(BlendPose(giverLow, offered, Ease(t)), 1f, 1f, Ease(t));
                float reach = Ease(Mathf.InverseLerp(0.10f, 0.93f, t));
                receiver.StageGrip(offered, reach, Ease(Mathf.InverseLerp(0.72f, 1f, t)));
                yield return null;
            }
            giver.StageGrip(offered, 1f, 1f, presentation: 1f);
            receiver.StageGrip(offered, 1f, 1f);
            yield return null;
            Pose contact = giver.HeldPistolPose;
            receiver.StageGrip(contact, 1f, 1f);
            yield return null;
            if (_menus.IsPaused)
                yield return new WaitWhile(() => _menus.IsPaused);
            giver.PassPistolTo(receiver);

            float returnTime = 0.10f + Mathf.Max(0.45f, routeLength / 1.85f);
            const float withdrawTime = 0.24f;
            giver.SetBackpedaling(true);
            for (float elapsed = 0f; elapsed < returnTime; elapsed += Time.deltaTime)
            {
                float retract = Ease(elapsed / withdrawTime);
                if (elapsed < withdrawTime)
                    giver.StageGrip(offered, 1f - retract, 1f - Ease(elapsed / 0.08f), presentation: 1f);
                else
                    giver.ClearStagedGrip();

                MoveEscortAlongHandoff(1f - Ease(Mathf.InverseLerp(0.10f, returnTime, elapsed)));
                receiver.StageGrip(contact, 1f - retract, 1f);
                yield return null;
            }
            escort.SetPositionAndRotation(from, escortStart);
            giver.SetBackpedaling(false);
            giver.ClearStagedGrip();
            receiver.ClearStagedGrip(holdAtSide: true);
            _escortStaged = false;

            void MoveEscortAlongHandoff(float progress)
            {
                Vector3 approach = Vector3.Lerp(from, behind, progress);
                Vector3 reach = Vector3.Lerp(behind, mark, progress);
                Vector3 direction = reach - approach;
                Quaternion walking = direction.sqrMagnitude > 0.0001f
                    ? Quaternion.LookRotation(direction) : escortFacing;
                Quaternion facing = Quaternion.Slerp(escortStart, walking, Ease(progress / 0.18f));
                facing = Quaternion.Slerp(facing, escortFacing, Ease(Mathf.InverseLerp(0.78f, 1f, progress)));
                escort.SetPositionAndRotation(Vector3.Lerp(approach, reach, progress), facing);
            }
        }

        IEnumerator AnimateEscortPistol(bool drawing)
        {
            var animation = _escort.EncounterAnimation;
            if (animation == null)
            {
                _escort.SetStagedWeaponVisible(drawing);
                yield break;
            }

            const float duration = 0.43f;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                float progress = drawing ? elapsed / duration : 1f - elapsed / duration;
                PoseEscortDraw(animation, progress);
                yield return null;
            }
            _escort.SetStagedWeaponVisible(drawing);
            animation.ClearStagedGrip(holdAtSide: drawing);
        }

        void PoseEscortDraw(CombatHumanoidAnimation animation, float progress)
        {
            Transform actor = _escort.transform;
            Pose holster = new(actor.TransformPoint(new Vector3(0.245f, 1.0f, -0.085f)),
                actor.rotation * Quaternion.Euler(86f, 0f, -10f));
            float grip = Ease(Mathf.InverseLerp(0f, 0.40f, progress));
            float lift = Ease(Mathf.InverseLerp(0.40f, 1f, progress));
            animation.StageGrip(BlendPose(holster, animation.LoweredPistolPose, lift), grip, grip);
            _escort.SetStagedWeaponVisible(progress >= 0.40f);
        }

        static float Ease(float value) => Mathf.SmoothStep(0f, 1f, value);

        static float WalkProgress(float time)
        {
            const float ramp = 0.16f;
            float half = Mathf.Min(time, 1f - time);
            float distance = half < ramp ? half * half / (2f * ramp) : half - ramp * 0.5f;
            float progress = distance / (1f - ramp);
            return time < 0.5f ? progress : 1f - progress;
        }

        static Pose BlendPose(Pose from, Pose to, float t) =>
            new(Vector3.Lerp(from.position, to.position, t), Quaternion.Slerp(from.rotation, to.rotation, t));

        void PlaceFacingPlayer(HostileNpc actor, Vector3 localPosition)
        {
            Vector3 position = transform.TransformPoint(localPosition);
            Vector3 facing = Vector3.ProjectOnPlane(PlayerChest - position, Vector3.up);
            Quaternion rotation = facing.sqrMagnitude > 0.001f ? Quaternion.LookRotation(facing) : transform.rotation;
            actor.transform.SetPositionAndRotation(position, rotation);
            actor.gameObject.SetActive(true);
        }

        IEnumerator Line(string text, float duration, string clipPath = null, HostileNpc speaker = null)
        {
            AudioClip clip = DialogueVoice.GetStoryClip(clipPath);
            bool visible = false;
            for (float remaining = duration; remaining > 0f;)
            {
                if (_pistol.IsEquipped)
                    visible = false;
                else
                {
                    if (!visible)
                        remaining = ShowDialogueSubtitle(text, remaining, clip, speaker);
                    visible = true;
                    remaining -= Time.deltaTime;
                }
                yield return null;
            }
        }

        float ShowVoicedSubtitle(string text, float duration, string clipPath, HostileNpc speaker = null) =>
            ShowDialogueSubtitle(text, duration, DialogueVoice.GetStoryClip(clipPath), speaker);

        float ShowDialogueSubtitle(string text, float duration, AudioClip clip, HostileNpc speaker)
        {
            StopDialogueVoice();
            if (clip != null)
            {
                if (_dialogueVoice == null)
                    _dialogueVoice = DialogueVoice.CreateSource(transform, "Final Encounter Voice",
                        minDistance: 6f, maxDistance: 40f);
                _dialogueVoice.spatialBlend = speaker != null ? 1f : 0f;
                if (speaker != null)
                    _dialogueVoice.transform.position = speaker.HeadPosition;
                _dialogueVoice.clip = clip;
                _dialogueVoice.Play();
                duration = Mathf.Max(duration, clip.length + 0.12f);
            }
            if (speaker != null && speaker.IsAlive && duration > 0f)
            {
                string spoken = NpcSpeechState.SpokenText(text);
                float speechDuration = clip != null ? clip.length :
                    Mathf.Min(duration, Mathf.Clamp(0.6f + spoken.Length / 16f, 1.1f, 8f));
                _dialogueSpeaker = speaker;
                _spokenUntil = Time.time + speechDuration;
                NpcSpeechState.GetOrAdd(speaker).BeginLine(spoken, speechDuration, clip != null ? _dialogueVoice : null, clip);
            }
            _hud.ShowSubtitle(text, duration: duration);
            return duration;
        }

        void StopDialogueVoice()
        {
            bool hasVoice = _dialogueVoice != null && _dialogueVoice.clip != null;
            if (_dialogueSpeaker != null && _dialogueSpeaker.TryGetComponent<NpcSpeechState>(out var speech))
                speech.EndLine(hasVoice ? _dialogueVoice : null);
            if (hasVoice)
            {
                _dialogueVoice.Stop();
                _dialogueVoice.clip = null;
            }
            _dialogueSpeaker = null;
            _spokenUntil = 0f;
        }

        void StopEncounterPlayback()
        {
            StopAllCoroutines();
            StopDialogueVoice();
            _escortStaged = false;
            _escortThreateningFather = false;
            if (_escort != null && _escort.EncounterAnimation is { } escortAnimation)
            {
                escortAnimation.SetBackpedaling(false);
                escortAnimation.ClearStagedGrip();
            }
            if (_father != null)
                _father.EncounterAnimation?.ClearStagedGrip();
        }

        void UpdateChoice()
        {
            int seconds = Mathf.Max(0, Mathf.CeilToInt(_deadline - _menus.UnpausedTime));
            if (seconds == 0)
            {
                ExecutePlayer(_father);
                return;
            }

            if (seconds != _shownSeconds)
            {
                _shownSeconds = seconds;
                _menus.UpdateEncounterTimer(seconds);
            }
        }

        void SelectEncounterChoice(int choice)
        {
            if (_stage != Stage.Choice)
                return;
            if (_menus.UnpausedTime >= _deadline)
            {
                ExecutePlayer(_father);
                return;
            }

            if (choice == 1 && !_pistol.TryPrepareEncounterShot())
            {
                _menus.ShowEncounterChoiceHint("Алексей не может достать пистолет.");
                return;
            }

            if (_storyEndings && choice is >= 1 and <= 3)
            {
                _endingChoice = choice;
                _menus.SetDeathEnding(BadEndingTitle, returnToMainMenu: choice == 2);
            }

            if (choice is 1 or 2)
            {
                _stage = Stage.ChoiceCinematic;
                _menus.BeginEncounterCinematic();
                StartCoroutine(choice == 1 ? PlayFatherShot() : PlayFatherPersuasion());
                return;
            }

            ShowCombatTutorial(fatherHelps: true);
        }

        string BadEndingTitle => $"Плохая концовка {_endingChoice}";

        void TryEndAtFather()
        {
            if (_menus.BlocksGameplayInput)
                return;

            Vector3 fatherPosition = _father.transform.position;
            Vector3 offset = _player.transform.position - fatherPosition;
            if (offset.sqrMagnitude > EndingApproachDistance * EndingApproachDistance)
                return;

            bool fatherAlive = _father.IsAlive;
            Vector3 target = fatherPosition + Vector3.up * (fatherAlive ? 1.2f : 0.35f);
            int sightLayers = Physics.DefaultRaycastLayers & ~(1 << _player.gameObject.layer);
            if (Physics.Linecast(PlayerChest, target, out var hit, sightLayers,
                    QueryTriggerInteraction.Ignore) && !hit.collider.transform.IsChildOf(_father.transform))
                return;

            _awaitingEnding = false;
            if (fatherAlive)
                _father.FaceForEncounter(PlayerChest);

            string title = fatherAlive && _endingChoice is 2 or 3
                ? (_endingChoice == 2 ? "Хорошая концовка" : "Концовка 3")
                : BadEndingTitle;
            _menus.ShowStoryEnding(title, fatherAlive ? FatherSurvivalLine : FatherDeathLine,
                success: fatherAlive, returnToMainMenu: _endingChoice == 2);
        }

        IEnumerator PlayFatherShot()
        {
            const float lookDuration = 0.75f;
            const float aimCorrectionDuration = 0.25f;
            Quaternion nearHead = LookAtFatherHead() * Quaternion.Euler(1.4f, -2.2f, 0f);
            yield return TurnEncounterCamera(EncounterLookCenter, nearHead, lookDuration);

            yield return null;
            _pistol.BeginEncounterShot();
            yield return new WaitUntil(() => _pistol.IsEncounterAiming);
            yield return TurnEncounterCamera(nearHead, LookAtFatherHead(), aimCorrectionDuration, endInputWeight: 0f);

            Quaternion aim = LookAtFatherHead();
            SetEncounterLook(aim, 0f);
            yield return null;
            if (_menus.IsPaused)
                yield return new WaitWhile(() => _menus.IsPaused);
            _pistol.FireEncounterShot();
            _father.TakeFatalShot();

            const float recoilTime = 0.22f;
            Quaternion kick = aim * Quaternion.Euler(-2f, 0.45f, 0f);
            yield return TurnEncounterCamera(kick, aim, recoilTime, startInputWeight: 0f);
            ShowCombatTutorial(fatherHelps: false);
        }

        Quaternion LookAtFatherHead() => Quaternion.LookRotation(_father.HeadPosition - EncounterView.position, Vector3.up);

        IEnumerator TurnEncounterCamera(Quaternion start, Quaternion target, float duration,
            float startInputWeight = 1f, float endInputWeight = 1f)
        {
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                float progress = Mathf.SmoothStep(0f, 1f, elapsed / duration);
                SetEncounterLook(Quaternion.Slerp(start, target, progress),
                    Mathf.Lerp(startInputWeight, endInputWeight, progress));
                yield return null;
            }
            SetEncounterLook(target, endInputWeight);
        }

        void SetEncounterLook(Quaternion rotation, float inputWeight)
        {
            if (_movement.CinemachineCameraTarget != null)
                _movement.SetEncounterLookRotation(rotation, inputWeight);
            else
                EncounterView.rotation = rotation;
        }

        IEnumerator PlayFatherPersuasion()
        {
            _movement.AllowFreeEncounterLook();
            yield return Line("Алексей: Помнишь кошелёк у магазина? Я говорил: никто не видел.",
                4.3f, "Alexey/Final_RememberWallet");
            yield return Line("Александр: Лёша, не надо…", 1.6f, "Alexander/Dont", _father);
            yield return Line("Алексей: Ты сказал: закон для всех один. И для нас тоже.",
                3.6f, "Alexey/Final_LawForEveryone");
            yield return Line("Александр: Я хотел всё вернуть. Деньги, всё…", 2.8f, "Alexander/GiveItBack", _father);
            yield return Line("Алексей: А сейчас хочешь заплатить мной.", 2.4f, "Alexey/Final_PayWithMe");
            yield return Line("Босс: Хватит. Он тебе свободу не даст. Я дам.", 2.8f, "Boss/StopListening", _boss);
            yield return Line("Александр: Если я не выстрелю, он нас обоих…", 2.7f, "Alexander/IfIDontShoot", _father);
            yield return Line("Александр: Ты меня выведешь?", 1.8f, "Alexander/WillYouGetMeOut", _father);
            yield return Line("Алексей: Да. За сделанное ответишь. Но здесь не останешься.",
                3.7f, "Alexey/Final_LeaveTogether");
            yield return Line("Охранник: Давай сюда.", 1.2f, speaker: _escort);
            ShowCombatTutorial(fatherHelps: false, resume: BeginFatherRescue);
        }

        void BeginFatherRescue()
        {
            if (_stage != Stage.CombatTutorial)
                return;

            _stage = Stage.FatherRescue;
            StartCoroutine(PlayFatherRescue());
        }

        IEnumerator PlayFatherRescue()
        {
            HostileNpc victim = Random.Range(0, 3) switch
            {
                0 => _boss,
                1 => _bossGuard,
                _ => _escort
            };
            HostileNpc first = victim == _boss ? _bossGuard : _boss;
            HostileNpc second = victim == _escort ? _bossGuard : _escort;
            first.SetStagedWeaponVisible(true);
            second.SetStagedWeaponVisible(true);

            Transform father = _father.transform;
            Vector3 victimPoint = FatherTargetPoint(victim);
            yield return TurnFatherTowards(victimPoint, 0.46f, () => _fatherGun.AimStagedWeapon(victimPoint, smooth: true));
            _menus.UnlockEncounterInput();
            _movement.ControlSprintEnabled = true;
            _father.AimForEncounter(victimPoint);
            _father.PlayStagedShot();
            victim.TakeFatalShot();
            ShowVoicedSubtitle(FatherCombatWarning, 1.8f, "Alexander/GetAside", _father);
            yield return new WaitForSeconds(0.2f);

            Vector3 cellDoor = transform.TransformPoint(_fatherCellPeek);
            Vector3[] escapeRoute = FindFatherRoute(cellDoor);
            int escapeCorner = 0;
            while (escapeCorner + 1 < escapeRoute.Length && FatherAt(escapeRoute[escapeCorner]))
                escapeCorner++;
            yield return TurnFatherTowards(escapeRoute[escapeCorner], 0.65f, () =>
            {
                _fatherGun.LowerStagedWeapon(smooth: true);
                TrackFather(first, second);
            });
            _father.LowerStagedWeapon();
            first.SetStagedDamageEnabled(true);
            second.SetStagedDamageEnabled(true);

            float elapsed = 0f;
            float runSpeed = 0f;
            bool firstShotFired = false;
            while (first.IsAlive || second.IsAlive)
            {
                TrackFather(first, second);
                if (!firstShotFired && elapsed >= FatherRescueFirstShotAt)
                {
                    FireAtFather(first.IsAlive ? first : second);
                    firstShotFired = true;
                }
                if (elapsed >= FatherRescueWindow)
                    break;

                yield return null;
                float step = Mathf.Min(Time.deltaTime, FatherRescueWindow - elapsed);
                runSpeed = Mathf.MoveTowards(runSpeed, FatherRunSpeed, FatherEscapeAcceleration * step);
                Vector3 next = escapeRoute[escapeCorner];
                _father.FaceForEncounter(next, smooth: true);
                father.position = Vector3.MoveTowards(father.position, next, runSpeed * step);
                if (FatherAt(next) && escapeCorner + 1 < escapeRoute.Length)
                    escapeCorner++;
                elapsed += step;
            }

            _fatherSavedInRescue = !first.IsAlive && !second.IsAlive;
            if (_fatherSavedInRescue)
                yield return FatherReturnsToCell();
            else
            {
                FireAtFather(second.IsAlive ? second : first);
                _father.TakeFatalShot();
            }

            ResumeFight();
            if (_fatherSavedInRescue)
                StartCoroutine(FatherKeepsWatchFromCover(allowSupportShots: false));
        }

        IEnumerator TurnFatherTowards(Vector3 point, float duration, System.Action updatePose = null)
        {
            Transform father = _father.transform;
            Vector3 direction = Vector3.ProjectOnPlane(point - father.position, Vector3.up);
            Quaternion start = father.rotation;
            Quaternion facing = direction.sqrMagnitude > 0.001f ? Quaternion.LookRotation(direction) : start;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                father.rotation = Quaternion.Slerp(start, facing, Mathf.SmoothStep(0f, 1f, elapsed / duration));
                updatePose?.Invoke();
                yield return null;
            }
            father.rotation = facing;
        }

        void TrackFather(HostileNpc first, HostileNpc second)
        {
            Vector3 target = FatherTargetPoint(_father);
            if (first.IsAlive)
                first.AimForEncounter(target, smooth: true);
            if (second.IsAlive)
                second.AimForEncounter(target, smooth: true);
        }

        void FireAtFather(HostileNpc shooter)
        {
            shooter.SetStagedWeaponVisible(true);
            shooter.AimForEncounter(FatherTargetPoint(_father));
            shooter.PlayStagedShot();
            _father.ReactToStagedShot();
        }

        void ShowCombatTutorial(bool fatherHelps, System.Action resume = null)
        {
            _stage = Stage.CombatTutorial;
            _fatherHelpsInFight = fatherHelps;
            StartCombatStress();
            resume ??= ResumeFight;
            bool tutorialOpened = _menus.ShowEncounterCombatTutorial(resume);
            if (fatherHelps)
                StartCoroutine(FatherSupportsPlayer());
            if (!tutorialOpened)
                resume();
        }

        void ResumeFight()
        {
            if (_stage != Stage.CombatTutorial && _stage != Stage.FatherRescue)
                return;

            bool followsRescue = _stage == Stage.FatherRescue;
            _stage = Stage.FightOpening;
            _deadline = Time.time + _openingDelay;
            _openingHitChance = followsRescue ? PersuasionFightHitChance : null;
            PrepareGuardsForFight(aimImmediately: !followsRescue);
            if (!followsRescue)
                _pistol.SetEquipped(true);
            if (_boss.IsAlive)
                ShowVoicedSubtitle("Босс: К двери не пускать!", 3f, "Boss/BlockExit", _boss);
            else
                ShowDialogueSubtitle("Охранник: К двери не пускать!", 3f, null,
                    System.Array.Find(_enemies, enemy => enemy != null && enemy.IsAlive));
        }

        IEnumerator FatherSupportsPlayer()
        {
            yield return new WaitWhile(() => _stage == Stage.CombatTutorial);
            if (_stage is not (Stage.FightOpening or Stage.Fight))
                yield break;
            yield return new WaitForSeconds(FatherEscapeDelay);
            yield return FatherReturnsToCell();
            Vector3 peek = transform.TransformPoint(_fatherCellPeek);
            Vector3 hide = transform.TransformPoint(_fatherCellCover);
            float timeToDoor = Vector3.Distance(_father.transform.position, peek) / FatherPeekSpeed;
            yield return new WaitForSeconds(Mathf.Max(0f, Random.Range(2f, 3f) - timeToDoor));

            _father.SetStagedWeaponVisible(true);

            int shotsLeft = Random.Range(2, 5);
            int blockedPeeks = 0;
            int returnHits = 0;
            var escortGun = _escort.GetComponent<BanditPistol>();
            while (_father.IsAlive && _escort.IsAlive && blockedPeeks < 3)
            {
                blockedPeeks++;
                yield return FatherPeekFromCover(peek, hide, _escort, () =>
                {
                    blockedPeeks = 0;
                    if (--shotsLeft == 0)
                    {
                        _escort.TakeFatalShot();
                        ShowVoicedSubtitle(FatherCombatWarning, 1.8f, "Alexander/GetAside", _father);
                    }
                    else
                    {
                        _escort.SetStagedWeaponVisible(true);
                        _escort.AimForEncounter(FatherTargetPoint(_father));
                        if (_endingChoice == 3 && FatherCanSee(escortGun.StagedMuzzlePosition, _father))
                        {
                            FireAtFather(_escort);
                            if (++returnHits >= 2)
                                _father.TakeFatalShot();
                        }
                    }
                });
                if (_father.IsAlive && _escort.IsAlive)
                    yield return new WaitForSeconds(Random.Range(0.65f, 1.1f));
            }

            // Keep the encounter completable if a changed map blocks the scripted exchange.
            if (_escort.IsAlive)
            {
                _fatherHelpsInFight = false;
                if (_stage == Stage.Fight)
                    _escort.BeginEncounterCombat(_openingTarget, _openingHitChance, holdCover: true);
            }
            if (_father.IsAlive)
                yield return FatherKeepsWatchFromCover(allowSupportShots: true);
        }

        IEnumerator FatherKeepsWatchFromCover(bool allowSupportShots)
        {
            var path = new NavMeshPath();
            var retry = new WaitForSeconds(0.45f);
            Vector3 cellPeek = transform.TransformPoint(_fatherCellPeek);
            Vector3 cellHide = transform.TransformPoint(_fatherCellCover);
            int shotsLeft = allowSupportShots ? Random.Range(3, 5) : 0;
            while (_father.IsAlive && (_stage is Stage.FightOpening or Stage.Fight))
            {
                if (shotsLeft == 0 || _stage != Stage.Fight || _boss.IsAlive || _bossGuard.IsAlive || _escort.IsAlive ||
                    !TryFindFatherCover(path, out var route, out var left, out var right))
                {
                    yield return new WaitForSeconds(Random.Range(3.5f, 5.5f));
                    if (!FatherAt(cellHide))
                        yield break;

                    HostileNpc target = null;
                    if (shotsLeft > 0 && _stage == Stage.Fight)
                        TryGetFatherTarget(cellPeek + Vector3.up * 1.4f, out target);
                    yield return FatherPeekFromCover(cellPeek, cellHide, target, () => shotsLeft--);
                    continue;
                }

                foreach (var corner in route)
                {
                    yield return MoveFatherInWorld(corner, FatherRunSpeed, requireSafeRoute: true);
                    if (!FatherAt(corner))
                        break;
                }

                Vector3 hide = route[route.Length - 1];
                if (!FatherAt(hide))
                {
                    ReleaseFatherCover();
                    yield return FatherReturnsToCell(fromHallCover: true);
                    continue;
                }

                while (shotsLeft > 0 && _fatherSupportCover != null && _fatherSupportCover.IsReservedBy(_fatherGun))
                {
                    if (!TryFindFatherShot(hide, left, right, out var peek, out var enemy))
                    {
                        yield return retry;
                        continue;
                    }

                    yield return FatherPeekFromCover(peek, hide, enemy, () => shotsLeft--, requireSafeRoute: true);
                    if (shotsLeft > 0)
                        yield return new WaitForSeconds(Random.Range(3.5f, 5.5f));
                }

                if (shotsLeft == 0)
                    yield break;

                ReleaseFatherCover();
                yield return FatherReturnsToCell(fromHallCover: true);
            }
        }

        IEnumerator FatherPeekFromCover(Vector3 peek, Vector3 hide, HostileNpc target, System.Action onShot,
            bool requireSafeRoute = false)
        {
            if (!_father.IsAlive)
                yield break;

            yield return MoveFatherInWorld(peek, FatherPeekSpeed, requireSafeRoute, aimAt: target);
            if (FatherAt(peek) && (!requireSafeRoute || FatherRouteIsSafe(peek, peek)))
            {
                bool fired = false;
                if (target != null && target.IsAlive)
                {
                    Physics.SyncTransforms();
                    Vector3 muzzle = _fatherGun.StagedMuzzlePosition;
                    Vector3 chest = FatherTargetPoint(_father);
                    if (FatherCanSee(_father.HeadPosition, target) &&
                        !Physics.Linecast(chest, muzzle, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
                        FatherCanSee(muzzle, target))
                    {
                        _father.PlayStagedShot();
                        onShot();
                        fired = true;
                    }
                }
                else
                    yield return TurnFatherTowards(transform.TransformPoint(_center), 0.19f);

                yield return new WaitForSeconds(fired ? FatherPeekShotTime : FatherPeekLookTime);
            }

            if (_father.IsAlive)
            {
                _father.LowerStagedWeapon();
                yield return MoveFatherInWorld(hide, FatherPeekSpeed);
            }
        }

        bool TryFindFatherCover(NavMeshPath path, out Vector3[] route, out Vector3? left, out Vector3? right)
        {
            route = null;
            left = right = null;
            Vector3 start = _father.transform.position;
            if (!NavMesh.SamplePosition(start, out var startHit, 0.6f, NavMesh.AllAreas))
                return false;

            HostileNpc nearest = null;
            float nearestDistance = float.PositiveInfinity;
            foreach (var enemy in _enemies)
            {
                if (enemy == null || !enemy.IsAlive)
                    continue;
                float distance = (enemy.transform.position - start).sqrMagnitude;
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = enemy;
                }
            }
            if (nearest == null)
                return false;

            BanditCover bestCover = null;
            float bestDistance = FatherCoverSearchRadius * FatherCoverSearchRadius;
            foreach (var cover in BanditCover.Active)
            {
                if (cover.gameObject.scene != gameObject.scene || !cover.IsAvailable)
                    continue;

                cover.GetPositions(FatherTargetPoint(nearest), false, out var hide, out var peekLeft, out var peekRight);
                float distance = (hide - start).sqrMagnitude;
                if (distance >= bestDistance ||
                    !NavMesh.SamplePosition(hide, out var hideHit, 0.45f, NavMesh.AllAreas) ||
                    !FatherHasCover(cover, hideHit.position) ||
                    !NavMesh.CalculatePath(startHit.position, hideHit.position, NavMesh.AllAreas, path) ||
                    path.status != NavMeshPathStatus.PathComplete)
                    continue;

                Vector3[] corners = path.corners;
                if (corners.Length == 0 || !FatherPathIsSafe(start, corners))
                    continue;

                Vector3? leftEdge = SampleFatherPeek(hideHit.position, peekLeft);
                Vector3? rightEdge = SampleFatherPeek(hideHit.position, peekRight);
                if (!leftEdge.HasValue && !rightEdge.HasValue)
                    continue;

                bestDistance = distance;
                bestCover = cover;
                route = corners;
                left = leftEdge;
                right = rightEdge;
            }

            if (bestCover == null)
                return false;

            _fatherSupportCover = bestCover;
            _fatherSupportCover.Reserve(_fatherGun);
            return true;
        }

        static Vector3? SampleFatherPeek(Vector3 hide, Vector3 position)
        {
            if (NavMesh.SamplePosition(position, out var hit, 0.45f, NavMesh.AllAreas) &&
                !NavMesh.Raycast(hide, hit.position, out _, NavMesh.AllAreas))
                return hit.position;

            return null;
        }

        bool FatherHasCover(BanditCover cover, Vector3 hide)
        {
            foreach (var enemy in _enemies)
            {
                if (enemy != null && enemy.IsAlive && !cover.ProtectsFrom(FatherTargetPoint(enemy), hide))
                    return false;
            }
            return true;
        }

        bool FatherPathIsSafe(Vector3 start, Vector3[] corners)
        {
            foreach (var corner in corners)
            {
                if (!FatherRouteIsSafe(start, corner))
                    return false;
                start = corner;
            }
            return true;
        }

        bool FatherRouteIsSafe(Vector3 start, Vector3 destination)
        {
            Vector3 step = Vector3.ProjectOnPlane(destination - start, Vector3.up);
            float lengthSquared = step.sqrMagnitude;
            foreach (var enemy in _enemies)
            {
                if (enemy == null || !enemy.IsAlive)
                    continue;
                Vector3 offset = Vector3.ProjectOnPlane(enemy.transform.position - start, Vector3.up);
                float along = lengthSquared > 0f ? Mathf.Clamp01(Vector3.Dot(offset, step) / lengthSquared) : 0f;
                if ((offset - step * along).sqrMagnitude < FatherSafeDistance * FatherSafeDistance)
                    return false;
            }
            return true;
        }

        bool TryFindFatherShot(Vector3 hide, Vector3? left, Vector3? right, out Vector3 peek, out HostileNpc target)
        {
            return CheckEdge(left, out peek, out target) || CheckEdge(right, out peek, out target);

            bool CheckEdge(Vector3? edge, out Vector3 position, out HostileNpc enemy)
            {
                position = edge.GetValueOrDefault();
                enemy = null;
                return edge.HasValue && FatherRouteIsSafe(hide, position) &&
                    TryGetFatherTarget(position + Vector3.up * 1.4f, out enemy);
            }
        }

        void ReleaseFatherCover()
        {
            if (_fatherSupportCover != null)
                _fatherSupportCover.Release(_fatherGun);
            _fatherSupportCover = null;
        }

        bool FatherAt(Vector3 destination) => (_father.transform.position - destination).sqrMagnitude <= 0.04f;

        IEnumerator FatherReturnsToCell(bool fromHallCover = false)
        {
            _father.LowerStagedWeapon();
            if (fromHallCover || transform.InverseTransformPoint(_father.transform.position).x < _fatherCellPeek.x)
                foreach (var corner in FindFatherRoute(transform.TransformPoint(_fatherCellPeek)))
                    yield return MoveFatherInWorld(corner, FatherRunSpeed);
            yield return MoveFatherTo(_fatherCellCover, FatherRunSpeed);
        }

        Vector3[] FindFatherRoute(Vector3 destination)
        {
            var path = new NavMeshPath();
            if (NavMesh.SamplePosition(_father.transform.position, out var start, 0.6f, NavMesh.AllAreas) &&
                NavMesh.SamplePosition(destination, out var end, 0.6f, NavMesh.AllAreas) &&
                NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path) &&
                path.status == NavMeshPathStatus.PathComplete)
            {
                Vector3[] corners = path.corners;
                if (corners.Length > 1)
                    return corners;
            }
            return new[] { destination };
        }

        IEnumerator MoveFatherTo(Vector3 localPosition, float speed) =>
            MoveFatherInWorld(transform.TransformPoint(localPosition), speed);

        IEnumerator MoveFatherInWorld(Vector3 destination, float speed, bool requireSafeRoute = false,
            HostileNpc aimAt = null)
        {
            Transform actor = _father.transform;
            Vector3 direction = Vector3.ProjectOnPlane(destination - actor.position, Vector3.up);
            Quaternion facing = direction.sqrMagnitude > 0.001f ? Quaternion.LookRotation(direction) : actor.rotation;
            float stepSpeed = 0f;

            while ((actor.position - destination).sqrMagnitude > 0.0001f)
            {
                if (requireSafeRoute && !FatherRouteIsSafe(actor.position, destination))
                    yield break;
                float deltaTime = Time.deltaTime;
                if (aimAt != null && aimAt.IsAlive)
                    _father.AimForEncounter(FatherTargetPoint(aimAt), smooth: true);
                else
                    _father.FaceForEncounter(actor.position + facing * Vector3.forward, smooth: true);
                float remaining = Vector3.Distance(actor.position, destination);
                float arrivalSpeed = Mathf.Min(speed, Mathf.Sqrt(2f * FatherEscapeAcceleration * remaining));
                stepSpeed = Mathf.MoveTowards(stepSpeed, arrivalSpeed, FatherEscapeAcceleration * deltaTime);
                actor.position = Vector3.MoveTowards(actor.position, destination, stepSpeed * deltaTime);
                yield return null;
            }

            actor.position = destination;
        }

        bool TryGetFatherTarget(Vector3 from, out HostileNpc target)
        {
            target = null;
            float bestDistance = float.PositiveInfinity;

            foreach (var enemy in _enemies)
            {
                if (enemy == null || !enemy.IsAlive)
                    continue;

                Vector3 point = FatherTargetPoint(enemy);
                float distance = (point - from).sqrMagnitude;
                if (distance >= bestDistance || !FatherCanSee(from, enemy))
                    continue;

                bestDistance = distance;
                target = enemy;
            }

            return target != null;
        }

        static Vector3 FatherTargetPoint(HostileNpc enemy) => enemy.transform.position + Vector3.up * 1.4f;

        static bool FatherCanSee(Vector3 from, HostileNpc enemy) =>
            !Physics.Linecast(from, FatherTargetPoint(enemy), out var hit, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore) || hit.collider.transform.IsChildOf(enemy.transform);

        void OnPlayerShot(Vector3 origin)
        {
            if (!isActiveAndEnabled || _player.IsDead)
                return;

            switch (_stage)
            {
                case Stage.AwaitingPlayer when _entrance.Contains(transform.InverseTransformPoint(origin)):
                case Stage.Arrival:
                case Stage.Holster:
                case Stage.WalkToCenter:
                case Stage.Dialogue:
                    StartEarlyFight(origin);
                    break;
                case Stage.FightOpening:
                    _openingTarget = PlayerChest;
                    GetComponent<BanditSquad>().ReportShot(origin, Time.time);
                    ReleaseGuards(_openingHitChance);
                    break;
            }
        }

        void StartEarlyFight(Vector3 shotPosition)
        {
            StopEncounterPlayback();
            _fatherWasExecuted = true;

            _father.transform.SetPositionAndRotation(transform.TransformPoint(_fatherBodyPosition),
                transform.rotation * Quaternion.Euler(0f, -90f, 0f));
            _father.SetStagedWeaponVisible(false);
            _father.LeaveBodyForEncounter();
            // If the escort is still inside, bring him through the opening to join the fight.
            if (transform.InverseTransformPoint(_escort.transform.position).x > _escortFightEntry.x)
                PlaceFacingPlayer(_escort, _escortFightEntry);

            PlayExecutionShot();
            StartCombatStress();
            PrepareGuardsForFight();
            GetComponent<BanditSquad>().ReportShot(shotPosition, Time.time);
            ReleaseGuards(EarlyFightHitChance);
            ShowVoicedSubtitle("Босс: Убейте Александра! Огонь!", 3f, "Boss/EarlyFight", _boss);
        }

        void OnPlayerShotLine(Ray shot, float reach, HostileNpc hit)
        {
            if (_stage != Stage.Fight || _rearOpeningCoverTime <= 0f ||
                (hit != _rearGuard && hit != _rearRightGuard &&
                    !IsRearGuardNearShot(_rearGuard, shot, reach) && !IsRearGuardNearShot(_rearRightGuard, shot, reach)))
                return;

            if (_rearGuard.IsAlive)
                _rearGuard.EndOpeningCover();
            if (_rearRightGuard.IsAlive)
                _rearRightGuard.EndOpeningCover();
        }

        static bool IsRearGuardNearShot(HostileNpc guard, Ray shot, float reach)
        {
            if (!guard.IsAlive)
                return false;

            Vector3 offset = guard.transform.position + Vector3.up * 1.1f - shot.origin;
            float along = Vector3.Dot(offset, shot.direction);
            return along >= 0f && along <= reach &&
                (offset - along * shot.direction).sqrMagnitude <= 0.85f * 0.85f;
        }

        void StartCombatStress()
        {
            if (!_encounterStressStarted)
            {
                _stressBeforeEncounter = _pistol.CombatStressActive;
                _encounterStressStarted = true;
            }
            _pistol.SetCombatStress(true);
        }

        void PrepareGuardsForFight(bool aimImmediately = true)
        {
            _openingTarget = PlayerChest;
            _menus.UnlockEncounterInput();
            _movement.ControlSprintEnabled = true;
            if (aimImmediately)
                AimGuardsAtOpeningTarget(smooth: false);
        }

        void AimGuardsAtOpeningTarget(bool smooth)
        {
            foreach (var enemy in _enemies)
            {
                if (enemy != null && enemy.IsAlive)
                {
                    if (enemy == _escort && _fatherHelpsInFight)
                        continue;
                    enemy.SetStagedWeaponVisible(true);
                    if (enemy == _boss && BossIsSpeaking)
                        enemy.SpeakAtForEncounter(_openingTarget);
                    else
                        enemy.AimForEncounter(_openingTarget, smooth);
                }
            }
        }

        void ReleaseGuards(float? visibleHitChance = null)
        {
            _stage = Stage.Fight;
            if (!_fatherWasExecuted)
                GetComponent<BanditSquad>().PrepareOpeningWarnings();
            foreach (var enemy in _enemies)
            {
                // The escort is held back only when Alexander takes him out from the cell.
                if (enemy != null && enemy.IsAlive &&
                    (enemy != _escort || !_fatherHelpsInFight))
                    enemy.BeginEncounterCombat(_openingTarget, visibleHitChance, holdCover: !_fatherWasExecuted,
                        openingCoverSeconds: enemy == _rearGuard || enemy == _rearRightGuard ? _rearOpeningCoverTime : 0f);
            }
        }

        void ExecutePlayer(HostileNpc shooter)
        {
            shooter.SetStagedWeaponVisible(true);
            shooter.AimForEncounter(PlayerChest);
            if (shooter == _father)
                shooter.PlayStagedShot(playAudio: false);
            Finish();
            PlayExecutionShot();
            _player.TakeDamage(PlayerHealth.MaxHealth);
        }

        bool TryExecuteRetreat()
        {
            if (_stage == Stage.Finished || _hallDoor == null || !_hallDoor.OpeningStarted ||
                _hallDoor.transform.InverseTransformPoint(_player.transform.position).z >= -HallRetreatDistance)
                return false;

            HostileNpc shooter = _rearGuard.IsAlive
                ? _rearGuard
                : System.Array.Find(_enemies, enemy => enemy != null && enemy.IsAlive);
            if (shooter == null)
                return false;

            ExecutePlayer(shooter);
            return true;
        }

        void PlayExecutionShot()
        {
            if (_executionShot != null)
                _shotAudio.PlayOneShot(_executionShot, 0.85f);
        }

        void Finish(bool wonFight = false)
        {
            if (_encounterStressStarted)
                _pistol.SetCombatStress(_stressBeforeEncounter);
            _stage = Stage.Finished;
            StopEncounterPlayback();
            _pistol.EndEncounterShot();
            foreach (var enemy in _enemies)
                if (enemy != null)
                    enemy.SetStagedDamageEnabled(false);
            bool fromHallCover = _fatherSupportCover != null;
            ReleaseFatherCover();
            if (_father != null && _father.IsAlive)
            {
                _father.LowerStagedWeapon();
                if (wonFight && !_fatherSavedInRescue)
                    StartCoroutine(FatherReturnsToCell(fromHallCover));
            }
            _menus.CloseEncounterOverlay();
            _menus.UnlockEncounterInput();
            _movement.ControlSprintEnabled = false;
        }

        void OnDisable()
        {
            if (_player != null && _stage != Stage.AwaitingPlayer && _stage != Stage.Finished)
                Finish();
            else
                StopEncounterPlayback();
        }

        void OnDestroy()
        {
            if (_pistol != null)
            {
                _pistol.ShotFired -= OnPlayerShot;
                _pistol.ShotLineFired -= OnPlayerShotLine;
            }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(_entrance.center, _entrance.size);
            Gizmos.DrawWireSphere(_center, _centerRadius);
        }
    }
}
