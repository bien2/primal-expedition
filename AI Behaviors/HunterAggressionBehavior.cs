using UnityEngine;
using UnityEngine.AI;
using WalaPaNameHehe;
using WalaPaNameHehe.Multiplayer;
using System.Collections.Generic;

public class HunterAggressionBehavior : DinoBehaviorRuleTemplate
{
    private static bool huntOccurredThisSession = false;
    private static int huntOwnerInstanceId = 0;
    private static readonly List<Transform> EmptyPlayers = new List<Transform>(0);
    private static DinoSpawnerManager cachedSpawner;
    private const float HunterChaseDelayMin = 60f;
    private const float HunterChaseDelayMax = 85f;
    private const float HunterIsolationChaseDelay = 5f;
    private const int HunterMinimumCueCountBeforeChase = 3;
    private const float HunterThirdCueCommitChance = 0.4f;
    private const float HunterFourthCueCommitChance = 0.5f;
    private const float RepathDistanceThreshold = 3f;
    private const float FleeProbeDistance = 14f;
    private const float FakeHuntRecoverySeconds = 4f;
    private const int RecoverySampleCount = 10;
    private const float ForceHuntSelectDelaySeconds = 10f;
    private const float ForceHuntCueIntervalSeconds = 10f;
    private const int ForceHuntCueCount = 2;

    private readonly Dictionary<int, Vector3> lastChaseDestinationByAi = new Dictionary<int, Vector3>();
    private readonly Dictionary<int, Transform> pendingTargetByAi = new Dictionary<int, Transform>();
    private readonly Dictionary<int, float> chaseReadyTimeByAi = new Dictionary<int, float>();
    private readonly Dictionary<int, float> nextTargetCueTimeByAi = new Dictionary<int, float>();
    private readonly Dictionary<int, int> targetCueCountByAi = new Dictionary<int, int>();
    private readonly Dictionary<int, int> cueMaxByAi = new Dictionary<int, int>();
    private readonly HashSet<int> roarPlayedByAi = new HashSet<int>();
    private readonly Dictionary<int, bool> encounterWillHuntByAi = new Dictionary<int, bool>();
    private readonly Dictionary<int, bool> committedToChaseByAi = new Dictionary<int, bool>();
    private readonly Dictionary<int, float> fleeUntilByAi = new Dictionary<int, float>();
    private readonly Dictionary<int, float> fakeHuntRecoveryUntilByAi = new Dictionary<int, float>();
    private readonly Dictionary<int, Vector3> fakeHuntRecoveryDestinationByAi = new Dictionary<int, Vector3>();
    private static readonly Dictionary<int, Vector3> returnToSpawnByAi = new Dictionary<int, Vector3>();
    private static readonly Dictionary<int, float> returnToSpawnUntilTimeByAi = new Dictionary<int, float>();
    private readonly Dictionary<int, float> forceHuntStartTimeByAi = new Dictionary<int, float>();
    private readonly Dictionary<int, Transform> forceHuntTargetByAi = new Dictionary<int, Transform>();
    private readonly Dictionary<int, int> forceHuntCuesPlayedByAi = new Dictionary<int, int>();
    private readonly Dictionary<int, float> forceHuntNextCueTimeByAi = new Dictionary<int, float>();

    public static void ResetSessionState()
    {
        huntOccurredThisSession = false;
        huntOwnerInstanceId = 0;
        returnToSpawnByAi.Clear();
        returnToSpawnUntilTimeByAi.Clear();
    }

    private static DinoSpawnerManager GetSpawner()
    {
        if (cachedSpawner != null)
        {
            return cachedSpawner;
        }

        cachedSpawner = Object.FindFirstObjectByType<DinoSpawnerManager>(FindObjectsInactive.Exclude);
        return cachedSpawner;
    }

    private static bool TryGetHunterSpawnLocation(DinoAI ai, out Vector3 position)
    {
        position = default;
        if (ai == null)
        {
            return false;
        }

        DinoSpawnerManager spawner = GetSpawner();
        if (spawner == null)
        {
            return false;
        }

        return spawner.TryGetHunterSpawnLocation(ai.gameObject, out position, out _);
    }

    private static void RequestDespawnHunter(DinoAI ai)
    {
        if (ai == null)
        {
            return;
        }

        DinoSpawnerManager spawner = GetSpawner();
        if (spawner == null)
        {
            return;
        }

        spawner.DespawnHunter(ai.gameObject);
    }

    public static void NotifyHunterKill(DinoAI ai)
    {
        if (ai == null || !ai.IsServer)
        {
            return;
        }

        if (!TryGetHunterSpawnLocation(ai, out Vector3 spawnPosition))
        {
            RequestDespawnHunter(ai);
            return;
        }

        ai.StopChase();
        ai.SetHunterHiddenMode(false);
        ai.ChangeState(DinoAI.DinoState.Roam);
        returnToSpawnByAi[ai.GetInstanceID()] = spawnPosition;
        returnToSpawnUntilTimeByAi[ai.GetInstanceID()] = Time.time + Mathf.Max(0.25f, ai.roamerFleeSeconds);
    }

    public void HandleIdle(DinoAI ai)
    {
        if (TryHandleReturnToSpawn(ai))
        {
            return;
        }

        if (TryHandleForceHuntTest(ai))
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

        if (TryHandleForceHuntTest(ai))
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

        if (TryHandleForceHuntTest(ai))
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

        if (TryHandleForceHuntTest(ai))
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
                chaseReadyTimeByAi.Remove(key);
                nextTargetCueTimeByAi[key] = Time.time + GetRandomCueDelay(ai);
                targetCueCountByAi[key] = 0;
                cueMaxByAi.Remove(key);
                roarPlayedByAi.Remove(key);
                fleeUntilByAi.Remove(key);
                committedToChaseByAi[key] = false;
            }
            else
            {
                pendingTargetByAi.Remove(key);
                chaseReadyTimeByAi.Remove(key);
                nextTargetCueTimeByAi.Remove(key);
                targetCueCountByAi.Remove(key);
                cueMaxByAi.Remove(key);
                roarPlayedByAi.Remove(key);
                committedToChaseByAi.Remove(key);
                fleeUntilByAi[key] = Time.time + Mathf.Max(0.25f, ai.roamerFleeSeconds);
            }
        }

        ai.SetHunterHiddenMode(false);
        ai.StartChase(target, false);
        return true;
    }

    private bool TryHandleForceHuntTest(DinoAI ai)
    {
        if (ai == null || !ai.IsServer || !ai.hunterForceHuntTest)
        {
            return false;
        }

        int key = ai.GetInstanceID();
        if (!forceHuntStartTimeByAi.TryGetValue(key, out float startTime))
        {
            startTime = Time.time;
            forceHuntStartTimeByAi[key] = startTime;
        }

        if (!forceHuntTargetByAi.TryGetValue(key, out Transform target) || target == null)
        {
            ai.SetHunterHiddenMode(true);
            ai.StopChase();

            if (Time.time < startTime + ForceHuntSelectDelaySeconds)
            {
                return true;
            }

            if (!TryPickRandomAlivePlayer(out Transform picked))
            {
                return true;
            }

            forceHuntTargetByAi[key] = picked;
            forceHuntCuesPlayedByAi[key] = 0;
            forceHuntNextCueTimeByAi[key] = Time.time;
            committedToChaseByAi[key] = false;
            targetCueCountByAi[key] = 0;
            cueMaxByAi[key] = ForceHuntCueCount;
            nextTargetCueTimeByAi.Remove(key);
            pendingTargetByAi[key] = picked;
            ai.ChangeState(DinoAI.DinoState.Roam);
            return true;
        }

        if (!IsTargetValid(target))
        {
            ClearCachedDestination(ai);
            ai.StopChase();
            return true;
        }

        if (!ai.hunterForceHuntPlayCues)
        {
            MarkHuntStarted(ai);
            committedToChaseByAi[key] = true;
            ai.SetHunterHiddenMode(false);

            pendingTargetByAi.Remove(key);
            chaseReadyTimeByAi.Remove(key);
            nextTargetCueTimeByAi.Remove(key);
            targetCueCountByAi.Remove(key);
            cueMaxByAi.Remove(key);
            roarPlayedByAi.Remove(key);

            ai.StartChase(target, false);
            ai.hunterForceHuntTest = false;

            return true;
        }

        if (!forceHuntNextCueTimeByAi.TryGetValue(key, out float nextCueTime))
        {
            nextCueTime = Time.time;
            forceHuntNextCueTimeByAi[key] = nextCueTime;
        }

        if (Time.time < nextCueTime)
        {
            ai.SetHunterHiddenMode(true);
            ai.StopChase();
            return true;
        }

        int cuesPlayed = 0;
        forceHuntCuesPlayedByAi.TryGetValue(key, out cuesPlayed);
        if (cuesPlayed < ForceHuntCueCount)
        {
            TryPlayTargetCue(ai, target, true);
            cuesPlayed += 1;
            forceHuntCuesPlayedByAi[key] = cuesPlayed;
            forceHuntNextCueTimeByAi[key] = Time.time + ForceHuntCueIntervalSeconds;
            return true;
        }

        MarkHuntStarted(ai);
        committedToChaseByAi[key] = true;
        ai.SetHunterHiddenMode(false);

        pendingTargetByAi.Remove(key);
        chaseReadyTimeByAi.Remove(key);
        nextTargetCueTimeByAi.Remove(key);
        targetCueCountByAi.Remove(key);
        cueMaxByAi.Remove(key);
        roarPlayedByAi.Remove(key);

        ai.StartChase(target, false);
        ai.hunterForceHuntTest = false;
        return true;
    }

    private static bool TryPickRandomAlivePlayer(out Transform target)
    {
        target = null;
        WalaPaNameHehe.PlayerMovement[] players = Object.FindObjectsByType<WalaPaNameHehe.PlayerMovement>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (players == null || players.Length == 0)
        {
            return false;
        }

        int startIndex = Random.Range(0, players.Length);
        for (int i = 0; i < players.Length; i++)
        {
            WalaPaNameHehe.PlayerMovement p = players[(startIndex + i) % players.Length];
            if (p == null || p.IsDead)
            {
                continue;
            }

            target = p.transform;
            return true;
        }

        return false;
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
        chaseReadyTimeByAi.Remove(key);
        nextTargetCueTimeByAi.Remove(key);
        targetCueCountByAi.Remove(key);
        cueMaxByAi.Remove(key);
        roarPlayedByAi.Remove(key);
        encounterWillHuntByAi.Remove(key);
        fleeUntilByAi.Remove(key);

        fakeHuntRecoveryUntilByAi[key] = Time.time + Mathf.Max(FakeHuntRecoverySeconds, ai.roamerFleeSeconds);
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
        if (!nextTargetCueTimeByAi.TryGetValue(key, out float nextAllowedTime))
        {
            nextAllowedTime = Time.time + GetRandomCueDelay(ai);
            nextTargetCueTimeByAi[key] = nextAllowedTime;
        }

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
        int cueMaxFallback = ai != null ? Mathf.Max(1, ai.hunterCueCountMax) : 5;
        if (!cueMaxByAi.TryGetValue(key, out int cueMax) || cueMax <= 0)
        {
            cueMax = cueMaxFallback;
        }
        if (cueCount >= cueMax)
        {
            return;
        }

        playerMovement.TriggerHunterTargetSound();
        nextTargetCueTimeByAi[key] = Time.time + GetRandomCueDelay(ai);
        cueCount += 1;
        targetCueCountByAi[key] = cueCount;

        int minimumCueCountBeforeChase = Mathf.Min(HunterMinimumCueCountBeforeChase, cueMax);
        if (cueCount >= minimumCueCountBeforeChase)
        {
            bool shouldCommit = false;
            if (cueCount >= cueMax)
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

        if (!cueMaxByAi.ContainsKey(key))
        {
            cueMaxByAi[key] = GetRandomCueCount(ai);
        }

        if (!nextTargetCueTimeByAi.ContainsKey(key))
        {
            nextTargetCueTimeByAi[key] = Time.time + GetRandomCueDelay(ai);
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
        nextTargetCueTimeByAi.Remove(key);
        targetCueCountByAi.Remove(key);
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

        if (returnToSpawnUntilTimeByAi.TryGetValue(key, out float untilTime) && Time.time >= untilTime)
        {
            returnToSpawnByAi.Remove(key);
            returnToSpawnUntilTimeByAi.Remove(key);
            RequestDespawnHunter(ai);
            return true;
        }

        if (ai.Agent == null || !ai.Agent.isOnNavMesh)
        {
            ai.transform.position = destination;
            returnToSpawnByAi.Remove(key);
            returnToSpawnUntilTimeByAi.Remove(key);
            RequestDespawnHunter(ai);
            return true;
        }

        ai.Agent.isStopped = false;
        ai.Agent.speed = Mathf.Max(0f, ai.runSpeed);
        ai.Agent.SetDestination(destination);

        if (!ai.Agent.pathPending)
        {
            float arriveDistance = Mathf.Max(ai.Agent.stoppingDistance, 0.5f);
            if (ai.Agent.hasPath && ai.Agent.remainingDistance <= arriveDistance)
            {
                returnToSpawnByAi.Remove(key);
                returnToSpawnUntilTimeByAi.Remove(key);
                RequestDespawnHunter(ai);
            }
            else if (!ai.Agent.hasPath)
            {
                Vector3 delta = ai.transform.position - destination;
                delta.y = 0f;
                if (delta.sqrMagnitude <= (arriveDistance * arriveDistance))
                {
                    returnToSpawnByAi.Remove(key);
                    returnToSpawnUntilTimeByAi.Remove(key);
                    RequestDespawnHunter(ai);
                }
            }
        }

        return true;
    }

    private static void QueueReturnToSpawn(DinoAI ai)
    {
        if (ai == null || !ai.IsServer)
        {
            return;
        }

        if (!TryGetHunterSpawnLocation(ai, out Vector3 spawnPosition))
        {
            RequestDespawnHunter(ai);
            return;
        }

        returnToSpawnByAi[ai.GetInstanceID()] = spawnPosition;
        returnToSpawnUntilTimeByAi[ai.GetInstanceID()] = Time.time + Mathf.Max(0.25f, ai.roamerFleeSeconds);
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
        chaseReadyTimeByAi.Remove(key);
        nextTargetCueTimeByAi.Remove(key);
        targetCueCountByAi.Remove(key);
        cueMaxByAi.Remove(key);
        roarPlayedByAi.Remove(key);
        encounterWillHuntByAi.Remove(key);
        committedToChaseByAi.Remove(key);
        fleeUntilByAi.Remove(key);
        fakeHuntRecoveryUntilByAi.Remove(key);
        fakeHuntRecoveryDestinationByAi.Remove(key);
        forceHuntStartTimeByAi.Remove(key);
        forceHuntTargetByAi.Remove(key);
        forceHuntCuesPlayedByAi.Remove(key);
        forceHuntNextCueTimeByAi.Remove(key);
    }

    private static float GetRandomChaseDelay()
    {
        float min = Mathf.Max(0f, HunterChaseDelayMin);
        float max = Mathf.Max(min, HunterChaseDelayMax);
        return Random.Range(min, max);
    }

    private static float GetRandomCueDelay(DinoAI ai)
    {
        float min = ai != null ? Mathf.Max(0.1f, ai.hunterCueDelayMin) : 40f;
        float max = ai != null ? Mathf.Max(min, ai.hunterCueDelayMax) : 60f;
        return Random.Range(min, max);
    }

    private static int GetRandomCueCount(DinoAI ai)
    {
        int min = ai != null ? Mathf.Max(1, ai.hunterCueCountMin) : 5;
        int max = ai != null ? Mathf.Max(min, ai.hunterCueCountMax) : 5;
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




