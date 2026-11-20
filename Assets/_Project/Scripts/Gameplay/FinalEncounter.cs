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
        [SerializeField] Material _actorMaterial;
        [SerializeField] AudioClip _executionShot;
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
        [SerializeField, Min(1f)] float _holsterTime = 8f;
        [SerializeField, Min(1f)] float _redrawHolsterTime = 10f;
        [SerializeField, Min(1f)] float _walkTime = 20f;
        [SerializeField, Min(1f)] float _choiceTime = 60f;
        [SerializeField, Min(0f)] float _openingDelay = 1f;

        static readonly Color BossColor = new Color32(255, 219, 45, 255);
        static readonly Color GuardColor = new Color32(225, 47, 47, 255);
        static readonly Color FatherColor = new Color32(65, 206, 81, 255);
        const float EarlyFightHitChance = 0.8f;
        const float PersuasionFightHitChance = 1f;
        const float FatherEscapeDelay = 1f;
        const float FatherRunSpeed = 5.4f;
        const float FatherPeekSpeed = 2.6f;
        const float FatherCoverSearchRadius = 12f;
        const float FatherSafeDistance = 6f;
        const float FatherRescueWindow = 1f;
        const float FatherRescueFirstShotAt = 0.18f;
        const float FatherEscapeAcceleration = 18f;
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
        HostileNpc _father;
        BanditPistol _fatherGun;
        BanditCover _fatherSupportCover;
        AudioSource _shotAudio;
        float _deadline;
        float _redrawDeadline = float.PositiveInfinity;
        int _shownSeconds;
        Vector3 _openingTarget;
        float? _openingHitChance;
        bool _stressBeforeEncounter;
        bool _encounterStressStarted;
        bool _readyForChoice;
        bool _fatherWasExecuted;
        bool _fatherShotByPlayer;
        bool _fatherHelpsInFight;
        bool _fatherSavedInRescue;

        Vector3 PlayerChest => _player.transform.position + Vector3.up * 1.4f;
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

            _boss = Spawn(_bossPosition, BossColor, "Mafia Boss");
            _bossGuard = Spawn(_bossGuardPosition, GuardColor, "Boss Guard");
            _escort = Spawn(_escortEntrance, GuardColor, "Escort Guard");
            _rearGuard = Spawn(_rearLeftPosition, GuardColor, "Entrance Guard Left");
            _enemies = new[]
            {
                _boss, _escort, _rearGuard,
                _bossGuard,
                Spawn(_rearRightPosition, GuardColor, "Entrance Guard Right")
            };
            _father = Spawn(_fatherEntrance, FatherColor, "Alexander");
            _fatherGun = _father.GetComponent<BanditPistol>();
            _father.SetStagedWeaponVisible(false);

            _shotAudio = gameObject.AddComponent<AudioSource>();
            _shotAudio.playOnAwake = false;
            _shotAudio.spatialBlend = 0f;
            _shotAudio.ignoreListenerPause = true;
            _pistol.ShotFired += OnPlayerShot;
        }

        void Update()
        {
            if (_player.IsDead)
            {
                if (_stage != Stage.Finished)
                    Finish();
                return;
            }

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
                    if (!CheckRedrawnWeapon() && _readyForChoice && !_pistol.IsEquipped)
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
                    if (_fatherWasExecuted)
                        _hud.ShowSubtitle("Алексей: Камера справа. Надо найти отца.", duration: 5f);
                    break;
            }
        }

        HostileNpc Spawn(Vector3 localPosition, Color tint, string actorName)
        {
            Vector3 position = transform.TransformPoint(localPosition);
            Vector3 facing = Vector3.ProjectOnPlane(transform.TransformPoint(_center) - position, Vector3.up);
            Quaternion rotation = facing.sqrMagnitude > 0.001f ? Quaternion.LookRotation(facing) : transform.rotation;
            var actor = Instantiate(_banditPrefab, position, rotation, transform);
            actor.name = actorName;
            actor.StageForEncounter(tint, _actorMaterial);
            return actor;
        }

        void GreetPlayer()
        {
            const float duration = 4.5f;
            _stage = Stage.Arrival;
            _deadline = Time.time + duration;
            _hud.ShowSubtitle("Босс: Сына Александра я ждал. Не так скоро. Хочешь увидеть отца — входи. Один.",
                duration: duration);
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
            _hud.ShowSubtitle("Босс: Покажи сначала, что умеешь слушать. Ствол вниз. [1 — убрать пистолет]",
                duration: _holsterTime);
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
            _hud.ShowSubtitle($"Босс: {prefix}Подойди к центру зала. [{Mathf.CeilToInt(remaining)} сек.]",
                duration: remaining);
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
                        _hud.ShowSubtitle(string.Empty, duration: 0f);
                }
                _redrawDeadline = float.PositiveInfinity;
                return false;
            }

            if (float.IsPositiveInfinity(_redrawDeadline))
            {
                _redrawDeadline = Time.time + _redrawHolsterTime;
                _hud.ShowSubtitle($"Босс: Я сказал, убери пистолет. [1 — убрать, {_redrawHolsterTime:0} сек.]",
                    duration: _redrawHolsterTime);
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
            _menus.LockEncounterInput(_boss.transform.position + Vector3.up * 1.65f, SkipDialogue);
            StartCoroutine(PlayDialogue());
        }

        void SkipDialogue()
        {
            if (_readyForChoice)
                return;

            StopAllCoroutines();
            SetFatherAndEscortAtMarks();
            PrepareChoice();
            if (!_pistol.IsEquipped)
                _hud.ShowSubtitle(string.Empty, duration: 0f);
        }

        IEnumerator PlayDialogue()
        {
            yield return Line("Алексей: Выведи его.", 1.7f);
            yield return Line("Босс: Выводи его сюда!", 1.8f);
            yield return BringOutFather();
            yield return Line("Александр: Лёша…", 1.7f);
            yield return Line("Алексей: Я здесь. Стой спокойно.", 2.2f);
            yield return Line("Босс: Деньги проиграл. Груз возил. Потом решил, что полиция тебя от нас спрячет.", 4.9f);
            yield return Line("Алексей: Я читал бумаги. Отпусти его.", 2.6f);
            yield return Line("Босс: Он хотел сдать нас. Просто убить его — слишком легко.", 3.6f);
            yield return Line("Александр: Что ты хочешь?", 1.9f);
            _father.SetStagedWeaponVisible(true);
            yield return Line("Босс: Убей сына — выйдешь отсюда.", 2.8f);
            yield return Line("Алексей: Пап!", 1.3f);
            yield return Line("Александр: Ты правда меня выпустишь?", 2.5f);
            yield return Line("Босс: Дверь видишь? Через неё и уйдёшь.", 2.8f);
            yield return Line("Александр: Лёша, я… Они меня здесь…", 2.7f);
            _father.AimForEncounter(PlayerChest);
            yield return Line("Алексей: Ты сейчас в меня целишься.", 2.5f);
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
            _deadline = Time.unscaledTime + _choiceTime;
            _shownSeconds = -1;
            UpdateChoice();
        }

        IEnumerator BringOutFather()
        {
            Vector3 fromFather = transform.TransformPoint(_fatherEntrance);
            Vector3 fromEscort = transform.TransformPoint(_escortEntrance);
            Vector3 toFather = transform.TransformPoint(_fatherPosition);
            Vector3 toEscort = transform.TransformPoint(_escortPosition);
            _father.transform.SetPositionAndRotation(fromFather, Quaternion.LookRotation(toFather - fromFather));
            _escort.transform.SetPositionAndRotation(fromEscort, Quaternion.LookRotation(toEscort - fromEscort));
            _father.gameObject.SetActive(true);
            _escort.gameObject.SetActive(true);

            const float duration = 2.4f;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                float progress = Mathf.SmoothStep(0f, 1f, elapsed / duration);
                _father.transform.position = Vector3.Lerp(fromFather, toFather, progress);
                _escort.transform.position = Vector3.Lerp(fromEscort, toEscort, progress);
                yield return null;
            }

            SetFatherAndEscortAtMarks();
        }

        void SetFatherAndEscortAtMarks()
        {
            PlaceFacingPlayer(_father, _fatherPosition);
            PlaceFacingPlayer(_escort, _escortPosition);
        }

        void PlaceFacingPlayer(HostileNpc actor, Vector3 localPosition)
        {
            Vector3 position = transform.TransformPoint(localPosition);
            Vector3 facing = Vector3.ProjectOnPlane(PlayerChest - position, Vector3.up);
            Quaternion rotation = facing.sqrMagnitude > 0.001f ? Quaternion.LookRotation(facing) : transform.rotation;
            actor.transform.SetPositionAndRotation(position, rotation);
            actor.gameObject.SetActive(true);
        }

        IEnumerator Line(string text, float duration)
        {
            bool visible = false;
            for (float remaining = duration; remaining > 0f;)
            {
                if (_pistol.IsEquipped)
                    visible = false;
                else
                {
                    if (!visible)
                        _hud.ShowSubtitle(text, duration: remaining);
                    visible = true;
                    remaining -= Time.deltaTime;
                }
                yield return null;
            }
        }

        void UpdateChoice()
        {
            int seconds = Mathf.Max(0, Mathf.CeilToInt(_deadline - Time.unscaledTime));
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
            if (Time.unscaledTime >= _deadline)
            {
                ExecutePlayer(_father);
                return;
            }

            if (choice is 1 or 2)
            {
                if (choice == 1 && !_pistol.TryPrepareEncounterShot())
                {
                    _menus.ShowEncounterChoiceHint("Алексей не может достать пистолет.");
                    return;
                }

                _stage = Stage.ChoiceCinematic;
                _menus.BeginEncounterCinematic(choice == 2 ? SkipFatherPersuasion : null);
                StartCoroutine(choice == 1 ? PlayFatherShot() : PlayFatherPersuasion());
                return;
            }

            ShowCombatTutorial(fatherHelps: true);
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
            _pistol.FireEncounterShot();
            _fatherShotByPlayer = true;
            _father.TakeFatalShot();

            const float recoilTime = 0.22f;
            Quaternion kick = aim * Quaternion.Euler(-2f, 0.45f, 0f);
            yield return TurnEncounterCamera(kick, aim, recoilTime, startInputWeight: 0f);
            const float farewellDuration = 1.1f;
            _hud.ShowSubtitle("Александр: Лёша…", duration: farewellDuration);
            yield return new WaitForSeconds(farewellDuration);
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
            yield return Line("Алексей: Помнишь кошелёк у магазина? Я говорил: никто не видел.", 4.3f);
            yield return Line("Александр: Лёша, не надо…", 1.6f);
            yield return Line("Алексей: Ты сказал: закон для всех один. И для нас тоже.", 3.6f);
            yield return Line("Александр: Я хотел всё вернуть. Деньги, всё…", 2.8f);
            yield return Line("Алексей: А сейчас хочешь заплатить мной.", 2.4f);
            yield return Line("Босс: Хватит. Он тебе свободу не даст. Я дам.", 2.8f);
            yield return Line("Александр: Если я не выстрелю, он нас обоих…", 2.7f);
            yield return Line("Алексей: Пока ты целишься в меня, ему спокойно. Посмотри на него.", 4.1f);
            yield return Line("Александр: Ты меня выведешь?", 1.8f);
            yield return Line("Алексей: Да. За сделанное ответишь. Но здесь не останешься.", 3.7f);
            yield return Line("Охранник: Давай сюда.", 1.2f);
            ShowCombatTutorial(fatherHelps: false, resume: BeginFatherRescue);
        }

        void SkipFatherPersuasion()
        {
            if (_stage != Stage.ChoiceCinematic)
                return;

            StopAllCoroutines();
            _hud.ShowSubtitle(string.Empty, duration: 0f);
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

            Transform father = _father.transform;
            Vector3 victimPoint = FatherTargetPoint(victim);
            yield return TurnFatherTowards(victimPoint, 0.46f, () => _fatherGun.AimStagedWeapon(victimPoint, smooth: true));
            _menus.UnlockEncounterInput();
            _movement.ControlSprintEnabled = true;
            _father.AimForEncounter(victimPoint);
            _father.PlayStagedShot();
            victim.TakeFatalShot();
            _hud.ShowSubtitle(FatherCombatWarning, duration: 1.8f);
            yield return new WaitForSeconds(0.2f);

            Vector3 cellDoor = transform.TransformPoint(_fatherCellPeek);
            yield return TurnFatherTowards(cellDoor, 0.65f, () =>
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
                father.position = Vector3.MoveTowards(father.position, cellDoor, runSpeed * step);
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
        }

        IEnumerator TurnFatherTowards(Vector3 point, float duration, System.Action updatePose)
        {
            Transform father = _father.transform;
            Vector3 direction = Vector3.ProjectOnPlane(point - father.position, Vector3.up);
            Quaternion start = father.rotation;
            Quaternion facing = direction.sqrMagnitude > 0.001f ? Quaternion.LookRotation(direction) : start;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                father.rotation = Quaternion.Slerp(start, facing, Mathf.SmoothStep(0f, 1f, elapsed / duration));
                updatePose();
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
            shooter.AimForEncounter(FatherTargetPoint(_father));
            shooter.PlayStagedShot();
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
            _hud.ShowSubtitle(_boss.IsAlive ? "Босс: К двери не пускать!" : "Охранник: К двери не пускать!",
                duration: 3f);
            if (_fatherShotByPlayer)
                ReleaseGuards();
        }

        IEnumerator FatherSupportsPlayer()
        {
            yield return new WaitForSecondsRealtime(FatherEscapeDelay);
            yield return FatherReturnsToCell();
            yield return new WaitWhile(() => _stage == Stage.CombatTutorial);
            const float aimTime = 0.25f;
            float timeToDoor = Vector3.Distance(_father.transform.position, transform.TransformPoint(_fatherCellPeek)) /
                FatherPeekSpeed;
            yield return new WaitForSeconds(Mathf.Max(0f, Random.Range(2f, 3f) - timeToDoor - aimTime));

            _father.SetStagedWeaponVisible(true);

            int shots = Random.Range(2, 5);
            for (int shot = 1; shot <= shots; shot++)
            {
                yield return MoveFatherTo(_fatherCellPeek, FatherPeekSpeed);
                _father.AimForEncounter(_escort.transform.position + Vector3.up * 1.4f);
                yield return new WaitForSeconds(aimTime);
                _father.PlayStagedShot();
                if (shot == shots)
                {
                    _escort.TakeFatalShot();
                    _hud.ShowSubtitle(FatherCombatWarning, duration: 1.8f);
                }
                else
                {
                    _escort.AimForEncounter(_father.transform.position + Vector3.up * 1.4f);
                    _father.LowerStagedWeapon();
                    yield return MoveFatherTo(_fatherCellCover, FatherPeekSpeed);
                    yield return new WaitForSeconds(Random.Range(0.65f, 1.1f));
                }
            }

            yield return new WaitForSeconds(0.12f);
            yield return FatherReturnsToCell();
            yield return FatherSupportsFromHall();
        }

        IEnumerator FatherSupportsFromHall()
        {
            var path = new NavMeshPath();
            var retry = new WaitForSeconds(0.45f);
            int shotsLeft = Random.Range(3, 5);
            while (shotsLeft > 0)
            {
                if (_stage != Stage.Fight || _boss.IsAlive || _bossGuard.IsAlive || _escort.IsAlive ||
                    !TryFindFatherCover(path, out var route, out var left, out var right))
                {
                    yield return retry;
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
                    if (!TryFindFatherShot(hide, left, right, out var peek))
                    {
                        yield return retry;
                        continue;
                    }

                    yield return MoveFatherInWorld(peek, FatherPeekSpeed, requireSafeRoute: true);
                    if (FatherAt(peek) && TryGetFatherTarget(peek + Vector3.up * 1.4f, out var enemy))
                    {
                        _father.AimForEncounter(FatherTargetPoint(enemy));
                        yield return new WaitForSeconds(0.28f);
                        if (FatherRouteIsSafe(peek, peek) && enemy.IsAlive)
                        {
                            Vector3 point = FatherTargetPoint(enemy);
                            _father.AimForEncounter(point);
                            Physics.SyncTransforms();
                            Vector3 muzzle = _fatherGun.StagedMuzzlePosition;
                            Vector3 chest = _father.transform.position + Vector3.up * 1.4f;
                            if (!Physics.Linecast(chest, muzzle, Physics.DefaultRaycastLayers,
                                QueryTriggerInteraction.Ignore) && FatherCanSee(muzzle, enemy))
                            {
                                _father.PlayStagedShot();
                                shotsLeft--;
                                yield return new WaitForSeconds(0.22f);
                            }
                        }
                    }

                    _father.LowerStagedWeapon();
                    yield return MoveFatherInWorld(hide, FatherPeekSpeed);
                    if (shotsLeft > 0)
                        yield return new WaitForSeconds(Random.Range(3.5f, 5.5f));
                }

                if (shotsLeft > 0)
                {
                    ReleaseFatherCover();
                    yield return FatherReturnsToCell(fromHallCover: true);
                }
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

        bool TryFindFatherShot(Vector3 hide, Vector3? left, Vector3? right, out Vector3 peek)
        {
            peek = default;
            return CheckEdge(left, out peek) || CheckEdge(right, out peek);

            bool CheckEdge(Vector3? edge, out Vector3 position)
            {
                position = edge.GetValueOrDefault();
                return edge.HasValue && FatherRouteIsSafe(hide, position) &&
                    TryGetFatherTarget(position + Vector3.up * 1.4f, out _);
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
            if (fromHallCover)
            {
                var path = new NavMeshPath();
                Vector3 door = transform.TransformPoint(_fatherCellPeek);
                if (!NavMesh.SamplePosition(_father.transform.position, out var start, 0.6f, NavMesh.AllAreas) ||
                    !NavMesh.SamplePosition(door, out var end, 0.6f, NavMesh.AllAreas) ||
                    !NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path) ||
                    path.status != NavMeshPathStatus.PathComplete)
                    yield break;
                foreach (var corner in path.corners)
                    yield return MoveFatherInWorld(corner, FatherRunSpeed);
            }
            else if (transform.InverseTransformPoint(_father.transform.position).x < _fatherCellPeek.x)
                yield return MoveFatherTo(_fatherCellPeek, FatherRunSpeed);
            yield return MoveFatherTo(_fatherCellCover, FatherRunSpeed);
        }

        IEnumerator MoveFatherTo(Vector3 localPosition, float speed) =>
            MoveFatherInWorld(transform.TransformPoint(localPosition), speed);

        IEnumerator MoveFatherInWorld(Vector3 destination, float speed, bool requireSafeRoute = false)
        {
            Transform actor = _father.transform;
            Vector3 direction = Vector3.ProjectOnPlane(destination - actor.position, Vector3.up);
            Quaternion facing = direction.sqrMagnitude > 0.001f ? Quaternion.LookRotation(direction) : actor.rotation;

            while ((actor.position - destination).sqrMagnitude > 0.0001f)
            {
                if (requireSafeRoute && !FatherRouteIsSafe(actor.position, destination))
                    yield break;
                float deltaTime = Time.deltaTime;
                actor.rotation = Quaternion.RotateTowards(actor.rotation, facing, 600f * deltaTime);
                actor.position = Vector3.MoveTowards(actor.position, destination, speed * deltaTime);
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
            }
        }

        void StartEarlyFight(Vector3 shotPosition)
        {
            StopAllCoroutines();
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
            _hud.ShowSubtitle("Босс: Убейте Александра! Огонь!", duration: 3f);
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
                if (enemy != null && enemy.IsAlive)
                    enemy.AimForEncounter(_openingTarget, smooth);
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
                    enemy.BeginEncounterCombat(_openingTarget, visibleHitChance, holdCover: !_fatherWasExecuted);
            }
        }

        void ExecutePlayer(HostileNpc shooter)
        {
            shooter.AimForEncounter(PlayerChest);
            Finish();
            PlayExecutionShot();
            _player.TakeDamage(PlayerHealth.MaxHealth);
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
            StopAllCoroutines();
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
                StopAllCoroutines();
        }

        void OnDestroy()
        {
            if (_pistol != null)
                _pistol.ShotFired -= OnPlayerShot;
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
