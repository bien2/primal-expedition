using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;
using WalaPaNameHehe.Multiplayer;

public class DinoAI : NetworkBehaviour
{
    public enum DinoState { Idle, Roam, Chase, Stunned, AlertRoar, WakeUp, Investigate, Attack, Flee }
    public DinoState currentState;

    private const float WakeUpFallbackSecondsWithoutAnimatorController = 3f;
    private const float FleeFadeOutSeconds = 3f;
    private const float FleeMaxSeconds = 20f;

    public enum AggressionType { Passive, Neutral, Plunderer, Hunter, Roamer, Apex }
    public AggressionType aggressionType;

    [Header("Perception")]
    public float detectionRadius = 15f;
    public float soundReactionRadius = 50f;
    public float viewAngle = 120f;
    [SerializeField] private bool requireLineOfSight = true;
    [SerializeField] private float eyeHeight = 1.2f;
    [SerializeField] private LayerMask lineOfSightBlockers = ~0;
    [SerializeField] private bool showLineOfSightGizmo = true;
    [SerializeField] private LayerMask playerLayers;
    [SerializeField] private bool canHearSounds = true;
    [SerializeField] private float defaultSoundMemorySeconds = 2.5f;

    [Header("Movement")]
    public float walkSpeed = 3.5f;
    public float runSpeed = 6f;
    public float roamRadius = 20f;
    public float turnSpeed = 180f;
    public float alertTurnSpeed = 120f;
    public float turnMoveThreshold = 0.15f;
    [Tooltip("Yaw offset applied to facing direction. Use if the model walks backwards because its forward axis is flipped.")]
    public float modelYawOffset = 0f;

    [Header("Pathing")]
    [Min(1)] public int roamDestinationAttempts = 6;
    [SerializeField] private bool requireCompleteRoamPath = true;

    [Header("Behavior")]
    public float chaseDuration = 10f;
    public float loseSightChaseTimeout = 7f;
    [Min(0f)] public float reactionCooldownSeconds = 5f;
    [Min(0.1f)] public float roamerFleeSeconds = 15f;
    public float neutralReactDelayMin = 2f;
    public float neutralReactDelayMax = 4f;
    [Min(0.1f)] public float hunterCueDelayMin = 40f;
    [Min(0.1f)] public float hunterCueDelayMax = 60f;
    [Min(1)] public int hunterCueCountMin = 5;
    [Min(1)] public int hunterCueCountMax = 5;
    [HideInInspector] public bool hunterForceHuntTest = false;
    [HideInInspector] public bool hunterForceHuntPlayCues = true;
    public float idleTime = 5f;

    [Header("Plunderer")]
    [HideInInspector] public float plundererSpawnIntervalMin = 60f;
    [HideInInspector] public float plundererSpawnIntervalMax = 90f;
    [HideInInspector] public float plundererActiveTimeMin = 30f;
    [HideInInspector] public float plundererActiveTimeMax = 45f;
    [Range(0f, 1f)] public float plundererBaseGrabChanceDay = 0.1f;
    [Range(0f, 1f)] public float plundererBaseGrabChanceNight = 0.2f;
    [Range(0f, 1f)] public float plundererHuntedBonus = 0.1f;
    [Range(0f, 1f)] public float plundererGroupedBonus = 0.05f;
    [Range(0f, 1f)] public float plundererChanceCapMax = 0.35f;
    [HideInInspector] public float plundererReturnArriveDistance = 2f;
    public float plundererFlightHeight = 6f;
    [HideInInspector] public Transform[] plundererWaypoints;
    public float plundererWaypointArriveDistance = 2f;
    [HideInInspector] public Transform plundererSpawnPoint;
    public float plundererCarryHoldSeconds = 30f;
    public float plundererDropSearchRadius = 9999f;
    public float plundererDropArriveDistance = 2f;
    [HideInInspector] public bool plundererPreloadHidden = false;
    [HideInInspector] public int plundererWaypointLoopsMin = 0;
    [HideInInspector] public int plundererWaypointLoopsMax = 0;

    [Header("Investigate")]
    public float investigateSearchDuration = 2.5f;
    public float investigateArriveDistance = 1.1f;
    public bool investigateLookAround = true;
    public float investigateLookAroundSpeed = 90f;

    [Header("Alert Roar")]
    [SerializeField] private bool useAlertRoarBeforeChase = true;
    [SerializeField] private string alertRoarAnimationStateName = "Roar";
    [SerializeField] private float alertRoarExitNormalizedTime = 0.98f;
    [SerializeField] private float alertRoarCooldownSeconds = 0f;
    [SerializeField] private float alertRoarFallbackSeconds = 4f;

    [Header("Debug")]
    public bool isIdle;

    [Header("Attack")]
    public bool enableAttackKill = true;
    public float attackKillRadius = 1.2f;
    public float attackCheckInterval = 0.1f;

    [Header("Animation")]
    [SerializeField] private bool driveAnimator = true;
    [SerializeField] private Animator animator;
    [SerializeField] private string animatorStateParam = "State";
    [SerializeField] private string attackTriggerParam = "AttackTrigger";
    [SerializeField] private bool useIdleAnimationExit = true;
    [SerializeField] private string idleAnimationStateName = "Idle";
    [SerializeField] private float idleAnimationExitNormalizedTime = 0.98f;
    [SerializeField] private bool useWakeUpAfterStun = true;
    [SerializeField] private string wakeUpAnimationStateName = "Wake_up";
    [SerializeField] private float wakeUpExitNormalizedTime = 0.98f;

    [Header("Visibility")]
    [SerializeField] private Renderer[] visualRenderers;

    [Header("Grounding")]
    [SerializeField] private bool snapToGround = true;
    [SerializeField] private LayerMask groundLayers = ~0;
    [SerializeField] private float groundRayStartHeight = 1.5f;
    [SerializeField] private float groundRayLength = 4f;
    [SerializeField] private float groundOffset = 0f;
    [SerializeField] private float groundSnapSpeed = 12f;
    [SerializeField] private bool showGroundingGizmo = true;
    [SerializeField] private Color groundingGizmoColor = new Color(0.1f, 0.8f, 0.2f, 0.8f);

    private NavMeshAgent agent;
    private NavMeshPath cachedRoamPath;
    private bool proxyAgentModeApplied;
    private Transform targetPlayer;
    private DinoAttackController attackController;
    private DinoBehaviorRuleTemplate aggressionBehavior;
    private AggressionType activeBehaviorType;
    private float idleTimer;
    private float chaseTimer;
    private Coroutine stunRoutine;
    private float chaseLostSightTimer;
    private float lastAlertRoarTime = -999f;
    private DinoState alertRoarNextState = DinoState.Chase;
    private bool alertRoarNeedsVisibleTarget = true;
    private float alertRoarStateStartTime = -999f;
    private bool alertRoarSeenState;
    private Vector3 lastHeardSoundPosition;
    private float lastHeardSoundTime = -999f;
    private bool isFollowingSound;
    private bool hasInvestigatePosition;
    private Vector3 investigatePosition;
    private float investigateSearchTimer;
    private float fleeUntilTime;
    private Vector3 fleeDestination;
    private bool hasFleeDestination;
    private bool fleeToDespawnPoint;
    private float wakeUpStateStartTime;
    private bool isDespawning;
    private Coroutine despawnRoutine;
    private Coroutine fadeOutRoutine;
    private bool warnedMissingNetworkObjectInSession;
    private Vector3 homePosition;
    private Vector3 lastLosOrigin;
    private Vector3 lastLosTarget;
    private Vector3 lastLosHitPoint;
    private bool hasLosSample;
    private bool lastLosBlocked;
    private float nextAttackCheckTime;
    private float reactionCooldownUntilTime;
    private DinoAudio dinoAudio;
    private const int OverlapBufferSize = 32;
    private const int RaycastBufferSize = 16;
    private Collider[] overlapBuffer;
    private RaycastHit[] raycastBuffer;
    private readonly HashSet<Transform> uniquePlayers = new HashSet<Transform>();
    private readonly List<Transform> nearbyPlayersBuffer = new List<Transform>(8);
    private readonly HashSet<Transform> nearbyPlayersSet = new HashSet<Transform>();
    private int animatorStateHash;
    private bool hasAnimatorStateHash;
    private bool warnedMissingAnimatorStateParam;
    private int attackTriggerHash;
    private bool hasAttackTriggerHash;
    private float lastAttackTriggerTime = -999f;
    private Vector3 lastProxyPosition;
    private bool hasProxyPositionSample;
    private float proxySmoothedSpeed;
    private DinoState lastAppliedAnimatorState = (DinoState)(-1);
    private bool? lastAppliedHunterHidden;
    private bool? lastAppliedPlundererHidden;
    private float attackVisualUntilTime = -1f;
    private readonly NetworkVariable<int> syncedState = new(
        (int)DinoState.Roam,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> syncedHunterHidden = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> syncedPlundererHidden = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    [Header("Proxy Animation")]
    [SerializeField] private float proxySpeedSmoothing = 8f;
    [SerializeField] private float proxyIdleSpeedThreshold = 0.12f;
    [SerializeField] private float proxyRunEnterSpeed = 4.8f;
    [SerializeField] private float proxyRunExitSpeed = 4.2f;

    public float ChaseTimer
    {
        get => chaseTimer;
        set => chaseTimer = value;
    }

    public float DetectionRadiusSqr => detectionRadius * detectionRadius;
    public LayerMask PlayerLayersMask => playerLayers;
    public NavMeshAgent Agent => agent;
    public Transform CurrentTarget => targetPlayer;
    public bool IsFollowingSound => isFollowingSound;
    public bool IsReactionOnCooldown => Time.time < reactionCooldownUntilTime;
    public Vector3 HomePosition => homePosition;
    public bool HasInvestigatePosition => hasInvestigatePosition;
    public Vector3 InvestigatePosition => investigatePosition;
    public bool IsHunterHidden => syncedHunterHidden != null && syncedHunterHidden.Value;
    public bool IsPlundererHidden => syncedPlundererHidden != null && syncedPlundererHidden.Value;
    public bool HasLineOfSightToTarget(Transform target)
    {
        if (target == null)
        {
            return false;
        }

        return HasLineOfSightTo(target);
    }
    public float InvestigateSearchTimer
    {
        get => investigateSearchTimer;
        set => investigateSearchTimer = value;
    }

    private void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }
        dinoAudio = GetComponent<DinoAudio>();
        attackController = GetComponent<DinoAttackController>();
        if (agent != null)
        {
            agent.updateRotation = false;
        }
        CacheAnimatorHashes();
        CacheVisualRenderers();
        lastProxyPosition = transform.position;
        hasProxyPositionSample = true;
        homePosition = transform.position;
        if (aggressionType == AggressionType.Plunderer && plundererSpawnPoint != null)
        {
            homePosition = plundererSpawnPoint.position;
            transform.position = homePosition;
        }
        EnsureAggressionBehavior();
        EnsurePlayerLayerMask();
        ApplyVisibility();
        ChangeState(DinoState.Roam);
    }

    private void OnEnable()
    {
        WorldSoundStimulus.Emitted += HandleWorldSound;
    }

    private void OnDisable()
    {
        WorldSoundStimulus.Emitted -= HandleWorldSound;
    }

    private void OnDestroy()
    {
        DinoBehaviorRuleSet.CleanupState(this);
    }

    private void Update()
    {
        ApplyVisibilityIfNeeded();

        if (!ShouldRunAiLogic())
        {
            ForceProxyAgentStop();
            if (!ApplyProxyAnimatorFromSyncedState())
            {
                UpdateProxyAnimatorFromMotion();
            }
            return;
        }

        EnsureAuthorityAgentActive();
        EnsureAggressionBehavior();

        switch (currentState)
        {
            case DinoState.Idle:
                aggressionBehavior?.HandleIdle(this);
                break;
            case DinoState.Roam:
                aggressionBehavior?.HandleRoam(this);
                break;
            case DinoState.Investigate:
                aggressionBehavior?.HandleInvestigate(this);
                break;
            case DinoState.Chase:
                aggressionBehavior?.HandleChase(this);
                break;
            case DinoState.Attack:
                aggressionBehavior?.HandleChase(this);
                break;
            case DinoState.Flee:
                HandleFlee();
                break;
            case DinoState.AlertRoar:
                HandleAlertRoar();
                break;
            case DinoState.WakeUp:
                HandleWakeUp();
                break;
            case DinoState.Stunned:
                HandleStunned();
                break;
        }

        TryKillPlayersInRadius();
        UpdateFacing();
        SnapToGround();
    }

    public void ChangeState(DinoState newState)
    {
        currentState = newState;

        switch (newState)
        {
            case DinoState.Idle:
                idleTimer = 0f;
                SetAgentStoppedSafe(true);
                SetMoveSpeed(walkSpeed);
                break;
            case DinoState.Roam:
                chaseTimer = 0f;
                SetAgentStoppedSafe(false);
                SetMoveSpeed(walkSpeed);
                break;
            case DinoState.Chase:
                chaseTimer = 0f;
                chaseLostSightTimer = 0f;
                SetAgentStoppedSafe(false);
                SetMoveSpeed(runSpeed);
                break;
            case DinoState.Attack:
                chaseTimer = 0f;
                chaseLostSightTimer = 0f;
                SetAgentStoppedSafe(false);
                SetMoveSpeed(runSpeed);
                break;
            case DinoState.Flee:
                chaseTimer = 0f;
                chaseLostSightTimer = 0f;
                targetPlayer = null;
                isFollowingSound = false;
                hasInvestigatePosition = false;
                investigateSearchTimer = 0f;
                hasFleeDestination = false;
                SetAgentStoppedSafe(false);
                SetMoveSpeed(runSpeed);
                break;
            case DinoState.AlertRoar:
                chaseLostSightTimer = 0f;
                alertRoarStateStartTime = Time.time;
                alertRoarSeenState = false;
                SetAgentStoppedSafe(true);
                ResetPathSafe();
                SetMoveSpeed(walkSpeed);
                break;
            case DinoState.WakeUp:
                wakeUpStateStartTime = Time.time;
                SetAgentStoppedSafe(true);
                ResetPathSafe();
                SetMoveSpeed(walkSpeed);
                break;
            case DinoState.Stunned:
                targetPlayer = null;
                hasInvestigatePosition = false;
                investigateSearchTimer = 0f;
                ResetPathSafe();
                SetAgentStoppedSafe(true);
                break;
            case DinoState.Investigate:
                chaseTimer = 0f;
                SetAgentStoppedSafe(false);
                SetMoveSpeed(walkSpeed);
                break;
        }

        if (newState == DinoState.Chase)
        {
            SetHunterHiddenMode(false);
        }

        SyncStateForNetwork(newState);
        ApplyAnimatorState();
    }

    public void ExecuteIdleCycle()
    {
        if (ShouldLeaveIdleByAnimation())
        {
            isIdle = false;
            ChangeState(DinoState.Roam);
            return;
        }

        if (useIdleAnimationExit && driveAnimator && animator != null && idleTime <= 0f)
        {
            isIdle = true;
            return;
        }

        idleTimer += Time.deltaTime;

        if (idleTimer < idleTime)
        {
            isIdle = true;
            return;
        }

        isIdle = false;
        ChangeState(DinoState.Roam);
    }

    private bool ShouldLeaveIdleByAnimation()
    {
        if (!useIdleAnimationExit || !driveAnimator || animator == null)
        {
            return false;
        }

        if (animator.layerCount <= 0)
        {
            return false;
        }

        if (animator.IsInTransition(0))
        {
            return false;
        }

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        if (!state.IsName(idleAnimationStateName))
        {
            return false;
        }

        float exitTime = Mathf.Clamp(idleAnimationExitNormalizedTime, 0.01f, 10f);
        return state.normalizedTime >= exitTime;
    }

    public void ExecuteRoamCycle(bool useGlobalRoam, bool skipIdle = false)
    {
        if (!CanUseAgent())
        {
            return;
        }

        agent.isStopped = false;
        SetMoveSpeed(walkSpeed);

        float arriveDistance = Mathf.Max(agent.stoppingDistance, 0.5f);
        if (agent.pathPending)
        {
            return;
        }

        if (agent.hasPath)
        {
            if (agent.remainingDistance > arriveDistance)
            {
                return;
            }

            agent.ResetPath();
            isFollowingSound = false;
            idleTimer = 0f;
            if (!skipIdle)
            {
                ChangeState(DinoState.Idle);
                return;
            }
        }

        int attempts = Mathf.Max(1, roamDestinationAttempts);
        bool setDestination = false;
        for (int i = 0; i < attempts; i++)
        {
            Vector3 destination;
            bool hasDestination = useGlobalRoam
                ? TryGetRandomGlobalNavMeshPoint(out destination)
                : TryGetLocalRoamDestination(roamRadius, out destination);

            if (!hasDestination)
            {
                continue;
            }

            if (TrySetRoamDestination(destination))
            {
                setDestination = true;
                break;
            }
        }

        if (!setDestination)
        {
            idleTimer = 0f;
            if (!skipIdle)
            {
                ChangeState(DinoState.Idle);
            }
            return;
        }
        isFollowingSound = false;
    }

    public bool TrySetRoamDestination(Vector3 destination)
    {
        if (!CanUseAgent())
        {
            return false;
        }

        if (requireCompleteRoamPath)
        {
            if (cachedRoamPath == null)
            {
                cachedRoamPath = new NavMeshPath();
            }

            if (!agent.CalculatePath(destination, cachedRoamPath) ||
                cachedRoamPath.status != NavMeshPathStatus.PathComplete)
            {
                return false;
            }
        }

        return agent.SetDestination(destination);
    }

    public bool TryFindClosestPlayer(float radius, out Transform closestPlayer, out int nearbyPlayerCount)
    {
        closestPlayer = null;
        nearbyPlayerCount = 0;

        if (radius <= 0f)
        {
            return false;
        }

        if (!TryGetPlayerMask(out int mask))
        {
            return false;
        }

        Transform self = transform;
        Vector3 selfPosition = self.position;
        Collider[] hitsBuffer = EnsureOverlapBuffer();
        int hitCount = Physics.OverlapSphereNonAlloc(
            selfPosition,
            radius,
            hitsBuffer,
            mask,
            QueryTriggerInteraction.Ignore);

        uniquePlayers.Clear();
        float closestSqrDistance = float.MaxValue;

        if (hitCount == hitsBuffer.Length)
        {
            Collider[] hits = Physics.OverlapSphere(
                selfPosition,
                radius,
                mask,
                QueryTriggerInteraction.Ignore);
            hitCount = hits.Length;
            for (int i = 0; i < hitCount; i++)
            {
                Collider hit = hits[i];
                if (hit == null)
                {
                    continue;
                }

                Transform candidate = hit.transform.root;
                if (candidate == null || candidate == self.root)
                {
                    continue;
                }

                WalaPaNameHehe.PlayerMovement candidateMovement = candidate.GetComponentInParent<WalaPaNameHehe.PlayerMovement>();
                if (candidateMovement == null || candidateMovement.IsDead)
                {
                    continue;
                }

                if (!uniquePlayers.Add(candidate))
                {
                    continue;
                }

                float sqrDistance = (candidate.position - selfPosition).sqrMagnitude;
                if (!IsInsideVisionCone(candidate))
                {
                    continue;
                }

                if (requireLineOfSight && !HasLineOfSightTo(candidate))
                {
                    continue;
                }

                if (sqrDistance < closestSqrDistance)
                {
                    closestSqrDistance = sqrDistance;
                    closestPlayer = candidate;
                }
            }
        }
        else
        {
            for (int i = 0; i < hitCount; i++)
            {
                Collider hit = hitsBuffer[i];
                if (hit == null)
                {
                    continue;
                }

                Transform candidate = hit.transform.root;
                if (candidate == null || candidate == self.root)
                {
                    continue;
                }

                WalaPaNameHehe.PlayerMovement candidateMovement = candidate.GetComponentInParent<WalaPaNameHehe.PlayerMovement>();
                if (candidateMovement == null || candidateMovement.IsDead)
                {
                    continue;
                }

                if (!uniquePlayers.Add(candidate))
                {
                    continue;
                }

                float sqrDistance = (candidate.position - selfPosition).sqrMagnitude;
                if (!IsInsideVisionCone(candidate))
                {
                    continue;
                }

                if (requireLineOfSight && !HasLineOfSightTo(candidate))
                {
                    continue;
                }

                if (sqrDistance < closestSqrDistance)
                {
                    closestSqrDistance = sqrDistance;
                    closestPlayer = candidate;
                }
            }
        }

        nearbyPlayerCount = uniquePlayers.Count;
        return closestPlayer != null;
    }

    private bool IsInsideVisionCone(Transform target)
    {
        if (target == null)
        {
            return false;
        }

        float clampedAngle = Mathf.Clamp(viewAngle, 1f, 360f);
        if (clampedAngle >= 359f)
        {
            return true;
        }

        Vector3 toTarget = target.position - transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude <= 0.0001f)
        {
            return true;
        }

        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.0001f)
        {
            forward = Vector3.forward;
        }

        float angleToTarget = Vector3.Angle(forward.normalized, toTarget.normalized);
        return angleToTarget <= (clampedAngle * 0.5f);
    }

    private bool HasLineOfSightTo(Transform target)
    {
        if (target == null)
        {
            return false;
        }

        Vector3 origin = transform.position + Vector3.up * Mathf.Max(0f, eyeHeight);
        Vector3 targetPos = target.position + Vector3.up * Mathf.Max(0f, eyeHeight * 0.85f);
        lastLosOrigin = origin;
        lastLosTarget = targetPos;
        hasLosSample = true;
        lastLosBlocked = false;
        Vector3 toTarget = targetPos - origin;
        float distance = toTarget.magnitude;
        if (distance <= 0.001f)
        {
            return true;
        }

        Vector3 direction = toTarget / distance;
        RaycastHit[] hitsBuffer = EnsureRaycastBuffer();
        int hitCount = Physics.RaycastNonAlloc(
            origin,
            direction,
            hitsBuffer,
            distance,
            lineOfSightBlockers,
            QueryTriggerInteraction.Ignore);

        if (hitCount == hitsBuffer.Length)
        {
            RaycastHit[] hits = Physics.RaycastAll(origin, direction, distance, lineOfSightBlockers, QueryTriggerInteraction.Ignore);
            hitCount = hits.Length;
            if (hitCount == 0)
            {
                return true;
            }

            int bestIndex = -1;
            float bestDistance = float.MaxValue;
            Transform bestRoot = null;
            for (int i = 0; i < hitCount; i++)
            {
                Transform hitRoot = hits[i].transform != null ? hits[i].transform.root : null;
                if (hitRoot == null || hitRoot == transform.root)
                {
                    continue;
                }

                float hitDistance = hits[i].distance;
                if (hitDistance < bestDistance)
                {
                    bestDistance = hitDistance;
                    bestIndex = i;
                    bestRoot = hitRoot;
                }
            }

            if (bestIndex == -1)
            {
                return true;
            }

            bool canSee = bestRoot == target.root;
            lastLosBlocked = !canSee;
            if (lastLosBlocked)
            {
                lastLosHitPoint = hits[bestIndex].point;
            }
            return canSee;
        }

        if (hitCount <= 0)
        {
            return true;
        }

        int closestIndex = -1;
        float closestDistance = float.MaxValue;
        Transform closestRoot = null;

        for (int i = 0; i < hitCount; i++)
        {
            Transform hitRoot = hitsBuffer[i].transform != null ? hitsBuffer[i].transform.root : null;
            if (hitRoot == null || hitRoot == transform.root)
            {
                continue;
            }

            float hitDistance = hitsBuffer[i].distance;
            if (hitDistance < closestDistance)
            {
                closestDistance = hitDistance;
                closestIndex = i;
                closestRoot = hitRoot;
            }
        }

        if (closestIndex == -1)
        {
            return true;
        }

        bool isVisible = closestRoot == target.root;
        lastLosBlocked = !isVisible;
        if (lastLosBlocked)
        {
            lastLosHitPoint = hitsBuffer[closestIndex].point;
        }
        return isVisible;
    }

    public bool TryConsumeRecentSound(out Vector3 position)
    {
        return TryConsumeRecentSound(defaultSoundMemorySeconds, out position);
    }

    public bool TryConsumeRecentSound(float maxAgeSeconds, out Vector3 position)
    {
        position = default;
        if (!canHearSounds)
        {
            return false;
        }

        float maxAge = Mathf.Max(0f, maxAgeSeconds);
        if (Time.time - lastHeardSoundTime > maxAge)
        {
            return false;
        }

        position = lastHeardSoundPosition;
        return true;
    }

    public void StartChase(Transform target)
    {
        StartChase(target, false);
    }

    public void StartChase(Transform target, bool alertBeforeChase)
    {
        if (target == null)
        {
            return;
        }

        isFollowingSound = false;
        targetPlayer = target;
        chaseLostSightTimer = 0f;
        DinoState chaseState = aggressionType == AggressionType.Plunderer ? DinoState.Attack : DinoState.Chase;

        if (alertBeforeChase && ShouldPlayAlertRoarBeforeChase())
        {
            BeginAlertRoar(chaseState, true);
            return;
        }

        ChangeState(chaseState);
    }

    public void StopChase()
    {
        isFollowingSound = false;
        targetPlayer = null;
        chaseLostSightTimer = 0f;
        ChangeState(DinoState.Roam);
    }

    public void StartInvestigate(Vector3 position)
    {
        isFollowingSound = false;
        targetPlayer = null;
        chaseLostSightTimer = 0f;
        investigatePosition = position;
        hasInvestigatePosition = true;
        investigateSearchTimer = 0f;
        ChangeState(DinoState.Investigate);
    }

    public void ClearInvestigate()
    {
        hasInvestigatePosition = false;
        investigateSearchTimer = 0f;
    }

    private bool ShouldPlayAlertRoarBeforeChase()
    {
        if (!useAlertRoarBeforeChase)
        {
            return false;
        }

        if (currentState == DinoState.Chase || currentState == DinoState.Attack || currentState == DinoState.AlertRoar)
        {
            return false;
        }

        float cooldown = Mathf.Max(0f, alertRoarCooldownSeconds);
        if (Time.time - lastAlertRoarTime < cooldown)
        {
            return false;
        }

        return true;
    }

    private void HandleAlertRoar()
    {
        SetAgentStoppedSafe(true);

        if (alertRoarNeedsVisibleTarget && (targetPlayer == null || !IsTargetVisibleForChase(targetPlayer)))
        {
            StopChase();
            return;
        }

        bool timedOut = (Time.time - alertRoarStateStartTime) >= Mathf.Max(0.25f, alertRoarFallbackSeconds);
        if (!timedOut && !IsAlertRoarFinished())
        {
            return;
        }

        lastAlertRoarTime = Time.time;
        if (alertRoarNextState == DinoState.Chase || alertRoarNextState == DinoState.Attack)
        {
            ChangeState(alertRoarNextState);
            if (targetPlayer != null)
            {
                SetDestinationSafe(targetPlayer.position);
            }
            return;
        }

        isFollowingSound = false;
        targetPlayer = null;
        chaseLostSightTimer = 0f;
        ChangeState(alertRoarNextState);
    }

    private void BeginAlertRoar(DinoState nextState, bool needsVisibleTarget)
    {
        alertRoarNextState = nextState;
        alertRoarNeedsVisibleTarget = needsVisibleTarget;
        ChangeState(DinoState.AlertRoar);
    }

    public bool TryStartAlertRoarToChase(Transform target)
    {
        if (target == null || !ShouldPlayAlertRoarBeforeChase())
        {
            return false;
        }

        isFollowingSound = false;
        targetPlayer = target;
        chaseLostSightTimer = 0f;
        BeginAlertRoar(DinoState.Chase, true);
        return true;
    }

    public void StartAlertRoarToRoam()
    {
        BeginAlertRoar(DinoState.Roam, false);
    }

    public bool TryStartAlertRoarToRoam()
    {
        if (!ShouldPlayAlertRoarBeforeChase())
        {
            return false;
        }

        BeginAlertRoar(DinoState.Roam, false);
        return true;
    }
    public void ForceAlertRoarToChase(Transform target)
    {
        if (target == null)
        {
            return;
        }

        isFollowingSound = false;
        targetPlayer = target;
        chaseLostSightTimer = 0f;
        BeginAlertRoar(DinoState.Chase, true);
    }

    private bool IsAlertRoarFinished()
    {
        if (!driveAnimator || animator == null)
        {
            return true;
        }

        if (animator.layerCount <= 0 || animator.IsInTransition(0))
        {
            return false;
        }

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        if (!state.IsName(alertRoarAnimationStateName))
        {
            return alertRoarSeenState;
        }

        float exitTime = Mathf.Clamp(alertRoarExitNormalizedTime, 0.01f, 10f);
        alertRoarSeenState = true;
        return state.normalizedTime >= exitTime;
    }

    private bool IsTargetVisible(Transform target)
    {
        if (target == null)
        {
            return false;
        }

        bool inSightRadius = (target.position - transform.position).sqrMagnitude <= DetectionRadiusSqr;
        if (!inSightRadius || !IsInsideVisionCone(target))
        {
            return false;
        }

        return !requireLineOfSight || HasLineOfSightTo(target);
    }

    private void TryKillPlayersInRadius()
    {
        if (!enableAttackKill)
        {
            return;
        }

        if (currentState != DinoState.Chase && currentState != DinoState.Attack)
        {
            return;
        }

        if (attackKillRadius <= 0f)
        {
            return;
        }

        if (Time.time < nextAttackCheckTime)
        {
            return;
        }

        nextAttackCheckTime = Time.time + Mathf.Max(0.02f, attackCheckInterval);

        if (!TryGetPlayerMask(out int mask))
        {
            return;
        }

        Vector3 selfPosition = transform.position;
        Collider[] hitsBuffer = EnsureOverlapBuffer();
        int hitCount = Physics.OverlapSphereNonAlloc(
            selfPosition,
            attackKillRadius,
            hitsBuffer,
            mask,
            QueryTriggerInteraction.Ignore);

        if (hitCount == hitsBuffer.Length)
        {
            Collider[] hits = Physics.OverlapSphere(
                selfPosition,
                attackKillRadius,
                mask,
                QueryTriggerInteraction.Ignore);
            KillPlayersFromHits(hits, hits.Length);
            return;
        }

        KillPlayersFromHits(hitsBuffer, hitCount);
    }

    private void KillPlayersFromHits(Collider[] hits, int hitCount)
    {
        Transform target = targetPlayer;
        if (target == null)
        {
            return;
        }

        Transform targetRoot = target.root;
        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = hits[i];
            if (hit == null)
            {
                continue;
            }

            Transform hitRoot = hit.transform != null ? hit.transform.root : null;
            if (hitRoot == null || hitRoot != targetRoot)
            {
                continue;
            }

            WalaPaNameHehe.PlayerMovement player = hit.GetComponentInParent<WalaPaNameHehe.PlayerMovement>();
            if (player == null || player.IsDead)
            {
                continue;
            }

            if (attackController != null)
            {
                if (!attackController.TryAttackTarget(player))
                {
                    continue;
                }
            }
            else
            {
                if (!player.ServerKill())
                {
                    continue;
                }
            }

            if (aggressionType == AggressionType.Apex)
            {
                StartReactionCooldown();
                StopChase();
            }

            return;
        }
    }

    public bool IsTargetVisibleForChase(Transform target)
    {
        if (target == null)
        {
            return false;
        }

        // Once chase has started, do not require detection radius.
        // Chase only drops when target stays out of sight long enough.
        if (requireLineOfSight)
        {
            return HasLineOfSightTo(target);
        }

        // If LOS is disabled, use forward vision only (still no radius limit).
        return IsInsideVisionCone(target);
    }

    public void MoveToSound(Vector3 soundPosition, float speed)
    {
        if (!CanUseAgent())
        {
            return;
        }

        isFollowingSound = true;
        SetMoveSpeed(speed);
        SetAgentStoppedSafe(false);
        Vector3 destination = soundPosition;
        // Sound sources (drone/extraction) can be above ground or off the navmesh.
        // Use a wider projection radius so the AI can still find a reachable ground point.
        float sampleRadius = Mathf.Max(8f, soundReactionRadius * 0.5f);
        if (NavMesh.SamplePosition(soundPosition, out NavMeshHit hit, sampleRadius, agent.areaMask))
        {
            destination = hit.position;
        }

        SetDestinationSafe(destination);
    }

    public void StopFollowingSound()
    {
        if (!isFollowingSound)
        {
            return;
        }

        isFollowingSound = false;
        ResetPathSafe();
    }

    public bool HasTarget()
    {
        return targetPlayer != null;
    }

    public bool TryAttackTarget(WalaPaNameHehe.PlayerMovement target)
    {
        if (attackController == null)
        {
            return false;
        }

        return attackController.TryAttackTarget(target);
    }

    public void ClearTargetIfMatches(Transform targetRoot)
    {
        if (targetRoot == null)
        {
            return;
        }

        if (!ShouldRunAiLogic())
        {
            return;
        }

        if (targetPlayer == null || targetPlayer.root != targetRoot)
        {
            return;
        }

        targetPlayer = null;
        isFollowingSound = false;
        chaseLostSightTimer = 0f;
        hasInvestigatePosition = false;
        investigateSearchTimer = 0f;

        if (currentState == DinoState.Chase || currentState == DinoState.Attack || currentState == DinoState.AlertRoar || currentState == DinoState.Investigate)
        {
            ChangeState(DinoState.Roam);
        }
    }

    public float GetTargetSqrDistance()
    {
        if (targetPlayer == null)
        {
            return float.MaxValue;
        }

        return (targetPlayer.position - transform.position).sqrMagnitude;
    }

    public bool TryGetLocalRoamDestination(float radius, out Vector3 destination)
    {
        Vector3 randomDirection = Random.insideUnitSphere * Mathf.Max(0f, radius);
        randomDirection += transform.position;

        if (NavMesh.SamplePosition(randomDirection, out NavMeshHit localHit, Mathf.Max(0.1f, radius), NavMesh.AllAreas))
        {
            destination = localHit.position;
            return true;
        }

        destination = transform.position;
        return false;
    }

    public bool TryGetLocalRoamDestinationFromCenter(Vector3 center, float radius, out Vector3 destination)
    {
        Vector3 randomDirection = Random.insideUnitSphere * Mathf.Max(0f, radius);
        randomDirection += center;

        if (NavMesh.SamplePosition(randomDirection, out NavMeshHit localHit, Mathf.Max(0.1f, radius), NavMesh.AllAreas))
        {
            destination = localHit.position;
            return true;
        }

        destination = center;
        return false;
    }

    public bool TryGetRandomGlobalNavMeshPoint(out Vector3 point)
    {
        NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
        if (tri.vertices == null || tri.vertices.Length == 0)
        {
            point = transform.position;
            return false;
        }

        int index = Random.Range(0, tri.vertices.Length);
        Vector3 candidate = tri.vertices[index];
        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas))
        {
            point = hit.position;
            return true;
        }

        point = transform.position;
        return false;
    }

    public void Stun(float duration)
    {
        Stun(duration, false);
    }

    public void Stun(float duration, bool fleeAfterWakeUp)
    {
        if (!ShouldRunAiLogic())
        {
            return;
        }

        if (aggressionType == AggressionType.Apex)
        {
            return;
        }

        if (stunRoutine != null)
        {
            StopCoroutine(stunRoutine);
        }

        stunRoutine = StartCoroutine(StunRoutine(duration, fleeAfterWakeUp));
    }

    private IEnumerator StunRoutine(float duration, bool fleeAfterWakeUp)
    {
        isFollowingSound = false;
        ChangeState(DinoState.Stunned);
        yield return new WaitForSeconds(Mathf.Max(0f, duration));

        if (useWakeUpAfterStun)
        {
            ChangeState(DinoState.WakeUp);
            while (currentState == DinoState.WakeUp && !IsWakeUpFinished())
            {
                yield return null;
            }
        }

        stunRoutine = null;

        if (aggressionType == AggressionType.Passive || aggressionType == AggressionType.Neutral)
        {
            StartFleeToDespawnPoint(false);
            yield break;
        }

        if (aggressionType == AggressionType.Roamer)
        {
            StartFleeToDespawnPoint(true);
            yield break;
        }

        if (fleeAfterWakeUp)
        {
            BeginFlee();
            yield break;
        }

        ChangeState(DinoState.Roam);
    }

    public void StartReactionCooldown()
    {
        float duration = Mathf.Max(0f, reactionCooldownSeconds);
        reactionCooldownUntilTime = Time.time + duration;
    }

    private void HandleStunned()
    {
        if (CanUseAgent() && !agent.isStopped)
        {
            agent.isStopped = true;
        }
    }

    private void HandleWakeUp()
    {
        if (CanUseAgent() && !agent.isStopped)
        {
            agent.isStopped = true;
        }
    }

    private void BeginFlee()
    {
        fleeUntilTime = Time.time + FleeMaxSeconds;
        hasFleeDestination = false;
        fleeToDespawnPoint = false;
        ChangeState(DinoState.Flee);
    }

    public void StartFleeToDespawnPoint(bool playAlertRoar)
    {
        if (!ShouldRunAiLogic())
        {
            return;
        }

        if (aggressionType == AggressionType.Apex)
        {
            return;
        }

        fleeUntilTime = Time.time + FleeMaxSeconds;
        hasFleeDestination = false;
        fleeToDespawnPoint = DinoDespawnPoint.Point != null;

        if (playAlertRoar)
        {
            alertRoarNextState = DinoState.Flee;
            alertRoarNeedsVisibleTarget = false;
            ChangeState(DinoState.AlertRoar);
            return;
        }

        ChangeState(DinoState.Flee);
    }

    private void HandleFlee()
    {
        if (isDespawning)
        {
            return;
        }

        if (fleeToDespawnPoint && DinoDespawnPoint.Point != null)
        {
            if (Time.time >= fleeUntilTime)
            {
                BeginFadeOutDespawn();
                return;
            }

            if (!CanUseAgent())
            {
                return;
            }

            agent.isStopped = false;
            agent.speed = Mathf.Max(0f, runSpeed);

            if (hasFleeDestination)
            {
                if (!agent.pathPending)
                {
                    float arriveDistance = Mathf.Max(agent.stoppingDistance, 0.5f);
                    if (agent.hasPath && agent.remainingDistance <= arriveDistance)
                    {
                        BeginFadeOutDespawn();
                        return;
                    }

                    Vector3 delta = transform.position - fleeDestination;
                    delta.y = 0f;
                    if (!agent.hasPath && delta.sqrMagnitude <= (arriveDistance * arriveDistance))
                    {
                        BeginFadeOutDespawn();
                        return;
                    }
                }
                return;
            }

            fleeDestination = DinoDespawnPoint.Point.position;
            hasFleeDestination = true;
            SetDestinationSafe(fleeDestination);
            return;
        }

        if (Time.time >= fleeUntilTime)
        {
            BeginFadeOutDespawn();
            return;
        }

        if (!CanUseAgent())
        {
            return;
        }

        agent.isStopped = false;
        agent.speed = Mathf.Max(0f, runSpeed);

        if (hasFleeDestination)
        {
            if (!agent.pathPending)
            {
                float arriveDistance = Mathf.Max(agent.stoppingDistance, 0.5f);
                if (agent.hasPath && agent.remainingDistance <= arriveDistance)
                {
                    BeginFadeOutDespawn();
                    return;
                }

                Vector3 delta = transform.position - fleeDestination;
                delta.y = 0f;
                if (!agent.hasPath && delta.sqrMagnitude <= (arriveDistance * arriveDistance))
                {
                    BeginFadeOutDespawn();
                    return;
                }
            }
            return;
        }

        if (TryPickFleeDestination(out Vector3 destination))
        {
            hasFleeDestination = true;
            fleeDestination = destination;
            SetDestinationSafe(destination);
        }
    }

    private bool TryPickFleeDestination(out Vector3 destination)
    {
        destination = transform.position;

        // Pick a navmesh point that maximizes distance from nearby players.
        // This is evaluated once when entering Flee (keeps behavior predictable and cheap).
        const float playerSearchRadius = 60f;
        const float sampleRadius = 35f;
        const int samples = 14;

        List<Transform> players = GetNearbyPlayersNonAlloc(playerSearchRadius);
        if (players == null || players.Count == 0)
        {
            return TryGetRandomGlobalNavMeshPoint(out destination);
        }

        // Always include a "directly away from closest player" candidate.
        Vector3 origin = transform.position;
        origin.y = 0f;
        Transform closest = null;
        float closestSqr = float.MaxValue;
        for (int i = 0; i < players.Count; i++)
        {
            Transform p = players[i];
            if (p == null)
            {
                continue;
            }

            float d = (p.position - transform.position).sqrMagnitude;
            if (d < closestSqr)
            {
                closestSqr = d;
                closest = p;
            }
        }

        bool foundAny = false;
        float bestScore = -1f;
        Vector3 bestPoint = transform.position;

        if (closest != null)
        {
            Vector3 away = transform.position - closest.position;
            away.y = 0f;
            if (away.sqrMagnitude <= 0.0001f)
            {
                away = transform.forward;
                away.y = 0f;
            }

            away = away.sqrMagnitude > 0.0001f ? away.normalized : Vector3.forward;
            Vector3 candidate = transform.position + away * sampleRadius;
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, Mathf.Max(2f, sampleRadius * 0.5f), NavMesh.AllAreas))
            {
                float score = ScorePointAgainstPlayers(hit.position, players);
                bestScore = score;
                bestPoint = hit.position;
                foundAny = true;
            }
        }

        for (int i = 0; i < samples; i++)
        {
            if (!TryGetRandomNavmeshPointNear(transform.position, sampleRadius, out Vector3 point))
            {
                continue;
            }

            float score = ScorePointAgainstPlayers(point, players);
            if (!foundAny || score > bestScore)
            {
                bestScore = score;
                bestPoint = point;
                foundAny = true;
            }
        }

        if (!foundAny)
        {
            return TryGetRandomGlobalNavMeshPoint(out destination);
        }

        destination = bestPoint;
        return true;
    }

    private static bool TryGetRandomNavmeshPointNear(Vector3 center, float radius, out Vector3 point)
    {
        point = center;
        Vector3 randomDirection = Random.insideUnitSphere * Mathf.Max(0f, radius);
        randomDirection += center;

        if (NavMesh.SamplePosition(randomDirection, out NavMeshHit hit, Mathf.Max(1f, radius), NavMesh.AllAreas))
        {
            point = hit.position;
            return true;
        }

        return false;
    }

    private static float ScorePointAgainstPlayers(Vector3 point, List<Transform> players)
    {
        float minSqr = float.MaxValue;
        for (int i = 0; i < players.Count; i++)
        {
            Transform p = players[i];
            if (p == null)
            {
                continue;
            }

            float d = (p.position - point).sqrMagnitude;
            if (d < minSqr)
            {
                minSqr = d;
            }
        }

        if (minSqr == float.MaxValue)
        {
            return 0f;
        }

        return minSqr;
    }

    private void DespawnSelf()
    {
        BeginFadeOutDespawn();
    }

    private void BeginFadeOutDespawn()
    {
        if (isDespawning)
        {
            return;
        }

        isDespawning = true;

        StartFadeOutLocal();
        if (IsNetworkActive() && IsServer)
        {
            StartFadeOutClientRpc();
        }

        if (despawnRoutine != null)
        {
            StopCoroutine(despawnRoutine);
        }

        despawnRoutine = StartCoroutine(DespawnAfterFadeRoutine());
    }

    private IEnumerator DespawnAfterFadeRoutine()
    {
        yield return new WaitForSeconds(FleeFadeOutSeconds);
        DespawnSelfImmediate();
    }

    private void DespawnSelfImmediate()
    {
        if (IsNetworkActive())
        {
            if (!IsServer)
            {
                return;
            }

            NetworkObject networkObject = GetComponent<NetworkObject>();
            if (networkObject != null && networkObject.IsSpawned)
            {
                networkObject.Despawn(true);
                return;
            }
        }

        Destroy(gameObject);
    }

    [ClientRpc]
    private void StartFadeOutClientRpc()
    {
        if (IsServer)
        {
            return;
        }

        StartFadeOutLocal();
    }

    private struct FadeMaterialColor
    {
        public bool hasBaseColor;
        public Color baseColor;
        public bool hasColor;
        public Color color;
    }

    private struct FadeRendererData
    {
        public Renderer renderer;
        public FadeMaterialColor[] materials;
    }

    private FadeRendererData[] fadeRenderers = System.Array.Empty<FadeRendererData>();
    private MaterialPropertyBlock fadeBlock;

    private void StartFadeOutLocal()
    {
        if (fadeOutRoutine != null)
        {
            return;
        }

        CacheFadeRenderers();
        fadeOutRoutine = StartCoroutine(FadeOutRoutine());
    }

    private void CacheFadeRenderers()
    {
        Renderer[] renderers = (visualRenderers != null && visualRenderers.Length > 0)
            ? visualRenderers
            : GetComponentsInChildren<Renderer>(true);

        if (renderers == null)
        {
            fadeRenderers = System.Array.Empty<FadeRendererData>();
            return;
        }

        fadeRenderers = new FadeRendererData[renderers.Length];
        for (int r = 0; r < renderers.Length; r++)
        {
            Renderer renderer = renderers[r];
            FadeRendererData data = new FadeRendererData { renderer = renderer, materials = System.Array.Empty<FadeMaterialColor>() };
            if (renderer == null)
            {
                fadeRenderers[r] = data;
                continue;
            }

            PrepareRendererMaterialsForFade(renderer);
            Material[] mats = renderer.materials;
            if (mats == null || mats.Length == 0)
            {
                fadeRenderers[r] = data;
                continue;
            }

            FadeMaterialColor[] materialColors = new FadeMaterialColor[mats.Length];
            for (int i = 0; i < mats.Length; i++)
            {
                Material m = mats[i];
                FadeMaterialColor c = new FadeMaterialColor();
                if (m != null)
                {
                    if (m.HasProperty("_BaseColor"))
                    {
                        c.hasBaseColor = true;
                        c.baseColor = m.GetColor("_BaseColor");
                    }
                    if (m.HasProperty("_Color"))
                    {
                        c.hasColor = true;
                        c.color = m.GetColor("_Color");
                    }
                }
                materialColors[i] = c;
            }

            data.materials = materialColors;
            fadeRenderers[r] = data;
        }

        if (fadeBlock == null)
        {
            fadeBlock = new MaterialPropertyBlock();
        }
    }

    private static void PrepareRendererMaterialsForFade(Renderer renderer)
    {
        // URP/Lit materials set to Opaque will ignore alpha, so we force them into a transparent blend mode
        // on the per-renderer material instances. This avoids modifying shared assets.
        if (renderer == null)
        {
            return;
        }

        Material[] mats = renderer.materials;
        if (mats == null || mats.Length == 0)
        {
            return;
        }

        for (int i = 0; i < mats.Length; i++)
        {
            Material mat = mats[i];
            if (mat == null)
            {
                continue;
            }

            TrySetMaterialToTransparent(mat);
        }
    }

    private static void TrySetMaterialToTransparent(Material mat)
    {
        // URP Lit
        if (mat.HasProperty("_Surface"))
        {
            mat.SetFloat("_Surface", 1f); // 0 = Opaque, 1 = Transparent
            if (mat.HasProperty("_Blend"))
            {
                mat.SetFloat("_Blend", 0f); // 0 = Alpha (URP)
            }

            if (mat.HasProperty("_ZWrite"))
            {
                mat.SetFloat("_ZWrite", 0f);
            }

            if (mat.HasProperty("_SrcBlend"))
            {
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            }

            if (mat.HasProperty("_DstBlend"))
            {
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            }

            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return;
        }

        // Built-in Standard shader
        if (mat.HasProperty("_Mode"))
        {
            // 0=Opaque,1=Cutout,2=Fade,3=Transparent
            mat.SetFloat("_Mode", 2f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
    }

    private IEnumerator FadeOutRoutine()
    {
        float start = Time.time;
        float duration = Mathf.Max(0.01f, FleeFadeOutSeconds);

        while (true)
        {
            float t = Mathf.Clamp01((Time.time - start) / duration);
            float alpha = 1f - t;
            ApplyFadeAlpha(alpha);
            if (t >= 1f)
            {
                break;
            }

            yield return null;
        }

        ApplyFadeAlpha(0f);
    }

    private void ApplyFadeAlpha(float alpha01)
    {
        if (fadeRenderers == null || fadeRenderers.Length == 0)
        {
            return;
        }

        float a = Mathf.Clamp01(alpha01);

        for (int r = 0; r < fadeRenderers.Length; r++)
        {
            FadeRendererData data = fadeRenderers[r];
            if (data.renderer == null || data.materials == null || data.materials.Length == 0)
            {
                continue;
            }

            for (int i = 0; i < data.materials.Length; i++)
            {
                FadeMaterialColor colors = data.materials[i];
                if (!colors.hasBaseColor && !colors.hasColor)
                {
                    continue;
                }

                fadeBlock.Clear();
                if (colors.hasBaseColor)
                {
                    Color c = colors.baseColor;
                    c.a *= a;
                    fadeBlock.SetColor("_BaseColor", c);
                }
                if (colors.hasColor)
                {
                    Color c = colors.color;
                    c.a *= a;
                    fadeBlock.SetColor("_Color", c);
                }

                data.renderer.SetPropertyBlock(fadeBlock, i);
            }
        }
    }

    private bool IsWakeUpFinished()
    {
        if (!driveAnimator || animator == null)
        {
            return true;
        }

        if (animator.runtimeAnimatorController == null)
        {
            return (Time.time - wakeUpStateStartTime) >= WakeUpFallbackSecondsWithoutAnimatorController;
        }

        if (animator.layerCount <= 0)
        {
            return (Time.time - wakeUpStateStartTime) >= WakeUpFallbackSecondsWithoutAnimatorController;
        }

        if (animator.IsInTransition(0))
        {
            return false;
        }

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        if (!state.IsName(wakeUpAnimationStateName))
        {
            return true;
        }

        float exitTime = Mathf.Clamp(wakeUpExitNormalizedTime, 0.01f, 10f);
        return state.normalizedTime >= exitTime;
    }

    private void HandleWorldSound(Vector3 position, float radius)
    {
        if (!ShouldRunAiLogic())
        {
            return;
        }

        if (!canHearSounds || currentState == DinoState.Stunned || currentState == DinoState.Investigate || currentState == DinoState.Chase)
        {
            return;
        }

        float hearingRadius = Mathf.Max(0f, soundReactionRadius);
        if (hearingRadius <= 0f)
        {
            return;
        }

        float sqrDistance = (position - transform.position).sqrMagnitude;
        if (sqrDistance > hearingRadius * hearingRadius)
        {
            return;
        }

        lastHeardSoundPosition = position;
        lastHeardSoundTime = Time.time;
    }

    private bool TryGetPlayerMask(out int mask)
    {
        mask = playerLayers.value;
        if (mask != 0)
        {
            return true;
        }

        mask = LayerMask.GetMask("Player");
        if (mask == 0)
        {
            return false;
        }

        playerLayers = mask;
        return true;
    }

    internal List<Transform> GetNearbyPlayersNonAlloc(float radius)
    {
        nearbyPlayersBuffer.Clear();
        nearbyPlayersSet.Clear();

        if (radius <= 0f)
        {
            return nearbyPlayersBuffer;
        }

        if (!TryGetPlayerMask(out int mask))
        {
            return nearbyPlayersBuffer;
        }

        Transform selfRoot = transform.root;
        Vector3 selfPosition = transform.position;
        Collider[] hitsBuffer = EnsureOverlapBuffer();
        int hitCount = Physics.OverlapSphereNonAlloc(
            selfPosition,
            radius,
            hitsBuffer,
            mask,
            QueryTriggerInteraction.Ignore);

        if (hitCount == hitsBuffer.Length)
        {
            Collider[] hits = Physics.OverlapSphere(
                selfPosition,
                radius,
                mask,
                QueryTriggerInteraction.Ignore);
            hitCount = hits.Length;
            for (int i = 0; i < hitCount; i++)
            {
                Collider hit = hits[i];
                if (hit == null)
                {
                    continue;
                }

                Transform root = hit.transform.root;
                if (root == null || root == selfRoot || !nearbyPlayersSet.Add(root))
                {
                    continue;
                }

                WalaPaNameHehe.PlayerMovement playerMovement = root.GetComponentInParent<WalaPaNameHehe.PlayerMovement>();
                if (playerMovement == null || playerMovement.IsDead)
                {
                    continue;
                }

                nearbyPlayersBuffer.Add(root);
            }

            return nearbyPlayersBuffer;
        }

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = hitsBuffer[i];
            if (hit == null)
            {
                continue;
            }

            Transform root = hit.transform.root;
            if (root == null || root == selfRoot || !nearbyPlayersSet.Add(root))
            {
                continue;
            }

            WalaPaNameHehe.PlayerMovement playerMovement = root.GetComponentInParent<WalaPaNameHehe.PlayerMovement>();
            if (playerMovement == null || playerMovement.IsDead)
            {
                continue;
            }

            nearbyPlayersBuffer.Add(root);
        }

        return nearbyPlayersBuffer;
    }

    private void EnsurePlayerLayerMask()
    {
        if (playerLayers.value == 0)
        {
            int mask = LayerMask.GetMask("Player");
            if (mask != 0)
            {
                playerLayers = mask;
            }
        }
    }

    private void EnsureAggressionBehavior()
    {
        if (aggressionBehavior != null && activeBehaviorType == aggressionType)
        {
            return;
        }

        aggressionBehavior = DinoBehaviorRuleSet.Create(aggressionType);
        activeBehaviorType = aggressionType;
    }

    private void SetMoveSpeed(float speed)
    {
        if (CanUseAgent())
        {
            agent.speed = Mathf.Max(0f, speed);
        }
    }

    private bool CanUseAgent()
    {
        return agent != null && agent.enabled && agent.isActiveAndEnabled && agent.isOnNavMesh;
    }

    private bool SetDestinationSafe(Vector3 destination)
    {
        if (!CanUseAgent())
        {
            return false;
        }

        return agent.SetDestination(destination);
    }

    private void ResetPathSafe()
    {
        if (CanUseAgent() && agent.hasPath)
        {
            agent.ResetPath();
        }
    }

    private void SetAgentStoppedSafe(bool stopped)
    {
        if (CanUseAgent())
        {
            agent.isStopped = stopped;
        }
    }

    private Collider[] EnsureOverlapBuffer()
    {
        if (overlapBuffer == null || overlapBuffer.Length < OverlapBufferSize)
        {
            overlapBuffer = new Collider[OverlapBufferSize];
        }

        return overlapBuffer;
    }

    private RaycastHit[] EnsureRaycastBuffer()
    {
        if (raycastBuffer == null || raycastBuffer.Length < RaycastBufferSize)
        {
            raycastBuffer = new RaycastHit[RaycastBufferSize];
        }

        return raycastBuffer;
    }

    private void CacheAnimatorHashes()
    {
        hasAnimatorStateHash = false;
        if (!string.IsNullOrWhiteSpace(animatorStateParam))
        {
            animatorStateHash = Animator.StringToHash(animatorStateParam);
            if (animator != null)
            {
                AnimatorControllerParameter[] parameters = animator.parameters;
                for (int i = 0; i < parameters.Length; i++)
                {
                    AnimatorControllerParameter p = parameters[i];
                    if (p != null && p.type == AnimatorControllerParameterType.Int && p.name == animatorStateParam)
                    {
                        hasAnimatorStateHash = true;
                        break;
                    }
                }
            }
        }
        if (!string.IsNullOrWhiteSpace(attackTriggerParam))
        {
            attackTriggerHash = Animator.StringToHash(attackTriggerParam);
            hasAttackTriggerHash = true;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            return;
        }

        if (IsServer)
        {
            syncedState.Value = (int)currentState;
            ApplyVisibility();
            return;
        }

        if (IsClient)
        {
            ApplyProxyAnimatorFromSyncedState();
            ApplyVisibility();
        }
    }

    private void ApplyAnimatorState()
    {
        if (!driveAnimator || animator == null)
        {
            return;
        }

        ApplyAnimatorState(GetVisualState());
    }

    private void ApplyAnimatorState(DinoState visualState)
    {
        if (!driveAnimator || animator == null)
        {
            return;
        }

        if (!hasAnimatorStateHash)
        {
#if UNITY_EDITOR
            if (!warnedMissingAnimatorStateParam)
            {
                warnedMissingAnimatorStateParam = true;
                Debug.LogWarning(
                    $"DinoAI: Animator is missing int parameter '{animatorStateParam}'. State-driven animation sync is disabled for '{name}'.",
                    this);
            }
#endif
            return;
        }

        if (lastAppliedAnimatorState == visualState)
        {
            return;
        }

        animator.SetInteger(animatorStateHash, (int)visualState);
        lastAppliedAnimatorState = visualState;
    }

    private void SyncStateForNetwork(DinoState state)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || !IsServer || !IsSpawned)
        {
            return;
        }

        syncedState.Value = (int)state;
    }

    public void SetHunterHiddenMode(bool hidden)
    {
        if (aggressionType != AggressionType.Hunter)
        {
            hidden = false;
        }

        if (syncedHunterHidden == null)
        {
            return;
        }

        NetworkManager nm = NetworkManager.Singleton;
        if (nm != null && nm.IsListening)
        {
            if (!IsServer || !IsSpawned)
            {
                return;
            }

            syncedHunterHidden.Value = hidden;
        }
        else
        {
            syncedHunterHidden.Value = hidden;
        }

        ApplyVisibility();
    }

    public void SetPlundererHidden(bool hidden)
    {
        if (aggressionType != AggressionType.Plunderer)
        {
            hidden = false;
        }

        if (syncedPlundererHidden == null)
        {
            return;
        }

        NetworkManager nm = NetworkManager.Singleton;
        if (nm != null && nm.IsListening)
        {
            if (!IsServer || !IsSpawned)
            {
                return;
            }

            syncedPlundererHidden.Value = hidden;
        }
        else
        {
            syncedPlundererHidden.Value = hidden;
        }

        ApplyVisibility();
    }

    private bool ApplyProxyAnimatorFromSyncedState()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || !IsClient || !IsSpawned)
        {
            return false;
        }

        if (IsAttackVisualActive())
        {
            ApplyAnimatorState(DinoState.Attack);
            return true;
        }

        int rawState = syncedState.Value;
        if (!System.Enum.IsDefined(typeof(DinoState), rawState))
        {
            return false;
        }

        ApplyAnimatorState((DinoState)rawState);
        return true;
    }

    private void UpdateProxyAnimatorFromMotion()
    {
        if (!driveAnimator || animator == null)
        {
            return;
        }

        if (IsAttackVisualActive())
        {
            ApplyAnimatorState(DinoState.Attack);
            return;
        }

        if (!hasProxyPositionSample)
        {
            lastProxyPosition = transform.position;
            hasProxyPositionSample = true;
            return;
        }

        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        float speed = (transform.position - lastProxyPosition).magnitude / dt;
        lastProxyPosition = transform.position;
        float lerpT = 1f - Mathf.Exp(-Mathf.Max(0.01f, proxySpeedSmoothing) * dt);
        proxySmoothedSpeed = Mathf.Lerp(proxySmoothedSpeed, speed, lerpT);

        DinoState visualState;
        if (proxySmoothedSpeed <= Mathf.Max(0f, proxyIdleSpeedThreshold))
        {
            visualState = DinoState.Idle;
        }
        else
        {
            bool inRun = lastAppliedAnimatorState == DinoState.Chase;
            if (inRun)
            {
                visualState = proxySmoothedSpeed >= proxyRunExitSpeed ? DinoState.Chase : DinoState.Roam;
            }
            else
            {
                visualState = proxySmoothedSpeed >= proxyRunEnterSpeed ? DinoState.Chase : DinoState.Roam;
            }
        }

        ApplyAnimatorState(visualState);
    }

    public void TriggerAttackVisual(float durationSeconds)
    {
        float duration = Mathf.Max(0.05f, durationSeconds);
        SetAttackVisual(duration);

        if (IsNetworkActive() && IsServer)
        {
            TriggerAttackVisualClientRpc(duration);
        }
    }

    private void SetAttackVisual(float durationSeconds)
    {
        attackVisualUntilTime = Time.time + durationSeconds;
        ApplyAnimatorState(DinoState.Attack);
        TriggerAttackAnimator();
    }

    private void TriggerAttackAnimator()
    {
        if (!driveAnimator || animator == null || !hasAttackTriggerHash)
        {
            return;
        }

        float now = Time.time;
        if (now - lastAttackTriggerTime < 0.05f)
        {
            return;
        }

        animator.ResetTrigger(attackTriggerHash);
        animator.SetTrigger(attackTriggerHash);
        lastAttackTriggerTime = now;
    }

    private bool IsAttackVisualActive()
    {
        return attackVisualUntilTime > 0f && Time.time < attackVisualUntilTime;
    }

    private DinoState GetVisualState()
    {
        if (IsAttackVisualActive())
        {
            return DinoState.Attack;
        }

        return currentState;
    }

    [ClientRpc]
    private void TriggerAttackVisualClientRpc(float durationSeconds)
    {
        SetAttackVisual(durationSeconds);
    }

    private bool IsNetworkActive()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
    }

    public void PlayAlertRoarAudio()
    {
        bool networkActive = IsNetworkActive();
        if (networkActive && !IsServer)
        {
            return;
        }

        PlayAlertRoarAudioLocal();

        if (networkActive && IsServer)
        {
            PlayAlertRoarAudioClientRpc();
        }
    }

    [ClientRpc]
    private void PlayAlertRoarAudioClientRpc()
    {
        if (IsServer)
        {
            return;
        }

        PlayAlertRoarAudioLocal();
    }

    private void PlayAlertRoarAudioLocal()
    {
        if (dinoAudio == null)
        {
            dinoAudio = GetComponent<DinoAudio>();
        }

        dinoAudio?.PlayAlertRoar();
    }

    private bool ShouldRunAiLogic()
    {
        bool canRun = CoopGuard.ShouldRunServerGameplay(this);
        if (canRun)
        {
            return true;
        }

        NetworkManager nm = NetworkManager.Singleton;
        if (nm != null && nm.IsListening && !warnedMissingNetworkObjectInSession)
        {
            NetworkObject no = GetComponent<NetworkObject>();
            if (no == null)
            {
                warnedMissingNetworkObjectInSession = true;
                Debug.LogWarning($"DinoAI '{name}' has no NetworkObject in active multiplayer session. AI will only run on server.");
            }
        }

        return false;
    }

    private void ForceProxyAgentStop()
    {
        if (agent == null)
        {
            return;
        }

        if (agent.enabled)
        {
            ResetPathSafe();
            SetAgentStoppedSafe(true);
            agent.enabled = false;
        }
        proxyAgentModeApplied = true;
    }

    private void EnsureAuthorityAgentActive()
    {
        if (agent == null)
        {
            return;
        }

        if (proxyAgentModeApplied && !agent.enabled)
        {
            agent.enabled = true;
            proxyAgentModeApplied = false;
        }

        if (CanUseAgent())
        {
            bool shouldMove = ShouldMoveWithAgent(currentState);
            if (shouldMove)
            {
                if (agent.isStopped && !agent.pathPending)
                {
                    agent.isStopped = false;
                }
            }
            else if (!agent.isStopped)
            {
                agent.isStopped = true;
            }
        }

        agent.updateRotation = false;
    }

    private static bool ShouldMoveWithAgent(DinoState state)
    {
        switch (state)
        {
            case DinoState.Roam:
            case DinoState.Chase:
            case DinoState.Attack:
            case DinoState.Investigate:
            case DinoState.Flee:
                return true;
            default:
                return false;
        }
    }


    private void UpdateFacing()
    {
        if (!ShouldRunAiLogic() || agent == null)
        {
            return;
        }

        float moveThreshold = Mathf.Max(0.01f, turnMoveThreshold);
        float activeTurnSpeed = currentState == DinoState.AlertRoar
            ? Mathf.Max(0f, alertTurnSpeed)
            : Mathf.Max(0f, turnSpeed);

        Vector3 desiredForward = agent.desiredVelocity;
        desiredForward.y = 0f;

        if (desiredForward.sqrMagnitude <= moveThreshold * moveThreshold)
        {
            if (!agent.hasPath)
            {
                return;
            }

            Vector3 toSteeringTarget = agent.steeringTarget - transform.position;
            toSteeringTarget.y = 0f;
            if (toSteeringTarget.sqrMagnitude <= moveThreshold * moveThreshold)
            {
                return;
            }

            desiredForward = toSteeringTarget;
        }

        Quaternion targetRotation = Quaternion.LookRotation(desiredForward.normalized, Vector3.up);
        if (Mathf.Abs(modelYawOffset) > 0.01f)
        {
            targetRotation = targetRotation * Quaternion.Euler(0f, modelYawOffset, 0f);
        }
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            activeTurnSpeed * Time.deltaTime);
    }

    private void SnapToGround()
    {
        if (!snapToGround || aggressionType == AggressionType.Plunderer)
        {
            return;
        }

        Vector3 origin = transform.position + Vector3.up * Mathf.Max(0f, groundRayStartHeight);
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, groundRayLength, groundLayers, QueryTriggerInteraction.Ignore))
        {
            Vector3 position = transform.position;
            float targetY = hit.point.y + groundOffset;
            float newY = Mathf.MoveTowards(position.y, targetY, Mathf.Max(0.01f, groundSnapSpeed) * Time.deltaTime);
            if (!Mathf.Approximately(position.y, newY))
            {
                transform.position = new Vector3(position.x, newY, position.z);
            }
        }
    }

    private void OnDrawGizmos()
    {
        bool usesPerception = aggressionType != AggressionType.Passive && aggressionType != AggressionType.Plunderer;
        bool usesSoundRadius = canHearSounds && (aggressionType == AggressionType.Apex || aggressionType == AggressionType.Hunter || aggressionType == AggressionType.Roamer);
        bool usesLocalRoamRadius = aggressionType != AggressionType.Apex && aggressionType != AggressionType.Hunter;

        if (!usesPerception && !usesSoundRadius && !usesLocalRoamRadius)
        {
            return;
        }

        if (usesPerception)
        {
            Gizmos.color = new Color(1f, 0f, 0f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, detectionRadius);
        }

        if (usesLocalRoamRadius)
        {
            Gizmos.color = new Color(0f, 1f, 0f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, roamRadius);
        }

        if (usesSoundRadius && soundReactionRadius > 0f)
        {
            Gizmos.color = new Color(1f, 0.92f, 0.16f, 0.25f);
            Gizmos.DrawWireSphere(transform.position, soundReactionRadius);
        }

        if (usesPerception && viewAngle < 360f)
        {
            DrawViewConeGizmo(new Color(0.2f, 0.8f, 1f, 0.5f), detectionRadius);
        }
    }

    private void OnDrawGizmosSelected()
    {
        bool usesPerception = aggressionType != AggressionType.Passive && aggressionType != AggressionType.Plunderer;
        bool usesSoundRadius = canHearSounds && (aggressionType == AggressionType.Apex || aggressionType == AggressionType.Hunter || aggressionType == AggressionType.Roamer);
        bool usesLocalRoamRadius = aggressionType != AggressionType.Apex && aggressionType != AggressionType.Hunter;
        if (!usesPerception && !usesSoundRadius && !usesLocalRoamRadius)
        {
            DrawGroundingGizmo();
            return;
        }

        if (usesPerception)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, detectionRadius);
        }

        if (usesLocalRoamRadius)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, roamRadius);
        }


        if (usesSoundRadius && soundReactionRadius > 0f)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, soundReactionRadius);
        }

        if (usesPerception && viewAngle < 360f)
        {
            DrawViewConeGizmo(new Color(0.2f, 0.8f, 1f, 0.9f), detectionRadius);
        }

        if (enableAttackKill && attackKillRadius > 0f)
        {
            Gizmos.color = new Color(1f, 0.35f, 0.35f, 0.7f);
            Gizmos.DrawWireSphere(transform.position, attackKillRadius);
        }

        if (usesPerception && showLineOfSightGizmo && requireLineOfSight && hasLosSample)
        {
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.9f);
            Gizmos.DrawSphere(lastLosOrigin, 0.07f);

            if (lastLosBlocked)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(lastLosOrigin, lastLosHitPoint);
                Gizmos.DrawSphere(lastLosHitPoint, 0.08f);
                Gizmos.color = new Color(1f, 0.5f, 0f, 0.8f);
                Gizmos.DrawLine(lastLosHitPoint, lastLosTarget);
            }
            else
            {
                Gizmos.color = Color.green;
                Gizmos.DrawLine(lastLosOrigin, lastLosTarget);
                Gizmos.DrawSphere(lastLosTarget, 0.06f);
            }
        }

        DrawGroundingGizmo();
    }

    private void DrawGroundingGizmo()
    {
        if (!showGroundingGizmo || !snapToGround)
        {
            return;
        }

        Vector3 origin = transform.position + Vector3.up * Mathf.Max(0f, groundRayStartHeight);
        Vector3 end = origin + Vector3.down * Mathf.Max(0.01f, groundRayLength);
        Gizmos.color = groundingGizmoColor;
        Gizmos.DrawLine(origin, end);

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, groundRayLength, groundLayers, QueryTriggerInteraction.Ignore))
        {
            float targetY = hit.point.y + groundOffset;
            Vector3 target = new Vector3(transform.position.x, targetY, transform.position.z);
            Gizmos.DrawSphere(target, 0.07f);
        }
    }

    private void DrawViewConeGizmo(Color color, float range)
    {
        float clampedRange = Mathf.Max(0f, range);
        float clampedAngle = Mathf.Clamp(viewAngle, 1f, 360f);
        if (clampedRange <= 0f || clampedAngle >= 359f)
        {
            return;
        }

        Vector3 origin = transform.position;
        Vector3 left = Quaternion.Euler(0f, -clampedAngle * 0.5f, 0f) * transform.forward;
        Vector3 right = Quaternion.Euler(0f, clampedAngle * 0.5f, 0f) * transform.forward;

        Gizmos.color = color;
        Gizmos.DrawLine(origin, origin + left.normalized * clampedRange);
        Gizmos.DrawLine(origin, origin + right.normalized * clampedRange);
    }

    private void CacheVisualRenderers()
    {
        if (visualRenderers != null && visualRenderers.Length > 0)
        {
            return;
        }

        visualRenderers = GetComponentsInChildren<Renderer>(true);
    }

    private void ApplyVisibility()
    {
        CacheVisualRenderers();

        bool hunterHidden = aggressionType == AggressionType.Hunter && syncedHunterHidden != null && syncedHunterHidden.Value;
        bool plundererHidden = aggressionType == AggressionType.Plunderer && syncedPlundererHidden != null && syncedPlundererHidden.Value;
        bool visible = !(hunterHidden || plundererHidden);
        SetRenderersEnabled(visible);

        lastAppliedHunterHidden = hunterHidden;
        lastAppliedPlundererHidden = plundererHidden;
    }

    private void ApplyVisibilityIfNeeded()
    {
        if (syncedHunterHidden == null || syncedPlundererHidden == null)
        {
            return;
        }

        bool hunterHidden = aggressionType == AggressionType.Hunter && syncedHunterHidden.Value;
        bool plundererHidden = aggressionType == AggressionType.Plunderer && syncedPlundererHidden.Value;
        if (lastAppliedHunterHidden.HasValue && lastAppliedHunterHidden.Value == hunterHidden &&
            lastAppliedPlundererHidden.HasValue && lastAppliedPlundererHidden.Value == plundererHidden)
        {
            return;
        }

        ApplyVisibility();
    }

    private void SetRenderersEnabled(bool enabled)
    {
        if (visualRenderers == null)
        {
            return;
        }

        for (int i = 0; i < visualRenderers.Length; i++)
        {
            Renderer renderer = visualRenderers[i];
            if (renderer == null)
            {
                continue;
            }

            renderer.enabled = enabled;
        }
    }
}




