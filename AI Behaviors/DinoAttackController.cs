using System.Collections;
using UnityEngine;
using Unity.Netcode;

public class DinoAttackController : NetworkBehaviour
{
    public enum AttackEffect
    {
        Instakill,
        DownState,
        Grab
    }

    [SerializeField] private bool enableAttack = true;
    [Min(0f)] [SerializeField] private float attackRadius = 1.2f;
    [Min(0.01f)] [SerializeField] private float attackCheckInterval = 0.1f;

    [SerializeField] private bool enableJumpAttack = true;
    [Min(0f)] [SerializeField] private float jumpAttackRadius = 1.5f;
    [Min(0.05f)] [SerializeField] private float jumpAttackDurationSeconds = 0.60f;
    [Min(0f)] [SerializeField] private float jumpAttackHeight = 4.0f;

    [SerializeField] private AttackEffect attackEffect = AttackEffect.Instakill;
    [SerializeField] private float attackCooldown = 1.5f;
    [SerializeField] private float attackWindupSeconds = 0.15f;
    [SerializeField] private float validateRange = 1.2f;
    [SerializeField] private float attackStateSeconds = 0.6f;
    [SerializeField] private float knockbackImpulse = 6f;
    [SerializeField] private float knockbackUpward = 1.2f;

    [SerializeField] private bool useBlackoutOnInstakill = true;
    [Min(0f)] [SerializeField] private float blackoutDelaySeconds = 1f;

    [SerializeField] private Animator animator;
    [SerializeField] private string attackStateName = "Attack";
    [SerializeField] private Transform bitePoint;
    [SerializeField] private float biteHoldSeconds = 1.5f;

    private float nextAttackTime;
    private Coroutine attackRoutine;
    private Coroutine blackoutRoutine;
    private bool blackoutTriggeredOrScheduled;
    private DinoAI dinoAi;
    private WalaPaNameHehe.PlayerMovement lastAttackTarget;

    [HideInInspector] [SerializeField] private bool migratedAttackSettings;

    public bool EnableAttack => enableAttack;
    public float AttackRadius => Mathf.Max(0f, attackRadius);
    public float AttackCheckInterval => Mathf.Max(0.01f, attackCheckInterval);
    public bool EnableJumpAttack => enableJumpAttack;
    public float JumpAttackRadius => Mathf.Max(0f, jumpAttackRadius);
    public float JumpAttackDurationSeconds => Mathf.Max(0.05f, jumpAttackDurationSeconds);
    public float JumpAttackHeight => Mathf.Max(0f, jumpAttackHeight);

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }
        dinoAi = GetComponent<DinoAI>();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (migratedAttackSettings)
        {
            return;
        }

        DinoAI ai = GetComponent<DinoAI>();
        if (ai == null)
        {
            return;
        }

        enableAttack = ai.enableAttackKill;
        attackRadius = ai.attackKillRadius;
        attackCheckInterval = ai.attackCheckInterval;
        enableJumpAttack = ai.enableJumpAttack;
        jumpAttackRadius = ai.jumpAttackRadius;
        jumpAttackDurationSeconds = ai.jumpAttackDurationSeconds;
        jumpAttackHeight = ai.jumpAttackHeight;

        migratedAttackSettings = true;
    }
#endif

    public bool TryAttackTarget(WalaPaNameHehe.PlayerMovement target)
    {
        if (target == null)
        {
            return false;
        }

        if (IsNetworkActive() && !IsServer)
        {
            return false;
        }

        bool isGrab = attackEffect == AttackEffect.Grab;
        if (!isGrab && Time.time < nextAttackTime)
        {
            return false;
        }

        float attackRange = GetAttackRange();
        if (attackRange > 0f)
        {
            float sqr = (target.transform.position - transform.position).sqrMagnitude;
            if (sqr > attackRange * attackRange)
            {
                return false;
            }
        }

        if (attackRoutine != null)
        {
            StopCoroutine(attackRoutine);
        }
        if (blackoutRoutine != null)
        {
            StopCoroutine(blackoutRoutine);
            blackoutRoutine = null;
        }
        blackoutTriggeredOrScheduled = false;

        TriggerAttackAnimation();
        attackRoutine = StartCoroutine(AttackRoutine(target));
        if (!isGrab)
        {
            nextAttackTime = Time.time + Mathf.Max(0.05f, attackCooldown);
        }
        lastAttackTarget = target;
        return true;
    }

    public bool ForceInstakill(WalaPaNameHehe.PlayerMovement target)
    {
        if (target == null)
        {
            return false;
        }

        if (IsNetworkActive() && !IsServer)
        {
            return false;
        }

        if (target.IsDead)
        {
            return false;
        }

        lastAttackTarget = target;
        if (blackoutRoutine != null)
        {
            StopCoroutine(blackoutRoutine);
            blackoutRoutine = null;
        }
        blackoutTriggeredOrScheduled = false;
        TriggerAttackAnimation();

        WalaPaNameHehe.PlayerHitHandler hitHandler = target.GetComponent<WalaPaNameHehe.PlayerHitHandler>();
        if (hitHandler == null)
        {
            hitHandler = target.GetComponentInChildren<WalaPaNameHehe.PlayerHitHandler>();
        }

        if (hitHandler == null)
        {
            return false;
        }

        Vector3 impulse = GetKnockbackImpulse(target);
        NetworkObject netObj = GetComponent<NetworkObject>();
        bool hasAttackerNetworkId = netObj != null && netObj.IsSpawned;
        ulong attackerNetworkId = hasAttackerNetworkId ? netObj.NetworkObjectId : 0;
        hitHandler.ServerApplyInstakillWithRagdoll(impulse, hasAttackerNetworkId, attackerNetworkId, transform);
        ScheduleBlackout(target);
        if (dinoAi != null)
        {
            dinoAi.ChangeState(DinoAI.DinoState.Idle);
        }

        return true;
    }

    private IEnumerator AttackRoutine(WalaPaNameHehe.PlayerMovement target)
    {
        float windup = attackEffect == AttackEffect.Grab ? 0f : Mathf.Max(0f, attackWindupSeconds);
        if (windup > 0f)
        {
            yield return new WaitForSeconds(windup);
        }

        if (target == null || target.IsDead)
        {
            attackRoutine = null;
            yield break;
        }

        float attackRange = GetAttackRange();
        if (attackRange > 0f)
        {
            float sqr = (target.transform.position - transform.position).sqrMagnitude;
            if (sqr > attackRange * attackRange)
            {
                attackRoutine = null;
                yield break;
            }
        }

        WalaPaNameHehe.PlayerHitHandler hitHandler = target.GetComponent<WalaPaNameHehe.PlayerHitHandler>();
        if (hitHandler == null)
        {
            hitHandler = target.GetComponentInChildren<WalaPaNameHehe.PlayerHitHandler>();
        }

        if (hitHandler != null)
        {
            WalaPaNameHehe.PlayerHitHandler.HitResult result =
                attackEffect == AttackEffect.Instakill
                    ? WalaPaNameHehe.PlayerHitHandler.HitResult.Instakill
                    : (attackEffect == AttackEffect.DownState
                        ? WalaPaNameHehe.PlayerHitHandler.HitResult.DownState
                        : WalaPaNameHehe.PlayerHitHandler.HitResult.None);

            bool forceGrabForPlunderer = dinoAi != null && dinoAi.aggressionType == DinoAI.AggressionType.Plunderer;
            bool shouldUseGrabHold = bitePoint != null && (attackEffect == AttackEffect.Grab || forceGrabForPlunderer);
            if (shouldUseGrabHold)
            {
                NetworkObject netObj = GetComponent<NetworkObject>();
                if (netObj != null)
                {
                    float holdSeconds = Mathf.Max(0.05f, biteHoldSeconds);
                    if (dinoAi != null && dinoAi.aggressionType == DinoAI.AggressionType.Plunderer)
                    {
                        holdSeconds = Mathf.Max(0.05f, dinoAi.plundererCarryHoldSeconds);
                    }
                    string path = BuildRelativePath(transform, bitePoint);
                    if (dinoAi != null && dinoAi.aggressionType == DinoAI.AggressionType.Plunderer)
                    {
                        hitHandler.ServerBeginGrabHold(netObj.NetworkObjectId, path, holdSeconds, true);
                    }
                    else
                    {
                        hitHandler.ServerBeginGrabHold(netObj.NetworkObjectId, path, holdSeconds);
                    }
                }
                else
                {
                    hitHandler.ServerApplyHit(WalaPaNameHehe.PlayerHitHandler.HitResult.None);
                }
            }
            else if (attackEffect == AttackEffect.Instakill)
            {
                Vector3 impulse = GetKnockbackImpulse(target);
                NetworkObject netObj = GetComponent<NetworkObject>();
                bool hasAttackerNetworkId = netObj != null && netObj.IsSpawned;
                ulong attackerNetworkId = hasAttackerNetworkId ? netObj.NetworkObjectId : 0;
                hitHandler.ServerApplyInstakillWithRagdoll(impulse, hasAttackerNetworkId, attackerNetworkId, transform);
                ScheduleBlackout(target);
                if (dinoAi != null)
                {
                    dinoAi.ChangeState(DinoAI.DinoState.Idle);
                }
            }
            else
            {
                hitHandler.ServerApplyHit(result);
            }
        }

            if (dinoAi != null && dinoAi.aggressionType == DinoAI.AggressionType.Hunter
                && attackEffect == AttackEffect.Instakill)
            {
                WalaPaNameHehe.Multiplayer.HunterMeterManager.Instance?.OnHunterKill();
            }

            attackRoutine = null;
        }

    // Animation event hook: place this on the attack animation at the desired blackout frame.
    public void AnimEvent_TriggerBlackout()
    {
        if (blackoutTriggeredOrScheduled)
        {
            return;
        }

        if (attackEffect != AttackEffect.Instakill)
        {
            return;
        }

        WalaPaNameHehe.PlayerMovement target = lastAttackTarget;
        if (target == null && dinoAi != null && dinoAi.CurrentTarget != null)
        {
            target = dinoAi.CurrentTarget.GetComponentInParent<WalaPaNameHehe.PlayerMovement>();
        }
        if (target == null)
        {
            target = FindNearestPlayerInRange();
        }
        if (target == null)
        {
            return;
        }

        WalaPaNameHehe.PlayerHitHandler hitHandler = target.GetComponent<WalaPaNameHehe.PlayerHitHandler>();
        if (hitHandler == null)
        {
            hitHandler = target.GetComponentInChildren<WalaPaNameHehe.PlayerHitHandler>(true);
        }

        if (hitHandler == null)
        {
            return;
        }

        if (IsNetworkActive() && !IsServer)
        {
            RequestTriggerBlackoutServerRpc();
            return;
        }

        Debug.Log("DinoAttackController: Calling ServerTriggerBlackoutForOwner.");
        blackoutTriggeredOrScheduled = true;
        hitHandler.ServerTriggerBlackoutForOwner(false);
    }

    private void ScheduleBlackout(WalaPaNameHehe.PlayerMovement target)
    {
        if (!useBlackoutOnInstakill)
        {
            return;
        }

        if (blackoutTriggeredOrScheduled)
        {
            return;
        }

        if (attackEffect != AttackEffect.Instakill)
        {
            return;
        }

        if (target == null)
        {
            return;
        }

        if (IsNetworkActive() && !IsServer)
        {
            return;
        }

        if (blackoutRoutine != null)
        {
            StopCoroutine(blackoutRoutine);
        }

        blackoutTriggeredOrScheduled = true;
        blackoutRoutine = StartCoroutine(BlackoutDelayRoutine(target));
    }

    private IEnumerator BlackoutDelayRoutine(WalaPaNameHehe.PlayerMovement target)
    {
        float delay = Mathf.Max(0f, blackoutDelaySeconds);
        if (delay > 0f)
        {
            yield return new WaitForSeconds(delay);
        }

        if (target == null)
        {
            blackoutRoutine = null;
            yield break;
        }

        WalaPaNameHehe.PlayerHitHandler hitHandler = target.GetComponent<WalaPaNameHehe.PlayerHitHandler>();
        if (hitHandler == null)
        {
            hitHandler = target.GetComponentInChildren<WalaPaNameHehe.PlayerHitHandler>(true);
        }

        if (hitHandler == null)
        {
            blackoutRoutine = null;
            yield break;
        }

        hitHandler.ServerTriggerBlackoutForOwner(true);
        blackoutRoutine = null;
    }

    private WalaPaNameHehe.PlayerMovement FindNearestPlayerInRange()
    {
        float range = Mathf.Max(0.5f, GetAttackRange() * 2f);
        WalaPaNameHehe.PlayerMovement[] players = FindObjectsByType<WalaPaNameHehe.PlayerMovement>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        WalaPaNameHehe.PlayerMovement best = null;
        float bestSqr = float.MaxValue;
        Vector3 self = transform.position;

        for (int i = 0; i < players.Length; i++)
        {
            WalaPaNameHehe.PlayerMovement player = players[i];
            if (player == null || player.IsDead)
            {
                continue;
            }

            float sqr = (player.transform.position - self).sqrMagnitude;
            if (sqr > range * range)
            {
                continue;
            }

            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = player;
            }
        }

        return best;
    }

    [ServerRpc]
    private void RequestTriggerBlackoutServerRpc(ServerRpcParams serverRpcParams = default)
    {
        if (lastAttackTarget == null)
        {
            return;
        }

        WalaPaNameHehe.PlayerHitHandler hitHandler = lastAttackTarget.GetComponent<WalaPaNameHehe.PlayerHitHandler>();
        if (hitHandler == null)
        {
            hitHandler = lastAttackTarget.GetComponentInChildren<WalaPaNameHehe.PlayerHitHandler>(true);
        }

        if (hitHandler == null)
        {
            return;
        }

        hitHandler.ServerTriggerBlackoutForOwner();
    }

    private void TriggerAttackAnimation()
    {
        StartAttackVisualFromAnimation();
    }

    private void StartAttackVisualFromAnimation()
    {
        if (dinoAi == null)
        {
            return;
        }

        dinoAi.TriggerAttackVisual(attackStateSeconds);

        if (animator != null && !string.IsNullOrWhiteSpace(attackStateName))
        {
            StartCoroutine(AttackVisualRoutine());
        }
    }

    private IEnumerator AttackVisualRoutine()
    {
        const float maxWaitSeconds = 0.5f;
        float start = Time.time;
        int stateHash = Animator.StringToHash(attackStateName);

        while (Time.time - start < maxWaitSeconds)
        {
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            if (state.shortNameHash == stateHash || state.IsName(attackStateName))
            {
                float duration = Mathf.Max(0.05f, state.length);
                dinoAi.TriggerAttackVisual(duration);
                yield break;
            }

            if (animator.IsInTransition(0))
            {
                AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(0);
                if (next.shortNameHash == stateHash || next.IsName(attackStateName))
                {
                    float duration = Mathf.Max(0.05f, next.length);
                    dinoAi.TriggerAttackVisual(duration);
                    yield break;
                }
            }

            yield return null;
        }

        dinoAi.TriggerAttackVisual(attackStateSeconds);
    }

    private bool IsNetworkActive()
    {
        return NetworkManager != null && IsSpawned;
    }

    private float GetAttackRange()
    {
        if (attackRadius > 0f)
        {
            return attackRadius;
        }

        return validateRange;
    }

    private Vector3 GetKnockbackImpulse(WalaPaNameHehe.PlayerMovement target)
    {
        if (target == null)
        {
            return Vector3.zero;
        }

        Vector3 direction = target.transform.position - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            direction = transform.forward;
        }

        direction.Normalize();
        Vector3 impulse = direction * Mathf.Max(0f, knockbackImpulse);
        impulse.y += Mathf.Max(0f, knockbackUpward);
        return impulse;
    }

    private static string BuildRelativePath(Transform root, Transform target)
    {
        if (root == null || target == null)
        {
            return string.Empty;
        }

        if (root == target)
        {
            return string.Empty;
        }

        System.Collections.Generic.Stack<string> names = new System.Collections.Generic.Stack<string>();
        Transform current = target;
        while (current != null && current != root)
        {
            names.Push(current.name);
            current = current.parent;
        }

        if (current != root)
        {
            return string.Empty;
        }

        return string.Join("/", names);
    }
}
