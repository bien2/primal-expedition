using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using WalaPaNameHehe;

public class PlundererAggressionBehavior : DinoBehaviorRuleTemplate
{
    private static int activePlundererInstanceId;
    private static float nextSpawnTime;
    public static System.Action<float> PlundererDespawned;

    private readonly Dictionary<int, bool> grabAttemptedByAi = new Dictionary<int, bool>();
    private readonly Dictionary<int, bool> grabSucceededByAi = new Dictionary<int, bool>();
    private readonly Dictionary<int, float> returnUntilByAi = new Dictionary<int, float>();
    private readonly Dictionary<int, int> waypointIndexByAi = new Dictionary<int, int>();
    private readonly Dictionary<int, float> lastWaypointDistanceSqrByAi = new Dictionary<int, float>();
    private readonly Dictionary<int, int> waypointLoopsRemainingByAi = new Dictionary<int, int>();
    private readonly HashSet<int> returnSequenceByAi = new HashSet<int>();
    private readonly HashSet<int> carrySequenceByAi = new HashSet<int>();
    private readonly Dictionary<int, int> carryWaypointIndexByAi = new Dictionary<int, int>();
    private readonly Dictionary<int, float> carryReleaseTimeByAi = new Dictionary<int, float>();
    private readonly Dictionary<int, Vector3> carryDropTargetByAi = new Dictionary<int, Vector3>();
    private readonly Dictionary<int, WalaPaNameHehe.PlayerHitHandler> carryTargetByAi = new Dictionary<int, WalaPaNameHehe.PlayerHitHandler>();

    public void HandleIdle(DinoAI ai)
    {
        if (ai == null)
        {
            return;
        }

        HandlePlundererCore(ai);
    }

    public void HandleRoam(DinoAI ai)
    {
        if (ai == null)
        {
            return;
        }

        HandlePlundererCore(ai);
    }

    public void HandleInvestigate(DinoAI ai)
    {
        if (ai == null)
        {
            return;
        }

        ai.ClearInvestigate();
        ai.ChangeState(DinoAI.DinoState.Roam);
    }

    public void HandleChase(DinoAI ai)
    {
        if (ai == null)
        {
            return;
        }

        if (!IsActivePlunderer(ai))
        {
            ai.StopChase();
            return;
        }

        int key = ai.GetInstanceID();
        if (IsReturning(key))
        {
            ai.StopChase();
            return;
        }

        if (!grabAttemptedByAi.TryGetValue(key, out bool grabInProgress) || !grabInProgress)
        {
            ai.StopChase();
            return;
        }

        if (grabSucceededByAi.TryGetValue(key, out bool grabbed) && grabbed)
        {
            ai.StopChase();
            return;
        }

        Transform target = ai.CurrentTarget;
        if (target == null)
        {
            grabAttemptedByAi[key] = false;
            ai.StopChase();
            return;
        }

        DisableAgentIfNeeded(ai);
        FlyTowards(ai, target.position, Mathf.Max(0f, ai.runSpeed), lockHeight: false);

        WalaPaNameHehe.PlayerMovement targetMovement = target.GetComponentInParent<WalaPaNameHehe.PlayerMovement>();
        if (targetMovement == null || targetMovement.IsDead)
        {
            grabAttemptedByAi[key] = false;
            ai.StopChase();
            return;
        }

            if (ai.TryAttackTarget(targetMovement))
            {
                grabSucceededByAi[key] = true;
                grabAttemptedByAi[key] = false;
                StartCarrySequence(ai);
                ai.StopChase();
            }
    }

    private bool HandlePlundererCore(DinoAI ai)
    {
        if (ai == null)
        {
            return false;
        }

        if (!ShouldRunOnServer(ai))
        {
            return false;
        }

        int key = ai.GetInstanceID();

        if (!IsActivePlunderer(ai))
        {
            if (!TryActivatePlunderer(ai))
            {
                if (ai.plundererPreloadHidden)
                {
                    ai.SetPlundererHidden(true);
                    DisableAgentIfNeeded(ai);
                    Vector3 homeHover = GetHomeHoverPosition(ai);
                    ai.transform.position = homeHover;
                    if (ai.currentState != DinoAI.DinoState.Roam)
                    {
                        ai.ChangeState(DinoAI.DinoState.Roam);
                    }
                    return true;
                }

                DisableAgentIfNeeded(ai);
                HandleWander(ai);
                return true;
            }
        }

        if (carrySequenceByAi.Contains(key))
        {
            HandleCarry(ai);
            return true;
        }

        if (IsReturning(key))
        {
            HandleReturn(ai);
            return true;
        }

        if (!grabAttemptedByAi.TryGetValue(key, out bool grabInProgress) || !grabInProgress)
        {
            HandleWander(ai);
            return true;
        }

        if (grabSucceededByAi.TryGetValue(key, out bool grabbed) && grabbed)
        {
            StartCarrySequence(ai);
            return true;
        }

        if (!ai.HasTarget())
        {
            HandleWander(ai);
            return true;
        }

        if (ai.currentState != DinoAI.DinoState.Attack)
        {
            ai.ChangeState(DinoAI.DinoState.Attack);
        }

        return true;
    }

    private bool TryActivatePlunderer(DinoAI ai)
    {
        if (ai == null)
        {
            return false;
        }

        int key = ai.GetInstanceID();

        if (activePlundererInstanceId != 0)
        {
            return false;
        }

        if (nextSpawnTime <= 0f)
        {
            nextSpawnTime = Time.time;
        }

        if (Time.time < nextSpawnTime)
        {
            return false;
        }

        activePlundererInstanceId = key;
        grabAttemptedByAi[key] = false;
        grabSucceededByAi[key] = false;
        ai.SetPlundererHidden(false);
        returnUntilByAi.Remove(key);
        InitializeWaypointLoopCount(ai, key);

        if (ai.currentState != DinoAI.DinoState.Roam)
        {
            ai.ChangeState(DinoAI.DinoState.Roam);
        }

        return true;
    }

    private bool IsActivePlunderer(DinoAI ai)
    {
        if (ai == null)
        {
            return false;
        }

        return activePlundererInstanceId == ai.GetInstanceID();
    }

    private bool TryStartGrabAttempt(DinoAI ai)
    {
        if (ai == null)
        {
            return false;
        }

        int key = ai.GetInstanceID();

        WalaPaNameHehe.PlayerMovement targetMovement = GetRandomAlivePlayer();
        Transform target = targetMovement != null ? targetMovement.transform : null;
        if (targetMovement == null || targetMovement.IsDead)
        {
            return false;
        }

        float chance = CalculateGrabChance(ai, targetMovement);
        if (Random.value <= chance)
        {
            grabAttemptedByAi[key] = true;
            ai.PlayAlertRoarAudio();
            ai.StartChase(target, false);
            return true;
        }

        grabAttemptedByAi[key] = false;
        return false;
    }

    private float CalculateGrabChance(DinoAI ai, WalaPaNameHehe.PlayerMovement target)
    {
        if (ai == null)
        {
            return 0f;
        }

        bool isNight = IsNightTime();
        float chance = isNight ? ai.plundererBaseGrabChanceNight : ai.plundererBaseGrabChanceDay;

        if (IsPlayerHunted(target))
        {
            chance += ai.plundererHuntedBonus;
        }

        if (IsPlayerGrouped(target))
        {
            chance += ai.plundererGroupedBonus;
        }

        float cap = Mathf.Clamp01(ai.plundererChanceCapMax);
        chance = Mathf.Min(chance, cap);
        return Mathf.Clamp01(chance);
    }

    private static bool IsPlayerGrouped(WalaPaNameHehe.PlayerMovement target)
    {
        if (target == null)
        {
            return false;
        }

        return target.HasNearbyPlayer || !target.IsIsolated;
    }

    private static bool IsPlayerHunted(WalaPaNameHehe.PlayerMovement target)
    {
        if (target == null)
        {
            return false;
        }

        Transform targetRoot = target.transform.root;
        if (targetRoot == null)
        {
            return false;
        }

        DinoAI[] dinos = Object.FindObjectsByType<DinoAI>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < dinos.Length; i++)
        {
            DinoAI dino = dinos[i];
            if (dino == null || dino.CurrentTarget == null)
            {
                continue;
            }

            if (dino.CurrentTarget.root != targetRoot)
            {
                continue;
            }

            if (dino.currentState == DinoAI.DinoState.Chase || dino.currentState == DinoAI.DinoState.AlertRoar || dino.currentState == DinoAI.DinoState.Attack)
            {
                return true;
            }
        }

        return false;
    }

    private static WalaPaNameHehe.PlayerMovement GetRandomAlivePlayer()
    {
        WalaPaNameHehe.PlayerMovement[] players = Object.FindObjectsByType<WalaPaNameHehe.PlayerMovement>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        if (players == null || players.Length == 0)
        {
            return null;
        }

        int aliveCount = 0;
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] != null && !players[i].IsDead)
            {
                aliveCount++;
            }
        }

        if (aliveCount == 0)
        {
            return null;
        }

        int pick = Random.Range(0, aliveCount);
        for (int i = 0; i < players.Length; i++)
        {
            WalaPaNameHehe.PlayerMovement player = players[i];
            if (player == null || player.IsDead)
            {
                continue;
            }

            if (pick-- == 0)
            {
                return player;
            }
        }

        return null;
    }

    private void BeginReturn(DinoAI ai)
    {
        if (ai == null)
        {
            return;
        }

        int key = ai.GetInstanceID();
        if (returnUntilByAi.ContainsKey(key))
        {
            return;
        }

        const float returnTimeoutSeconds = 10f;
        returnUntilByAi[key] = Time.time + returnTimeoutSeconds;
        grabSucceededByAi[key] = false;
        ai.StopChase();
        ai.ChangeState(DinoAI.DinoState.Roam);
    }

    private bool IsReturning(int key)
    {
        return returnUntilByAi.ContainsKey(key);
    }

    private void HandleReturn(DinoAI ai)
    {
        if (ai == null)
        {
            return;
        }

        int key = ai.GetInstanceID();
        if (!returnUntilByAi.TryGetValue(key, out float until))
        {
            return;
        }

        DisableAgentIfNeeded(ai);
        FlyTowards(ai, GetHomeHoverPosition(ai), Mathf.Max(0f, ai.walkSpeed), lockHeight: true);

        float arriveDistance = Mathf.Max(ai.plundererReturnArriveDistance, 0.5f);
        Vector3 hoverHome = GetHomeHoverPosition(ai);
        float distanceToHome = (ai.transform.position - hoverHome).sqrMagnitude;
        bool arrived = distanceToHome <= arriveDistance * arriveDistance;

        if (arrived || Time.time >= until)
        {
            DespawnPlunderer(ai);
        }
    }

    private void DespawnPlunderer(DinoAI ai)
    {
        if (ai == null)
        {
            return;
        }

        int key = ai.GetInstanceID();
        activePlundererInstanceId = 0;
        grabAttemptedByAi.Remove(key);
        grabSucceededByAi.Remove(key);
        returnUntilByAi.Remove(key);
        waypointIndexByAi.Remove(key);
        lastWaypointDistanceSqrByAi.Remove(key);
        waypointLoopsRemainingByAi.Remove(key);
        returnSequenceByAi.Remove(key);
        carrySequenceByAi.Remove(key);
        carryWaypointIndexByAi.Remove(key);
        carryReleaseTimeByAi.Remove(key);
        carryDropTargetByAi.Remove(key);
        carryTargetByAi.Remove(key);

        float spawnDelay = GetRandomRange(ai.plundererSpawnIntervalMin, ai.plundererSpawnIntervalMax, 1f);
        nextSpawnTime = Time.time + spawnDelay;
        PlundererDespawned?.Invoke(nextSpawnTime);

        NetworkObject networkObject = ai.GetComponent<NetworkObject>();
        if (networkObject != null && networkObject.IsSpawned && ai.IsServer)
        {
            networkObject.Despawn(true);
            return;
        }

        Object.Destroy(ai.gameObject);
    }

    private static float GetRandomRange(float min, float max, float minClamp)
    {
        float clampedMin = Mathf.Max(minClamp, min);
        float clampedMax = Mathf.Max(clampedMin, max);
        return Random.Range(clampedMin, clampedMax);
    }

    private static bool ShouldRunOnServer(DinoAI ai)
    {
        if (ai == null)
        {
            return false;
        }

        NetworkManager nm = NetworkManager.Singleton;
        if (nm != null && nm.IsListening && !ai.IsServer)
        {
            return false;
        }

        return true;
    }

    private static bool IsNightTime()
    {
        Light sun = RenderSettings.sun;
        if (sun == null)
        {
            return false;
        }

        return sun.transform.forward.y > 0.0f;
    }

    private static Vector3 GetHomeHoverPosition(DinoAI ai)
    {
        if (ai == null)
        {
            return Vector3.zero;
        }

        float height = Mathf.Max(0f, ai.plundererFlightHeight);
        Vector3 home = GetPlundererHomePosition(ai);
        home.y += height;
        return home;
    }

    private static void FlyTowards(DinoAI ai, Vector3 target, float speed, bool lockHeight)
    {
        if (ai == null)
        {
            return;
        }

        Vector3 position = ai.transform.position;
        if (lockHeight)
        {
            float height = Mathf.Max(0f, ai.plundererFlightHeight);
            target.y = GetPlundererHomePosition(ai).y + height;
        }

        float clampedSpeed = Mathf.Max(0.01f, speed);
        Vector3 toTarget = target - position;
        float distance = toTarget.magnitude;
        if (distance <= 0.001f)
        {
            return;
        }

        Vector3 direction = toTarget / distance;
        float step = clampedSpeed * Time.deltaTime;
        Vector3 newPosition = distance <= step ? target : position + direction * step;
        ai.transform.position = newPosition;

        Vector3 flatDirection = direction;
        flatDirection.y = 0f;
        if (flatDirection.sqrMagnitude > 0.0001f)
        {
            Quaternion targetYaw = Quaternion.LookRotation(flatDirection.normalized, Vector3.up);
            ai.transform.rotation = Quaternion.RotateTowards(ai.transform.rotation, targetYaw, 360f * Time.deltaTime);
        }
    }

    private static void DisableAgentIfNeeded(DinoAI ai)
    {
        if (ai == null)
        {
            return;
        }

        if (ai.Agent != null && ai.Agent.enabled)
        {
            ai.Agent.ResetPath();
            ai.Agent.enabled = false;
        }
    }

    private void HandleWander(DinoAI ai)
    {
        if (ai == null)
        {
            return;
        }

        int key = ai.GetInstanceID();
        float now = Time.time;
        bool returning = returnSequenceByAi.Contains(key);
        Vector3 target;

        if (returning)
        {
            if (TryGetReturnWaypointTarget(ai, key, out Vector3 returnTarget))
            {
                target = returnTarget;
            }
            else
            {
                target = GetPlundererHomePosition(ai);
                target.y = GetPlundererHomePosition(ai).y + Mathf.Max(0f, ai.plundererFlightHeight);
            }
        }
        else
        {
            if (!TryGetWaypointTarget(ai, key, out target))
            {
                BeginReturn(ai);
                return;
            }
        }

        DisableAgentIfNeeded(ai);
        FlyTowards(ai, target, Mathf.Max(0f, ai.walkSpeed), lockHeight: true);

        float arriveDistance = Mathf.Max(0.5f, ai.plundererWaypointArriveDistance);
        float arriveSqr = arriveDistance * arriveDistance;
        float distSqr = (ai.transform.position - target).sqrMagnitude;
        bool arrived = distSqr <= arriveSqr;

        if (!arrived && lastWaypointDistanceSqrByAi.TryGetValue(key, out float lastSqr))
        {
            // If we passed the closest point (distance increasing), advance to avoid orbiting.
            if (distSqr > lastSqr && lastSqr <= arriveSqr * 4f)
            {
                arrived = true;
            }
        }

        lastWaypointDistanceSqrByAi[key] = distSqr;

        if (arrived)
        {
            if (returning)
            {
                if (HasWaypoints(ai))
                {
                    AdvanceWaypointIndexReturn(ai, key);
                }
                else
                {
                    DespawnPlunderer(ai);
                    return;
                }

                if (!TryGetReturnWaypointTarget(ai, key, out Vector3 nextReturnTarget))
                {
                    // No more waypoints; head to spawn point and despawn when reached.
                    Vector3 home = GetPlundererHomePosition(ai);
                    float homeArrive = Mathf.Max(0.5f, ai.plundererWaypointArriveDistance);
                    if ((ai.transform.position - home).sqrMagnitude <= homeArrive * homeArrive)
                    {
                        DespawnPlunderer(ai);
                        return;
                    }
                }
            }
            else
            {
                if (TryStartGrabAttempt(ai))
                {
                    lastWaypointDistanceSqrByAi.Remove(key);
                    return;
                }

                bool wrapped = AdvanceWaypointIndex(ai, key);
                if (wrapped && ShouldReturnAfterLoop(ai, key))
                {
                    StartReturnSequence(ai);
                    lastWaypointDistanceSqrByAi.Remove(key);
                    return;
                }
            }

            lastWaypointDistanceSqrByAi.Remove(key);
        }
    }

    private void HandleCarry(DinoAI ai)
    {
        if (ai == null)
        {
            return;
        }

        int key = ai.GetInstanceID();

        if (!TryGetCarryTarget(ai, key, out WalaPaNameHehe.PlayerHitHandler targetHandler))
        {
            EndCarryAndReturn(ai);
            return;
        }

        if (!carryReleaseTimeByAi.TryGetValue(key, out float releaseTime))
        {
            releaseTime = Time.time;
            carryReleaseTimeByAi[key] = releaseTime;
        }

        if (Time.time < releaseTime)
        {
            if (TryGetCarryWaypointTarget(ai, key, out Vector3 waypointTarget))
            {
                FlyTowards(ai, waypointTarget, Mathf.Max(0f, ai.runSpeed), lockHeight: true);
                float arriveDistance = Mathf.Max(0.5f, ai.plundererWaypointArriveDistance);
                if ((ai.transform.position - waypointTarget).sqrMagnitude <= arriveDistance * arriveDistance)
                {
                    AdvanceCarryWaypoint(ai, key);
                }
            }
            else
            {
                FlyTowards(ai, GetHomeHoverPosition(ai), Mathf.Max(0f, ai.walkSpeed), lockHeight: true);
            }
            return;
        }

        if (!carryDropTargetByAi.TryGetValue(key, out Vector3 dropPoint) || dropPoint == Vector3.zero)
        {
            dropPoint = GetRandomGroundDropPoint(ai);
            carryDropTargetByAi[key] = dropPoint;
        }

        // Move dino horizontally to drop point at flight height before releasing.
        Vector3 dropTarget = dropPoint;
        dropTarget.y = GetPlundererHomePosition(ai).y + Mathf.Max(0f, ai.plundererFlightHeight);
        FlyTowards(ai, dropTarget, Mathf.Max(0f, ai.runSpeed), lockHeight: true);
        float dropArrive = Mathf.Max(0.5f, ai.plundererDropArriveDistance);
        if ((ai.transform.position - dropTarget).sqrMagnitude <= dropArrive * dropArrive)
        {
            targetHandler.ServerEndBiteHold();
            EndCarryAndReturn(ai);
        }
    }

    private void StartCarrySequence(DinoAI ai)
    {
        if (ai == null)
        {
            return;
        }

        int key = ai.GetInstanceID();
        carrySequenceByAi.Add(key);
        carryWaypointIndexByAi[key] = 0;
        float holdSeconds = Mathf.Max(0.05f, ai.plundererCarryHoldSeconds);
        carryReleaseTimeByAi[key] = Time.time + holdSeconds;
        carryDropTargetByAi.Remove(key);

        Transform target = ai.CurrentTarget;
        if (target != null)
        {
            WalaPaNameHehe.PlayerHitHandler handler = target.GetComponentInParent<WalaPaNameHehe.PlayerHitHandler>();
            if (handler != null)
            {
                carryTargetByAi[key] = handler;
            }
        }
    }

    private bool TryGetCarryTarget(DinoAI ai, int key, out WalaPaNameHehe.PlayerHitHandler handler)
    {
        handler = null;
        if (carryTargetByAi.TryGetValue(key, out handler))
        {
            return handler != null;
        }

        return false;
    }

    private bool TryGetCarryWaypointTarget(DinoAI ai, int key, out Vector3 target)
    {
        target = Vector3.zero;
        if (ai == null || ai.plundererWaypoints == null || ai.plundererWaypoints.Length == 0)
        {
            return false;
        }

        int index = carryWaypointIndexByAi.TryGetValue(key, out int stored) ? stored : 0;
        if (index >= ai.plundererWaypoints.Length)
        {
            return false;
        }

        Transform waypoint = ai.plundererWaypoints[index];
        if (waypoint == null)
        {
            return false;
        }

        target = waypoint.position;
        return true;
    }

    private void AdvanceCarryWaypoint(DinoAI ai, int key)
    {
        if (ai == null || ai.plundererWaypoints == null)
        {
            return;
        }

        int count = ai.plundererWaypoints.Length;
        int index = carryWaypointIndexByAi.TryGetValue(key, out int stored) ? stored : 0;
        index = Mathf.Min(index + 1, count);
        carryWaypointIndexByAi[key] = index;
    }

    private Vector3 GetRandomGroundDropPoint(DinoAI ai)
    {
        if (ai == null)
        {
            return Vector3.zero;
        }

        NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
        if (triangulation.vertices == null || triangulation.vertices.Length == 0 || triangulation.indices == null || triangulation.indices.Length < 3)
        {
            return ai.transform.position;
        }

        int triCount = triangulation.indices.Length / 3;
        int triIndex = Random.Range(0, triCount) * 3;
        Vector3 v0 = triangulation.vertices[triangulation.indices[triIndex]];
        Vector3 v1 = triangulation.vertices[triangulation.indices[triIndex + 1]];
        Vector3 v2 = triangulation.vertices[triangulation.indices[triIndex + 2]];

        // Random point in triangle
        float r1 = Mathf.Sqrt(Random.value);
        float r2 = Random.value;
        Vector3 point = (1 - r1) * v0 + (r1 * (1 - r2)) * v1 + (r1 * r2) * v2;

        if (NavMesh.SamplePosition(point, out NavMeshHit hit, 2f, NavMesh.AllAreas))
        {
            return hit.position;
        }

        return point;
    }

    private void EndCarryAndReturn(DinoAI ai)
    {
        if (ai == null)
        {
            return;
        }

        int key = ai.GetInstanceID();
        carrySequenceByAi.Remove(key);
        carryWaypointIndexByAi.Remove(key);
        carryReleaseTimeByAi.Remove(key);
        carryDropTargetByAi.Remove(key);
        carryTargetByAi.Remove(key);

        BeginReturn(ai);
    }

    private void HandleOrbit(DinoAI ai, int key)
    {
        if (ai == null)
        {
            return;
        }

        const float orbitRadius = 12f;
        float speed = Mathf.Max(0.1f, ai.walkSpeed);
        float angle = (Time.time * speed * 0.5f) + (key * 0.123f);
        Vector3 center = GetPlundererHomePosition(ai);
        float height = Mathf.Max(0f, ai.plundererFlightHeight);
        Vector3 target = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * orbitRadius;
        target.y = center.y + height;

        DisableAgentIfNeeded(ai);
        FlyTowards(ai, target, speed, lockHeight: true);
    }

    private bool HasWaypoints(DinoAI ai)
    {
        return ai != null && ai.plundererWaypoints != null && ai.plundererWaypoints.Length > 0;
    }

    private bool TryGetReturnWaypointTarget(DinoAI ai, int key, out Vector3 target)
    {
        target = Vector3.zero;
        if (!HasWaypoints(ai))
        {
            return false;
        }

        Transform[] waypoints = ai.plundererWaypoints;
        int count = waypoints.Length;
        if (count == 0)
        {
            return false;
        }

        int index = waypointIndexByAi.TryGetValue(key, out int stored) ? stored : 0;
        if (index >= count)
        {
            return false;
        }

        Transform waypoint = waypoints[Mathf.Clamp(index, 0, count - 1)];
        if (waypoint == null)
        {
            return false;
        }

        target = waypoint.position;
        return true;
    }

    private static Vector3 GetPlundererHomePosition(DinoAI ai)
    {
        if (ai == null)
        {
            return Vector3.zero;
        }

        if (ai.plundererSpawnPoint != null)
        {
            return ai.plundererSpawnPoint.position;
        }

        return ai.HomePosition;
    }

    private bool TryGetWaypointTarget(DinoAI ai, int key, out Vector3 target)
    {
        target = Vector3.zero;
        if (!HasWaypoints(ai))
        {
            return false;
        }

        Transform[] waypoints = ai.plundererWaypoints;
        int count = waypoints.Length;
        if (count == 0)
        {
            return false;
        }

        int index = waypointIndexByAi.TryGetValue(key, out int stored) ? stored : 0;
        index = Mathf.Clamp(index, 0, count - 1);

        for (int i = 0; i < count; i++)
        {
            int probe = (index + i) % count;
            Transform waypoint = waypoints[probe];
            if (waypoint == null)
            {
                continue;
            }

            waypointIndexByAi[key] = probe;
            target = waypoint.position;
            return true;
        }

        return false;
    }

    private bool AdvanceWaypointIndex(DinoAI ai, int key)
    {
        if (!HasWaypoints(ai))
        {
            return false;
        }

        int count = ai.plundererWaypoints.Length;
        int index = waypointIndexByAi.TryGetValue(key, out int stored) ? stored : 0;
        int next = (index + 1) % count;
        bool wrapped = next == 0 && count > 0;
        index = next;
        waypointIndexByAi[key] = index;
        return wrapped;
    }

    private void AdvanceWaypointIndexReturn(DinoAI ai, int key)
    {
        if (!HasWaypoints(ai))
        {
            return;
        }

        int count = ai.plundererWaypoints.Length;
        int index = waypointIndexByAi.TryGetValue(key, out int stored) ? stored : 0;
        index = Mathf.Min(index + 1, count);
        waypointIndexByAi[key] = index;
    }

    private void StartReturnSequence(DinoAI ai)
    {
        if (ai == null)
        {
            return;
        }

        int key = ai.GetInstanceID();
        returnSequenceByAi.Add(key);
        grabAttemptedByAi.Remove(key);
        grabSucceededByAi.Remove(key);
        carrySequenceByAi.Remove(key);
        carryWaypointIndexByAi.Remove(key);
        carryReleaseTimeByAi.Remove(key);
        carryDropTargetByAi.Remove(key);
        carryTargetByAi.Remove(key);
        waypointLoopsRemainingByAi.Remove(key);
    }

    private void InitializeWaypointLoopCount(DinoAI ai, int key)
    {
        if (ai == null || !HasWaypoints(ai))
        {
            waypointLoopsRemainingByAi.Remove(key);
            return;
        }

        int minLoops = Mathf.Max(0, ai.plundererWaypointLoopsMin);
        int maxLoops = Mathf.Max(minLoops, ai.plundererWaypointLoopsMax);
        if (maxLoops <= 0)
        {
            waypointLoopsRemainingByAi.Remove(key);
            return;
        }

        int loops = Random.Range(minLoops, maxLoops + 1);
        loops = Mathf.Max(1, loops);
        waypointLoopsRemainingByAi[key] = loops;
    }

    private bool ShouldReturnAfterLoop(DinoAI ai, int key)
    {
        if (ai == null || !HasWaypoints(ai))
        {
            return false;
        }

        if (!waypointLoopsRemainingByAi.TryGetValue(key, out int loopsRemaining))
        {
            return false;
        }

        loopsRemaining -= 1;
        if (loopsRemaining <= 0)
        {
            waypointLoopsRemainingByAi.Remove(key);
            return true;
        }

        waypointLoopsRemainingByAi[key] = loopsRemaining;
        return false;
    }

    public void CleanupState(int key)
    {
        if (activePlundererInstanceId == key)
        {
            activePlundererInstanceId = 0;
        }

        grabAttemptedByAi.Remove(key);
        grabSucceededByAi.Remove(key);
        returnUntilByAi.Remove(key);
        waypointIndexByAi.Remove(key);
        lastWaypointDistanceSqrByAi.Remove(key);
        waypointLoopsRemainingByAi.Remove(key);
        returnSequenceByAi.Remove(key);
        carrySequenceByAi.Remove(key);
        carryWaypointIndexByAi.Remove(key);
        carryReleaseTimeByAi.Remove(key);
        carryDropTargetByAi.Remove(key);
        carryTargetByAi.Remove(key);
    }
}
