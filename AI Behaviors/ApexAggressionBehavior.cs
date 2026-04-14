using UnityEngine;
using WalaPaNameHehe.Multiplayer;

public class ApexAggressionBehavior : DinoBehaviorRuleTemplate
{
    private const float ApexSoundFollowMemorySeconds = 5f;

    public void HandleIdle(DinoAI ai)
    {
        if (TryDetectAndStartChase(ai)) return;
        if (TryReactToSound(ai)) return;
        ai.ExecuteIdleCycle();
    }

    public void HandleRoam(DinoAI ai)
    {
        if (ai.IsFollowingSound)
        {
            if (ai.Agent == null)
            {
                ai.StopFollowingSound();
                ai.ExecuteRoamCycle(true);
                return;
            }

            if (ai.TryConsumeRecentSound(ApexSoundFollowMemorySeconds, out Vector3 soundPosition))
            {
                ai.MoveToSound(soundPosition, ai.runSpeed);
                return;
            }

            float arriveThreshold = Mathf.Max(ai.Agent.stoppingDistance, 0.6f);
            bool pathIsUsable =
                ai.Agent.pathPending ||
                (ai.Agent.hasPath && ai.Agent.pathStatus != UnityEngine.AI.NavMeshPathStatus.PathInvalid);

            if (pathIsUsable && ai.Agent.remainingDistance > arriveThreshold)
            {
                ai.Agent.isStopped = false;
                ai.Agent.speed = Mathf.Max(0f, ai.runSpeed);
                return;
            }

            ai.StopFollowingSound();
            ai.ExecuteRoamCycle(true);
            return;
        }

        if (TryDetectAndStartChase(ai)) return;
        if (TryReactToSound(ai)) return;
        ai.ExecuteRoamCycle(true);
    }

    public void HandleInvestigate(DinoAI ai)
    {
        if (TryDetectAndStartChase(ai)) return;

        if (!ai.HasInvestigatePosition)
        {
            ai.ChangeState(DinoAI.DinoState.Roam);
            return;
        }

        if (ai.Agent == null)
        {
            ai.ClearInvestigate();
            ai.ChangeState(DinoAI.DinoState.Roam);
            return;
        }

        ai.Agent.isStopped = false;
        ai.Agent.speed = Mathf.Max(0f, ai.walkSpeed);
        ai.Agent.SetDestination(ai.InvestigatePosition);

        float arriveDistance = Mathf.Max(ai.investigateArriveDistance, ai.Agent.stoppingDistance, 0.6f);
        if (ai.Agent.pathPending)
        {
            return;
        }

        if (ai.Agent.hasPath && ai.Agent.remainingDistance > arriveDistance)
        {
            return;
        }

        ai.Agent.isStopped = true;
        ai.InvestigateSearchTimer += Time.deltaTime;
        if (ai.investigateLookAround)
        {
            ai.transform.Rotate(0f, ai.investigateLookAroundSpeed * Time.deltaTime, 0f);
        }

        if (ai.InvestigateSearchTimer < Mathf.Max(0f, ai.investigateSearchDuration))
        {
            return;
        }

        ai.ClearInvestigate();
        ai.ChangeState(DinoAI.DinoState.Roam);
    }

    public void HandleChase(DinoAI ai)
    {
        if (ai.CurrentTarget == null)
        {
            ai.StopChase();
            return;
        }

        if (UpdateLostSightTimeout(ai))
        {
            return;
        }

        if (ai.Agent == null)
        {
            return;
        }

        ai.Agent.speed = Mathf.Max(0f, ai.runSpeed);
        ai.Agent.isStopped = false;
        ai.Agent.SetDestination(ai.CurrentTarget.position);
    }

    private bool TryDetectAndStartChase(DinoAI ai)
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

        if (ai.IsServer)
        {
            HunterMeterManager.Instance?.ReportApexSeen(target);
        }

        if (!ai.TryStartAlertRoarToChase(target))
        {
            ai.StartChase(target);
        }
        return true;
    }

    private bool TryReactToSound(DinoAI ai)
    {
        if (ai == null || ai.IsReactionOnCooldown)
        {
            return false;
        }

        // Apex dinos never prioritize sound over an active chase target.
        if (ai.HasTarget() || ai.currentState == DinoAI.DinoState.Chase || ai.currentState == DinoAI.DinoState.Investigate)
        {
            return false;
        }

        if (!ai.TryConsumeRecentSound(ApexSoundFollowMemorySeconds, out Vector3 soundPosition))
        {
            return false;
        }

        if (ai.Agent == null)
        {
            return false;
        }

        ai.StartInvestigate(soundPosition);
        return true;
    }

    private bool UpdateLostSightTimeout(DinoAI ai)
    {
        if (ai.CurrentTarget == null)
        {
            ai.ChaseTimer = 0f;
            return false;
        }

        if (ai.IsTargetVisibleForChase(ai.CurrentTarget))
        {
            ai.ChaseTimer = 0f;
            return false;
        }

        ai.ChaseTimer += Time.deltaTime;
        float timeout = Mathf.Max(0f, ai.loseSightChaseTimeout);
        if (timeout <= 0f)
        {
            return false;
        }

        if (ai.ChaseTimer < timeout)
        {
            return false;
        }

        ai.ChaseTimer = 0f;
        ai.StartAlertRoarToRoam();
        return true;
    }
}
