using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class NeutralAggressionBehavior : DinoBehaviorRuleTemplate
{
    private const float RepathDistanceThreshold = 2f;

    private readonly Dictionary<int, float> detectionStartedAtByAi = new Dictionary<int, float>();
    private readonly Dictionary<int, float> detectionDelayByAi = new Dictionary<int, float>();
    private readonly Dictionary<int, Transform> detectedTargetByAi = new Dictionary<int, Transform>();
    private readonly Dictionary<int, float> chaseElapsedByAi = new Dictionary<int, float>();
    private readonly Dictionary<int, float> lostSightElapsedByAi = new Dictionary<int, float>();
    private readonly Dictionary<int, Vector3> lastChaseDestinationByAi = new Dictionary<int, Vector3>();

    public void HandleIdle(DinoAI ai)
    {
        if (TryDetectAndStartChase(ai))
        {
            return;
        }

        ai.ExecuteIdleCycle();
    }

    public void HandleRoam(DinoAI ai)
    {
        if (TryDetectAndStartChase(ai))
        {
            return;
        }

        ExecuteHomeRoam(ai);
    }

    public void HandleInvestigate(DinoAI ai)
    {
        ClearNeutralState(ai, clearChaseState: false);
        ai.ClearInvestigate();
        ai.ChangeState(DinoAI.DinoState.Roam);
    }

    public void HandleChase(DinoAI ai)
    {
        if (ai == null)
        {
            return;
        }

        int key = ai.GetInstanceID();
        Transform target = ai.CurrentTarget;
        if (target == null)
        {
            StopNeutralChase(ai);
            return;
        }

        if (ai.Agent == null)
        {
            StopNeutralChase(ai);
            return;
        }

        ai.Agent.speed = Mathf.Max(0f, ai.runSpeed);
        ai.Agent.isStopped = false;

        Vector3 chaseDestination = target.position;
        bool shouldRepath =
            !ai.Agent.hasPath ||
            !lastChaseDestinationByAi.TryGetValue(key, out Vector3 previousDestination) ||
            (previousDestination - chaseDestination).sqrMagnitude >= (RepathDistanceThreshold * RepathDistanceThreshold);

        if (shouldRepath)
        {
            lastChaseDestinationByAi[key] = chaseDestination;
            ai.Agent.SetDestination(chaseDestination);
        }

        chaseElapsedByAi[key] = GetValue(chaseElapsedByAi, key) + Time.deltaTime;

        if (ai.IsTargetVisibleForChase(target))
        {
            lostSightElapsedByAi[key] = 0f;
        }
        else
        {
            lostSightElapsedByAi[key] = GetValue(lostSightElapsedByAi, key) + Time.deltaTime;
            if (lostSightElapsedByAi[key] >= Mathf.Max(0f, ai.loseSightChaseTimeout))
            {
                StopNeutralChase(ai);
                return;
            }
        }

        if (chaseElapsedByAi[key] >= Mathf.Max(0f, ai.chaseDuration))
        {
            StopNeutralChase(ai);
        }
    }

    private bool TryDetectAndStartChase(DinoAI ai)
    {
        if (ai == null)
        {
            return false;
        }

        if (ai.IsReactionOnCooldown)
        {
            ClearDetectionState(ai.GetInstanceID());
            return false;
        }

        int key = ai.GetInstanceID();
        float scanRadius = Mathf.Max(0f, ai.detectionRadius);
        if (scanRadius <= 0f)
        {
            ClearDetectionState(key);
            return false;
        }

        if (!ai.TryFindClosestPlayer(scanRadius, out Transform target, out _))
        {
            ClearDetectionState(key);
            return false;
        }

        if (!detectedTargetByAi.TryGetValue(key, out Transform detectedTarget) || detectedTarget != target)
        {
            detectedTargetByAi[key] = target;
            detectionStartedAtByAi[key] = Time.time;
            detectionDelayByAi[key] = GetRandomDetectionDelay(ai);
            return false;
        }

        if ((target.position - ai.transform.position).sqrMagnitude > (scanRadius * scanRadius))
        {
            ClearDetectionState(key);
            return false;
        }

        float startedAt = detectionStartedAtByAi[key];
        float delay = detectionDelayByAi[key];
        if (Time.time - startedAt < delay)
        {
            return false;
        }

        ClearDetectionState(key);
        chaseElapsedByAi[key] = 0f;
        lostSightElapsedByAi[key] = 0f;
        lastChaseDestinationByAi.Remove(key);

        if (!ai.TryStartAlertRoarToChase(target))
        {
            ai.StartChase(target);
        }

        return true;
    }

    private void ExecuteHomeRoam(DinoAI ai)
    {
        if (ai == null || ai.Agent == null)
        {
            return;
        }

        ai.Agent.isStopped = false;
        ai.Agent.speed = Mathf.Max(0f, ai.walkSpeed);

        float arriveDistance = Mathf.Max(ai.Agent.stoppingDistance, 0.5f);
        if (ai.Agent.pathPending)
        {
            return;
        }

        if (ai.Agent.hasPath)
        {
            if (ai.Agent.remainingDistance > arriveDistance)
            {
                return;
            }

            ai.Agent.ResetPath();
            ai.ChangeState(DinoAI.DinoState.Idle);
            return;
        }

        if (!ai.TryGetLocalRoamDestinationFromCenter(ai.HomePosition, Mathf.Max(0f, ai.roamRadius), out Vector3 destination))
        {
            ai.ChangeState(DinoAI.DinoState.Idle);
            return;
        }

        if (!ai.TrySetRoamDestination(destination))
        {
            ai.ChangeState(DinoAI.DinoState.Idle);
        }
    }

    private void StopNeutralChase(DinoAI ai)
    {
        ClearNeutralState(ai, clearChaseState: true);
        ai.StartReactionCooldown();
        ai.StopChase();
    }

    private void ClearNeutralState(DinoAI ai, bool clearChaseState)
    {
        if (ai == null)
        {
            return;
        }

        int key = ai.GetInstanceID();
        ClearDetectionState(key);

        if (!clearChaseState)
        {
            return;
        }

        chaseElapsedByAi.Remove(key);
        lostSightElapsedByAi.Remove(key);
        lastChaseDestinationByAi.Remove(key);
    }

    private void ClearDetectionState(int key)
    {
        detectionStartedAtByAi.Remove(key);
        detectionDelayByAi.Remove(key);
        detectedTargetByAi.Remove(key);
    }

    private static float GetRandomDetectionDelay(DinoAI ai)
    {
        float min = Mathf.Max(0f, ai.neutralReactDelayMin);
        float max = Mathf.Max(min, ai.neutralReactDelayMax);
        return Random.Range(min, max);
    }

    private static float GetValue(Dictionary<int, float> source, int key)
    {
        return source.TryGetValue(key, out float value) ? value : 0f;
    }
}
