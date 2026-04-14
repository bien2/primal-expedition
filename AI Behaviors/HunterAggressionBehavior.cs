using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;
using WalaPaNameHehe;
using WalaPaNameHehe.Multiplayer;
using System.Collections.Generic;

public class HunterAggressionBehavior : DinoBehaviorRuleTemplate
{
    private static bool huntOccurredThisSession = false;
    private static int huntOwnerInstanceId = 0;
    private static readonly HashSet<int> despawnedHunters = new HashSet<int>();
    private static readonly List<Transform> EmptyPlayers = new List<Transform>(0);
    private const float HunterChaseDelayMin = 60f;
    private const float HunterChaseDelayMax = 85f;
    private const float HunterIsolationChaseDelay = 5f;
    private const float HunterTargetCueDelayMin = 1.5f;
    private const float HunterTargetCueDelayMax = 4f;
    private const float HunterTargetCueIntervalMin = 20f;
    private const float HunterTargetCueIntervalMax = 40f;
    private const float HunterCueStartDelayMin = 30f;
    private const float HunterCueStartDelayMax = 40f;
    private const int HunterCueCountMin = 3;
    private const int HunterCueCountMax = 5;
    private const int HunterMinimumCueCountBeforeChase = 3;
    private const int HunterCueCountMaxFixed = 5;
    private const float HunterThirdCueCommitChance = 0.4f;
    private const float HunterFourthCueCommitChance = 0.5f;
    private const float RepathDistanceThreshold = 3f;
    private const float FleeProbeDistance = 14f;
    private const float FakeHuntRecoverySeconds = 4f;
    private const int RecoverySampleCount = 10;

    private readonly Dictionary<int, Vector3> lastChaseDestinationByAi = new Dictionary<int, Vector3>();
    private readonly Dictionary<int, Transform> pendingTargetByAi = new Dictionary<int, Transform>();
    private readonly Dictionary<int, float> targetAcquiredTimeByAi = new Dictionary<int, float>();
    private readonly Dictionary<int, float> chaseReadyTimeByAi = new Dictionary<int, float>();
    private readonly Dictionary<int, float> targetCueDelayByAi = new Dictionary<int, float>();
    private readonly Dictionary<int, float> nextTargetCueTimeByAi = new Dictionary<int, float>();
    private readonly Dictionary<int, int> targetCueCountByAi = new Dictionary<int, int>();
    private readonly Dictionary<int, float> cueStartTimeByAi = new Dictionary<int, float>();
    private readonly Dictionary<int, int> cueMaxByAi = new Dictionary<int, int>();
    private readonly HashSet<int> roarPlayedByAi = new HashSet<int>();
    private readonly Dictionary<int, bool> encounterWillHuntByAi = new Dictionary<int, bool>();
    private readonly Dictionary<int, bool> committedToChaseByAi = new Dictionary<int, bool>();
    private readonly Dictionary<int, float> fleeUntilByAi = new Dictionary<int, float>();
    private readonly Dictionary<int, float> fakeHuntRecoveryUntilByAi = new Dictionary<int, float>();
    private readonly Dictionary<int, Vector3> fakeHuntRecoveryDestinationByAi = new Dictionary<int, Vector3>();
    private static readonly Dictionary<int, Vector3> returnToSpawnByAi = new Dictionary<int, Vector3>();

    public static void ResetSessionState()
    {
        huntOccurredThisSession = false;
        huntOwnerInstanceId = 0;
        despawnedHunters.Clear();
        returnToSpawnByAi.Clear();
    }

    public static void NotifyHunterKill(DinoAI ai)
    {
        if (ai == null || !ai.IsServer)
        {
            return;
        }

        if (ai.hunterSpawnPoint == null)
        {
            DespawnHunter(ai);
            return;
        }

        ai.StopChase();
        ai.SetHunterHiddenMode(false);
        ai.ChangeState(DinoAI.DinoState.Roam);
        returnToSpawnByAi[ai.GetInstanceID()] = ai.hunterSpawnPoint.position;
    }

    public void HandleIdle(DinoAI ai)
    {
        if (TryHandleReturnToSpawn(ai))
        {
            return;
        }

        if (HandleNonOwnerAfterHuntStart(ai))
        {
            return;
        }

        if (TryHandleFakeHuntRecovery(ai))
        {
            return;
        }

        if (TryHandlePendingTarget(ai))
        {
            return;
        }

        if (TryDetectPlayer(ai))
        {
            return;
        }

        HideHunterIdle(ai);
    }

    public void HandleRoam(DinoAI ai)
    {
        if (TryHandleReturnToSpawn(ai))
        {
            return;
        }

        if (HandleNonOwnerAfterHuntStart(ai))
        {
            return;
        }

        if (TryHandleFakeHuntRecovery(ai))
        {
            return;
        }

        if (TryHandlePendingTarget(ai))
        {
            return;
        }

        if (TryDetectPlayer(ai))
        {
            return;
        }

        HideHunterIdle(ai);
    }

    public void HandleInvestigate(DinoAI ai)
    {
        if (TryHandleReturnToSpawn(ai))
        {
            return;
        }

        if (HandleNonOwnerAfterHuntStart(ai))
        {
            return;
        }

        ai.ClearInvestigate();
        ai.ChangeState(DinoAI.DinoState.Roam);
    }

    public void HandleChase(DinoAI ai)
    {
        if (TryHandleReturnToSpawn(ai))
        {
            return;
        }

        if (HandleNonOwnerAfterHuntStart(ai))
        {
            return;
        }

        if (TryHandleFlee(ai))
        {
            return;
        }

        if (TryHandlePendingTarget(ai))
        {
            return;
        }

        Transform target = ai.CurrentTarget;
        if (target == null)
        {
            ClearCachedDestination(ai);
            ai.SetHunterHiddenMode(false);
            ai.StopChase();
            return;
        }

        ai.SetHunterHiddenMode(false);

        if (ai.Agent == null)
        {
            return;
        }

        ai.Agent.speed = Mathf.Max(0f, ai.runSpeed);
        ai.Agent.isStopped = false;
        Vector3 chaseDestination = target.position;
        Vector3 lastChaseDestination = GetLastChaseDestination(ai);
        bool shouldRepath =
            !ai.Agent.hasPath ||
            (lastChaseDestination - chaseDestination).sqrMagnitude >= (RepathDistanceThreshold * RepathDistanceThreshold);

        if (shouldRepath)
        {
            SetLastChaseDestination(ai, chaseDestination);
            ai.Agent.SetDestination(chaseDestination);
        }
    }

    private bool TryDetectPlayer(DinoAI ai)
    {
        if (ai == null)
        {
            return false;
        }

        if (ShouldFleeBecauseHuntStarted(ai))
        {
            return false;
        }

        if (!CanStartHunt(ai))
        {
            return false;
        }

        int key = ai.GetInstanceID();
        if (fleeUntilByAi.ContainsKey(key) || IsInFakeHuntRecovery(ai))
        {
            return false;
        }

        HunterMeterManager manager = HunterMeterManager.Instance;
        if (manager == null || !manager.ShouldHunterHuntThisRun)
        {
            return false;
        }

        if (!manager.TryGetHuntTarget(out Transform target))
        {
            return false;
        }

        if (!IsTargetValid(target))
        {
            ClearCachedDestination(ai);
            return false;
        }

        if (!encounterWillHuntByAi.TryGetValue(key, out bool willHunt))
        {
            willHunt = DecideWillHunt(ai);
            encounterWillHuntByAi[key] = willHunt;

            if (willHunt)
            {
                MarkHuntStarted(ai);
                pendingTargetByAi[key] = target;
                targetAcquiredTimeByAi[key] = Time.time;
                chaseReadyTimeByAi.Remove(key);
                targetCueDelayByAi[key] = GetRandomCueDelay();
                nextTargetCueTimeByAi.Remove(key);
                targetCueCountByAi[key] = 0;
                roarPlayedByAi.Remove(key);
                fleeUntilByAi.Remove(key);
                committedToChaseByAi[key] = false;
            }
            else
            {
                pendingTargetByAi.Remove(key);
                targetAcquiredTimeByAi.Remove(key);
                chaseReadyTimeByAi.Remove(key);
                targetCueDelayByAi.Remove(key);
                nextTargetCueTimeByAi.Remove(key);
                targetCueCountByAi.Remove(key);
                cueStartTimeByAi.Remove(key);
                cueMaxByAi.Remove(key);
                roarPlayedByAi.Remove(key);
                committedToChaseByAi.Remove(key);
                fleeUntilByAi[key] = Time.time + Mathf.Max(0.25f, ai.hunterFleeSeconds);
            }
        }

        ai.SetHunterHiddenMode(false);
        ai.StartChase(target, false);
        return true;
    }

    private static bool IsTargetValid(Transform target)
    {
        if (target == null)
        {
            return false;
        }

        WalaPaNameHehe.PlayerMovement playerMovement = target.GetComponentInParent<WalaPaNameHehe.PlayerMovement>();
        return playerMovement != null && !playerMovement.IsDead && target.gameObject.activeInHierarchy;
    }

    private static bool IsTargetReadyForChase(Transform target)
    {
        if (target == null)
        {
            return false;
        }

        WalaPaNameHehe.PlayerMovement playerMovement = target.GetComponentInParent<WalaPaNameHehe.PlayerMovement>();
        return playerMovement != null
            && !playerMovement.IsDead
            && playerMovement.IsIsolated
            && playerMovement.IsolatedDuration >= HunterIsolationChaseDelay;
    }

    private bool TryHandleFlee(DinoAI ai)
    {
        if (ai == null)
        {
            return false;
        }

        int key = ai.GetInstanceID();
        if (!fleeUntilByAi.TryGetValue(key, out float fleeUntilTime))
        {
            return false;
        }

        Transform target = ai.CurrentTarget;
        if (!IsTargetValid(target))
        {
            ClearCachedDestination(ai);
            ai.StopChase();
            return true;
        }

        ai.SetHunterHiddenMode(false);

        if (Time.time >= fleeUntilTime)
        {
            StartFakeHuntRecovery(ai, target);
            return true;
        }

        if (ai.Agent == null)
        {
            return true;
        }

        ai.Agent.speed = Mathf.Max(0f, ai.runSpeed);
        ai.Agent.isStopped = false;

        Vector3 fleeDestination = GetFleeDestination(ai, target);
        Vector3 lastChaseDestination = GetLastChaseDestination(ai);
        bool shouldRepath =
            !ai.Agent.hasPath ||
            (lastChaseDestination - fleeDestination).sqrMagnitude >= (RepathDistanceThreshold * RepathDistanceThreshold);

        if (shouldRepath)
        {
            SetLastChaseDestination(ai, fleeDestination);
            ai.Agent.SetDestination(fleeDestination);
        }

        return true;
    }

    private bool TryHandleFakeHuntRecovery(DinoAI ai)
    {
        if (ai == null)
        {
            return false;
        }

        int key = ai.GetInstanceID();
        if (!fakeHuntRecoveryUntilByAi.TryGetValue(key, out float recoveryUntil))
        {
            return false;
        }

        if (Time.time >= recoveryUntil)
        {
            if (ShouldDespawnAfterRecovery(ai))
            {
                fakeHuntRecoveryUntilByAi.Remove(key);
                fakeHuntRecoveryDestinationByAi.Remove(key);
                QueueReturnToSpawn(ai);
                return true;
            }

            fakeHuntRecoveryUntilByAi.Remove(key);
            fakeHuntRecoveryDestinationByAi.Remove(key);
            return false;
        }

        if (ai.Agent == null)
        {
            return true;
        }

        ai.SetHunterHiddenMode(false);
        ai.Agent.isStopped = false;
        ai.Agent.speed = Mathf.Max(0f, ai.walkSpeed);

        if (fakeHuntRecoveryDestinationByAi.TryGetValue(key, out Vector3 recoveryDestination))
        {
            float arriveDistance = Mathf.Max(ai.Agent.stoppingDistance, 1f);
            bool needsDestination = !ai.Agent.hasPath || (ai.Agent.destination - recoveryDestination).sqrMagnitude > 1f;
            if (needsDestination)
            {
                ai.Agent.SetDestination(recoveryDestination);
            }
            else if (!ai.Agent.pathPending && ai.Agent.remainingDistance <= arriveDistance)
            {
                ai.Agent.ResetPath();
            }
        }

        return true;
    }

    private void StartFakeHuntRecovery(DinoAI ai, Transform target)
    {
        if (ai == null)
        {
            return;
        }

        int key = ai.GetInstanceID();
        Vector3 recoveryDestination = GetRecoveryDestination(ai, target);

        lastChaseDestinationByAi.Remove(key);
        pendingTargetByAi.Remove(key);
        targetAcquiredTimeByAi.Remove(key);
        chaseReadyTimeByAi.Remove(key);
        targetCueDelayByAi.Remove(key);
        nextTargetCueTimeByAi.Remove(key);
        targetCueCountByAi.Remove(key);
        cueStartTimeByAi.Remove(key);
        cueMaxByAi.Remove(key);
        roarPlayedByAi.Remove(key);
        encounterWillHuntByAi.Remove(key);
        fleeUntilByAi.Remove(key);

        fakeHuntRecoveryUntilByAi[key] = Time.time + Mathf.Max(FakeHuntRecoverySeconds, ai.hunterFleeSeconds);
        fakeHuntRecoveryDestinationByAi[key] = recoveryDestination;

        ai.StopChase();

        if (ai.Agent != null)
        {
            ai.Agent.isStopped = false;
            ai.Agent.speed = Mathf.Max(0f, ai.walkSpeed);
            ai.Agent.SetDestination(recoveryDestination);
        }
    }

    private static Vector3 GetFleeDestination(DinoAI ai, Transform target)
    {
        if (ai == null || target == null)
        {
            return ai != null ? ai.transform.position : Vector3.zero;
        }

        Vector3 away = ai.transform.position - target.position;
        away.y = 0f;
        if (away.sqrMagnitude <= 0.0001f)
        {
            away = ai.transform.forward;
        }

        if (away.sqrMagnitude <= 0.0001f)
        {
            away = Vector3.forward;
        }

        away.Normalize();
        return FindBestDestinationAwayFromPlayers(ai, target, away, FleeProbeDistance);
    }

    private static Vector3 GetRecoveryDestination(DinoAI ai, Transform target)
    {
        if (ai == null)
        {
            return Vector3.zero;
        }

        Vector3 away = target != null ? ai.transform.position - target.position : ai.transform.forward;
        away.y = 0f;
        if (away.sqrMagnitude <= 0.0001f)
        {
            away = ai.transform.forward;
        }

        if (away.sqrMagnitude <= 0.0001f)
        {
            away = Vector3.forward;
        }

        away.Normalize();
        return FindBestDestinationAwayFromPlayers(ai, target, away, FleeProbeDistance * 1.5f);
    }

    private static Vector3 FindBestDestinationAwayFromPlayers(DinoAI ai, Transform primaryTarget, Vector3 away, float distance)
    {
        List<Transform> players = GetNearbyPlayers(ai, Mathf.Max(ai.detectionRadius * 2f, distance * 2f));
        Vector3 bestPoint = ai.transform.position + away * distance;
        float bestScore = float.NegativeInfinity;

        for (int i = 0; i < RecoverySampleCount; i++)
        {
            float angle = i * (360f / RecoverySampleCount);
            Vector3 candidateDirection = Quaternion.Euler(0f, angle, 0f) * away;
            Vector3 desired = ai.transform.position + candidateDirection * distance;
            if (!TrySampleNavMesh(ai, desired, distance, out Vector3 candidate))
            {
                continue;
            }

            float score = ScoreDestinationAwayFromPlayers(candidate, players, ai.transform.position, primaryTarget);
            if (score > bestScore)
            {
                bestScore = score;
                bestPoint = candidate;
            }
        }

        return bestPoint;
    }

    private static float ScoreDestinationAwayFromPlayers(Vector3 candidate, List<Transform> players, Vector3 hunterPosition, Transform primaryTarget)
    {
        float nearestPlayerDistance = float.MaxValue;
        for (int i = 0; i < players.Count; i++)
        {
            Transform player = players[i];
            if (player == null)
            {
                continue;
            }

            float distance = Vector3.Distance(candidate, player.position);
            if (distance < nearestPlayerDistance)
            {
                nearestPlayerDistance = distance;
            }
        }

        if (nearestPlayerDistance == float.MaxValue)
        {
            nearestPlayerDistance = Vector3.Distance(candidate, hunterPosition);
        }

        float score = nearestPlayerDistance;
        if (primaryTarget != null)
        {
            score += Vector3.Distance(candidate, primaryTarget.position);
        }

        return score;
    }

    private static List<Transform> GetNearbyPlayers(DinoAI ai, float radius)
    {
        if (ai == null)
        {
            return EmptyPlayers;
        }

        return ai.GetNearbyPlayersNonAlloc(radius);
    }

    private static bool TrySampleNavMesh(DinoAI ai, Vector3 desired, float sampleRadius, out Vector3 point)
    {
        point = desired;
        if (ai != null && ai.Agent != null && ai.Agent.isOnNavMesh)
        {
            int areaMask = ai.Agent.areaMask != 0 ? ai.Agent.areaMask : NavMesh.AllAreas;
            if (NavMesh.SamplePosition(desired, out NavMeshHit hit, sampleRadius, areaMask))
            {
                point = hit.position;
                return true;
            }
        }

        return false;
    }

    private bool IsInFakeHuntRecovery(DinoAI ai)
    {
        return ai != null
            && fakeHuntRecoveryUntilByAi.TryGetValue(ai.GetInstanceID(), out float recoveryUntil)
            && Time.time < recoveryUntil;
    }

    private static bool DecideWillHunt(DinoAI ai)
    {
        HunterMeterManager manager = HunterMeterManager.Instance;
        return manager != null && manager.ShouldHunterHuntThisRun;
    }

    private void TryPlayTargetCue(DinoAI ai, Transform target, bool forcePlay = false)
    {
        if (ai == null || target == null)
        {
            return;
        }

        int key = ai.GetInstanceID();
        if (!targetAcquiredTimeByAi.TryGetValue(key, out float targetAcquiredTime))
        {
            targetAcquiredTime = Time.time;
            targetAcquiredTimeByAi[key] = targetAcquiredTime;
        }

        if (!targetCueDelayByAi.TryGetValue(key, out float cueDelay))
        {
            cueDelay = GetRandomCueDelay();
            targetCueDelayByAi[key] = cueDelay;
        }

        float earliestCueTime = targetAcquiredTime + cueDelay;
        if (cueStartTimeByAi.TryGetValue(key, out float cueStartTime))
        {
            earliestCueTime = Mathf.Max(earliestCueTime, cueStartTime);
        }

        if (!forcePlay && Time.time < earliestCueTime)
        {
            return;
        }

        float nextAllowedTime = 0f;
        nextTargetCueTimeByAi.TryGetValue(key, out nextAllowedTime);

        if (!forcePlay && Time.time < nextAllowedTime)
        {
            return;
        }

        WalaPaNameHehe.PlayerMovement playerMovement = target.GetComponentInParent<WalaPaNameHehe.PlayerMovement>();
        if (playerMovement == null)
        {
            return;
        }

        if (committedToChaseByAi.TryGetValue(key, out bool committed) && committed)
        {
            return;
        }

        int cueCount = 0;
        targetCueCountByAi.TryGetValue(key, out cueCount);
        int cueMax = HunterCueCountMaxFixed;
        cueMaxByAi.TryGetValue(key, out cueMax);
        if (cueCount >= cueMax)
        {
            return;
        }

        playerMovement.TriggerHunterTargetSound();
        nextTargetCueTimeByAi[key] = Time.time + GetRandomCueInterval();
        cueCount += 1;
        targetCueCountByAi[key] = cueCount;

        if (cueCount >= HunterMinimumCueCountBeforeChase)
        {
            bool shouldCommit = false;
            if (cueCount >= HunterCueCountMaxFixed)
            {
                shouldCommit = true;
            }
            else if (cueCount == 3)
            {
                shouldCommit = Random.value <= HunterThirdCueCommitChance;
            }
            else if (cueCount == 4)
            {
                shouldCommit = Random.value <= HunterFourthCueCommitChance;
            }

            if (shouldCommit)
            {
                committedToChaseByAi[key] = true;
            }
        }
    }

    private bool TryHandlePendingTarget(DinoAI ai)
    {
        if (ai == null)
        {
            return false;
        }

        if (ShouldFleeBecauseHuntStarted(ai))
        {
            ClearCachedDestination(ai);
            return false;
        }

        if (!CanStartHunt(ai))
        {
            ClearCachedDestination(ai);
            ai.StopChase();
            return false;
        }

        int key = ai.GetInstanceID();
        if (!pendingTargetByAi.TryGetValue(key, out Transform target) || target == null)
        {
            return false;
        }

        if (!IsTargetValid(target))
        {
            ai.SetHunterHiddenMode(false);
            ClearCachedDestination(ai);
            return false;
        }

        ai.SetHunterHiddenMode(true);
        if (ai.Agent != null)
        {
            ai.Agent.isStopped = true;
            ai.Agent.ResetPath();
        }

        if (!cueStartTimeByAi.ContainsKey(key))
        {
            cueStartTimeByAi[key] = Time.time + GetRandomCueStartDelay();
            cueMaxByAi[key] = HunterCueCountMaxFixed;
        }

        if (!targetAcquiredTimeByAi.TryGetValue(key, out float acquiredTime))
        {
            acquiredTime = Time.time;
            targetAcquiredTimeByAi[key] = acquiredTime;
        }

        if (!cueStartTimeByAi.TryGetValue(key, out float cueStartTime) || Time.time < cueStartTime)
        {
            return true;
        }

        TryPlayTargetCue(ai, target);

        bool committed = committedToChaseByAi.TryGetValue(key, out bool committedValue) && committedValue;
        if (!committed)
        {
            return true;
        }

        if (!chaseReadyTimeByAi.TryGetValue(key, out float chaseReadyTime))
        {
            chaseReadyTime = Time.time + GetRandomChaseDelay();
            chaseReadyTimeByAi[key] = chaseReadyTime;
            return true;
        }

        if (Time.time < chaseReadyTime)
        {
            return true;
        }

        if (!IsTargetReadyForChase(target))
        {
            return true;
        }

        if (ShouldFleeBecauseHuntStarted(ai))
        {
            ClearCachedDestination(ai);
            return false;
        }

        if (!CanStartHunt(ai))
        {
            ClearCachedDestination(ai);
            ai.StopChase();
            return false;
        }

        MarkHuntStarted(ai);
        ai.SetHunterHiddenMode(false);
        pendingTargetByAi.Remove(key);
        chaseReadyTimeByAi.Remove(key);
        targetCueDelayByAi.Remove(key);
        nextTargetCueTimeByAi.Remove(key);
        targetCueCountByAi.Remove(key);
        cueStartTimeByAi.Remove(key);
        cueMaxByAi.Remove(key);
        roarPlayedByAi.Remove(key);
        committedToChaseByAi.Remove(key);
        ai.StartChase(target, false);
        return true;
    }

    private static void HideHunterIdle(DinoAI ai)
    {
        if (ai == null)
        {
            return;
        }

        ai.SetHunterHiddenMode(true);
        ai.StopChase();
        if (ai.Agent != null)
        {
            ai.Agent.isStopped = true;
            ai.Agent.ResetPath();
        }
    }

    private bool TryHandleReturnToSpawn(DinoAI ai)
    {
        if (ai == null)
        {
            return false;
        }

        int key = ai.GetInstanceID();
        if (!returnToSpawnByAi.TryGetValue(key, out Vector3 destination))
        {
            return false;
        }

        if (ai.Agent == null || !ai.Agent.isOnNavMesh)
        {
            ai.transform.position = destination;
            returnToSpawnByAi.Remove(key);
            DespawnHunter(ai);
            return true;
        }

        ai.Agent.isStopped = false;
        ai.Agent.speed = Mathf.Max(0f, ai.runSpeed);
        ai.Agent.SetDestination(destination);

        float arriveDistance = Mathf.Max(ai.Agent.stoppingDistance, 1.2f);
        if (!ai.Agent.pathPending && ai.Agent.remainingDistance <= arriveDistance)
        {
            returnToSpawnByAi.Remove(key);
            DespawnHunter(ai);
        }

        return true;
    }

    private static void QueueReturnToSpawn(DinoAI ai)
    {
        if (ai == null || !ai.IsServer)
        {
            return;
        }

        if (ai.hunterSpawnPoint == null)
        {
            DespawnHunter(ai);
            return;
        }

        returnToSpawnByAi[ai.GetInstanceID()] = ai.hunterSpawnPoint.position;
    }

    private Vector3 GetLastChaseDestination(DinoAI ai)
    {
        if (ai == null)
        {
            return Vector3.positiveInfinity;
        }

        if (lastChaseDestinationByAi.TryGetValue(ai.GetInstanceID(), out Vector3 destination))
        {
            return destination;
        }

        return Vector3.positiveInfinity;
    }

    private void SetLastChaseDestination(DinoAI ai, Vector3 destination)
    {
        if (ai == null)
        {
            return;
        }

        lastChaseDestinationByAi[ai.GetInstanceID()] = destination;
    }

    private void ClearCachedDestination(DinoAI ai)
    {
        if (ai == null)
        {
            return;
        }

        int key = ai.GetInstanceID();
        ai.SetHunterHiddenMode(false);
        lastChaseDestinationByAi.Remove(key);
        pendingTargetByAi.Remove(key);
        targetAcquiredTimeByAi.Remove(key);
        chaseReadyTimeByAi.Remove(key);
        targetCueDelayByAi.Remove(key);
        nextTargetCueTimeByAi.Remove(key);
        targetCueCountByAi.Remove(key);
        cueStartTimeByAi.Remove(key);
        cueMaxByAi.Remove(key);
        roarPlayedByAi.Remove(key);
        encounterWillHuntByAi.Remove(key);
        committedToChaseByAi.Remove(key);
        fleeUntilByAi.Remove(key);
        fakeHuntRecoveryUntilByAi.Remove(key);
        fakeHuntRecoveryDestinationByAi.Remove(key);
    }

    private static float GetRandomChaseDelay()
    {
        float min = Mathf.Max(0f, HunterChaseDelayMin);
        float max = Mathf.Max(min, HunterChaseDelayMax);
        return Random.Range(min, max);
    }

    private static float GetRandomCueInterval()
    {
        float min = Mathf.Max(0.1f, HunterTargetCueIntervalMin);
        float max = Mathf.Max(min, HunterTargetCueIntervalMax);
        return Random.Range(min, max);
    }

    private static float GetRandomCueDelay()
    {
        float min = Mathf.Max(0f, HunterTargetCueDelayMin);
        float max = Mathf.Max(min, HunterTargetCueDelayMax);
        return Random.Range(min, max);
    }
    private static float GetRandomCueStartDelay()
    {
        float min = Mathf.Max(0f, HunterCueStartDelayMin);
        float max = Mathf.Max(min, HunterCueStartDelayMax);
        return Random.Range(min, max);
    }

    private static int GetRandomCueCount()
    {
        int min = Mathf.Max(1, HunterCueCountMin);
        int max = Mathf.Max(min, HunterCueCountMax);
        return Random.Range(min, max + 1);
    }

    private static bool CanStartHunt(DinoAI ai)
    {
        if (ai == null || !ai.IsServer)
        {
            return false;
        }

        if (!huntOccurredThisSession)
        {
            return true;
        }

        return huntOwnerInstanceId == ai.GetInstanceID();
    }

    private static void DespawnHunter(DinoAI ai)
    {
        if (ai == null || !ai.IsServer)
        {
            return;
        }

        int key = ai.GetInstanceID();
        if (!despawnedHunters.Add(key))
        {
            return;
        }

        returnToSpawnByAi.Remove(key);

        NetworkObject networkObject = ai.GetComponent<NetworkObject>();
        if (networkObject != null && networkObject.IsSpawned)
        {
            networkObject.Despawn(true);
            return;
        }

        Object.Destroy(ai.gameObject);
    }

    private static void MarkHuntStarted(DinoAI ai)
    {
        if (ai == null || !ai.IsServer)
        {
            return;
        }

        huntOccurredThisSession = true;
        if (huntOwnerInstanceId == 0)
        {
            huntOwnerInstanceId = ai.GetInstanceID();
        }
    }

    private static void MarkHuntOccurred(DinoAI ai)
    {
        if (ai == null || !ai.IsServer)
        {
            return;
        }

        huntOccurredThisSession = true;
        huntOwnerInstanceId = ai.GetInstanceID();
    }

    private bool HandleNonOwnerAfterHuntStart(DinoAI ai)
    {
        if (ai == null || !ai.IsServer)
        {
            return false;
        }

        if (!ShouldFleeBecauseHuntStarted(ai))
        {
            return false;
        }

        if (TryHandleFakeHuntRecovery(ai))
        {
            return true;
        }

        StartFakeHuntRecovery(ai, ai.CurrentTarget);
        return true;
    }

    private static bool ShouldFleeBecauseHuntStarted(DinoAI ai)
    {
        if (ai == null || !ai.IsServer)
        {
            return false;
        }

        if (!huntOccurredThisSession)
        {
            return false;
        }

        return huntOwnerInstanceId != ai.GetInstanceID();
    }

    private static bool ShouldDespawnAfterRecovery(DinoAI ai)
    {
        if (ai == null || !ai.IsServer)
        {
            return false;
        }

        return huntOccurredThisSession && huntOwnerInstanceId != ai.GetInstanceID();
    }

}




