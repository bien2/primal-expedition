using UnityEngine;
using System.Collections.Generic;
using WalaPaNameHehe.Multiplayer;

public class RoamerAggressionBehavior : DinoBehaviorRuleTemplate
{
    private const float FleeSampleRadiusScale = 0.6f;
    private const float FleeStuckCheckInterval = 1.5f;
    private const float FleeStuckMinDistance = 0.5f;
    private const float SoundSeekSeconds = 6f;
    private static readonly Dictionary<int, float> nextRoarAllowedTime = new();
    private static readonly Dictionary<int, float> fleeUntilTime = new();
    private static readonly Dictionary<int, Vector3> fleeFromPosition = new();
    private static readonly Dictionary<int, Vector3> lastFleePosition = new();
    private static readonly Dictionary<int, float> lastFleeSampleTime = new();
    private static readonly Dictionary<int, float> soundSeekUntilTime = new();
    private static readonly Dictionary<int, Vector3> soundSeekDestination = new();

    public void HandleIdle(DinoAI ai)
    {
        if (UpdateFlee(ai))
        {
            return;
        }

        if (TryFleeFromPlayer(ai))
        {
            return;
        }

        if (UpdateSoundSeek(ai))
        {
            return;
        }

        if (TrySeekSound(ai))
        {
            return;
        }

        ai.ExecuteIdleCycle();
    }

    public void HandleRoam(DinoAI ai)
    {
        if (UpdateFlee(ai))
        {
            return;
        }

        if (TryFleeFromPlayer(ai))
        {
            return;
        }

        if (UpdateSoundSeek(ai))
        {
            return;
        }

        if (TrySeekSound(ai))
        {
            return;
        }

        ai.ExecuteRoamCycle(false, true);
    }

    public void HandleInvestigate(DinoAI ai)
    {
        ai.ClearInvestigate();
        ai.ChangeState(DinoAI.DinoState.Roam);
    }

    public void HandleChase(DinoAI ai)
    {
        if (UpdateFlee(ai))
        {
            return;
        }

        if (TryFleeFromPlayer(ai))
        {
            return;
        }

        if (UpdateSoundSeek(ai))
        {
            return;
        }

        ai.StopChase();
    }

    private bool TryFleeFromPlayer(DinoAI ai)
    {
        if (ai == null || ai.IsReactionOnCooldown)
        {
            return false;
        }

        float scanRadius = Mathf.Max(0f, ai.detectionRadius);
        if (scanRadius <= 0f)
        {
            return false;
        }

        if (!ai.TryFindClosestPlayer(scanRadius, out Transform target, out _))
        {
            return false;
        }

        if (target == null || !ai.IsTargetVisibleForChase(target))
        {
            return false;
        }

        if (ai.IsServer)
        {
            HunterMeterManager.Instance?.ReportRoamerEncounter(target);
        }

        ai.StartFleeToDespawnPoint(true);
        return true;
    }

    private bool TrySeekSound(DinoAI ai)
    {
        if (ai == null || ai.IsReactionOnCooldown)
        {
            return false;
        }

        if (!ai.TryConsumeRecentSound(out Vector3 position))
        {
            return false;
        }

        BeginSoundSeek(ai, position);
        return true;
    }

    private void BeginFlee(DinoAI ai, Vector3 fromPosition)
    {
        if (ai == null)
        {
            return;
        }

        int key = ai.GetInstanceID();
        float duration = Mathf.Max(0.1f, ai.roamerFleeSeconds);
        fleeUntilTime[key] = Time.time + duration;
        fleeFromPosition[key] = fromPosition;
        lastFleePosition[key] = ai.transform.position;
        lastFleeSampleTime[key] = Time.time;
    }

    private bool UpdateFlee(DinoAI ai)
    {
        if (ai == null || ai.Agent == null)
        {
            return false;
        }

        int key = ai.GetInstanceID();
        if (!fleeUntilTime.TryGetValue(key, out float until) || Time.time >= until)
        {
            return false;
        }

        if (IsFleeStuck(ai, key))
        {
            Vector3 randomDir = Random.insideUnitSphere;
            randomDir.y = 0f;
            if (randomDir.sqrMagnitude < 0.001f)
            {
                randomDir = ai.transform.forward;
            }
            fleeFromPosition[key] = ai.transform.position + randomDir.normalized;
            if (ai.Agent.hasPath)
            {
                ai.Agent.ResetPath();
            }
        }

        if (!fleeFromPosition.TryGetValue(key, out Vector3 fromPos))
        {
            fromPos = ai.transform.position - ai.transform.forward;
        }

        Vector3 away = ai.transform.position - fromPos;
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f)
        {
            away = ai.transform.forward;
        }

        Vector3 fleeCenter = ai.transform.position + away.normalized * Mathf.Max(2f, ai.roamRadius);
        float sampleRadius = Mathf.Max(4f, ai.roamRadius * FleeSampleRadiusScale);

        if (ai.TryGetLocalRoamDestinationFromCenter(fleeCenter, sampleRadius, out Vector3 destination))
        {
            ai.Agent.isStopped = false;
            ai.Agent.speed = Mathf.Max(0f, ai.runSpeed);
            ai.Agent.SetDestination(destination);
            return true;
        }

        ai.ExecuteRoamCycle(true, true);
        return true;
    }

    private void BeginSoundSeek(DinoAI ai, Vector3 destination)
    {
        if (ai == null || ai.Agent == null)
        {
            return;
        }

        int key = ai.GetInstanceID();
        soundSeekDestination[key] = destination;
        soundSeekUntilTime[key] = Time.time + Mathf.Max(0.5f, SoundSeekSeconds);
        ai.Agent.isStopped = false;
        ai.Agent.speed = Mathf.Max(0f, ai.walkSpeed);
        ai.Agent.SetDestination(destination);
    }

    private bool UpdateSoundSeek(DinoAI ai)
    {
        if (ai == null || ai.Agent == null)
        {
            return false;
        }

        int key = ai.GetInstanceID();
        if (!soundSeekUntilTime.TryGetValue(key, out float until) || Time.time >= until)
        {
            return false;
        }

        if (!soundSeekDestination.TryGetValue(key, out Vector3 destination))
        {
            return false;
        }

        if (!ai.Agent.pathPending && !ai.Agent.hasPath)
        {
            ai.Agent.SetDestination(destination);
        }

        float arriveThreshold = Mathf.Max(ai.Agent.stoppingDistance, 0.6f);
        if (ai.Agent.hasPath && ai.Agent.remainingDistance > arriveThreshold)
        {
            ai.Agent.isStopped = false;
            ai.Agent.speed = Mathf.Max(0f, ai.walkSpeed);
            return true;
        }

        soundSeekUntilTime.Remove(key);
        soundSeekDestination.Remove(key);
        return false;
    }

    private bool IsFleeStuck(DinoAI ai, int key)
    {
        if (!lastFleeSampleTime.TryGetValue(key, out float lastTime))
        {
            lastFleeSampleTime[key] = Time.time;
            lastFleePosition[key] = ai.transform.position;
            return false;
        }

        float elapsed = Time.time - lastTime;
        if (elapsed < FleeStuckCheckInterval)
        {
            return false;
        }

        Vector3 lastPos = lastFleePosition.TryGetValue(key, out Vector3 pos) ? pos : ai.transform.position;
        float moved = (ai.transform.position - lastPos).magnitude;
        lastFleeSampleTime[key] = Time.time;
        lastFleePosition[key] = ai.transform.position;
        return moved < FleeStuckMinDistance;
    }

    public void CleanupState(int key)
    {
        nextRoarAllowedTime.Remove(key);
        fleeUntilTime.Remove(key);
        fleeFromPosition.Remove(key);
        lastFleePosition.Remove(key);
        lastFleeSampleTime.Remove(key);
        soundSeekUntilTime.Remove(key);
        soundSeekDestination.Remove(key);
    }
}
