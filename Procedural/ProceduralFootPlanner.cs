using System;
using UnityEngine;
using UnityEngine.AI;

[Serializable]
public class ProceduralFootPlannerLeg
{
    public string name = "Leg";
    public Transform target;
    public Vector3 homeOffset = Vector3.zero;
    [Range(0f, 1f)] public float phaseOffset;

    [NonSerialized] public Vector3 currentPosition;
    [NonSerialized] public Vector3 plantedPosition;
    [NonSerialized] public Vector3 plantedNormal = Vector3.up;
    [NonSerialized] public bool initialized;
}

/// <summary>
/// Drives foot target transforms from movement so rig constraints can solve the actual legs.
/// Attach this to the moving dinosaur root and assign one target transform per foot.
/// </summary>
[DefaultExecutionOrder(150)]
public class ProceduralFootPlanner : MonoBehaviour
{
    private const float IdleBlendSpeed = 12f;
    private const float StepDurationSpeedupMultiplier = 0.65f;

    [Header("Rig")]
    [SerializeField] private Transform bodyRoot;
    [SerializeField] private ProceduralFootPlannerLeg[] legs = new ProceduralFootPlannerLeg[0];

    [Header("Step")]
    [SerializeField] private float maxSpeed = 6f;
    [SerializeField] private float moveThreshold = 0.03f;
    [SerializeField] private float stepDistance = 0.22f;
    [SerializeField] private float stepHeight = 0.08f;
    [SerializeField] private float stepDuration = 0.16f;
    [SerializeField] private float stepOvershoot = 0.04f;
    [SerializeField] private float stepTriggerDistance = 0.12f;
    [SerializeField] private float maxFootReachFromHome = 0.3f;
    [SerializeField] private int maxSimultaneousSteps = 1;

    [Header("Ground")]
    [SerializeField] private LayerMask groundLayers = ~0;
    [SerializeField] private float groundRayStartHeight = 1f;
    [SerializeField] private float groundRayLength = 2f;
    [SerializeField] private float footGroundOffset = 0.01f;

    [Header("Debug")]
    [SerializeField] private bool drawDebug = true;
    [SerializeField] private Color homeColor = new Color(0.2f, 0.9f, 1f, 0.9f);
    [SerializeField] private Color plantedColor = new Color(0.2f, 1f, 0.2f, 0.9f);
    [SerializeField] private Color targetColor = new Color(1f, 0.85f, 0.2f, 0.9f);

    private Rigidbody cachedRigidbody;
    private NavMeshAgent cachedAgent;
    private Vector3 lastBodyPosition;
    private StepState[] activeSteps = Array.Empty<StepState>();
    private bool initialized;

    private struct StepState
    {
        public bool active;
        public float startTime;
        public float duration;
        public Vector3 startPosition;
        public Vector3 endPosition;
        public Vector3 endNormal;
    }

    private void Awake()
    {
        if (bodyRoot == null)
        {
            bodyRoot = transform;
        }

        cachedRigidbody = GetComponent<Rigidbody>();
        cachedAgent = GetComponent<NavMeshAgent>();
        InitializeLegs();
    }

    private void OnEnable()
    {
        InitializeLegs();
    }

    private void OnValidate()
    {
        if (bodyRoot == null)
        {
            bodyRoot = transform;
        }

        maxSpeed = Mathf.Max(0.01f, maxSpeed);
        moveThreshold = Mathf.Max(0f, moveThreshold);
        stepDistance = Mathf.Max(0f, stepDistance);
        stepHeight = Mathf.Max(0f, stepHeight);
        stepDuration = Mathf.Max(0.05f, stepDuration);
        stepOvershoot = Mathf.Max(0f, stepOvershoot);
        stepTriggerDistance = Mathf.Max(0.01f, stepTriggerDistance);
        maxFootReachFromHome = Mathf.Max(0.01f, maxFootReachFromHome);
        maxSimultaneousSteps = Mathf.Max(1, maxSimultaneousSteps);
        groundRayStartHeight = Mathf.Max(0.01f, groundRayStartHeight);
        groundRayLength = Mathf.Max(0.05f, groundRayLength);
        footGroundOffset = Mathf.Max(0f, footGroundOffset);
    }

    private void LateUpdate()
    {
        if (!initialized)
        {
            InitializeLegs();
            if (!initialized)
            {
                return;
            }
        }

        Vector3 planarVelocity = GetPlanarVelocity();
        float speed = planarVelocity.magnitude;
        bool isMoving = speed > Mathf.Max(0.001f, moveThreshold);
        float normalizedSpeed = maxSpeed > 0f ? Mathf.Clamp01(speed / maxSpeed) : 0f;

        UpdateActiveSteps();
        if (isMoving)
        {
            QueueSteps(planarVelocity, normalizedSpeed);
        }
        else
        {
            PlantIdleFeet();
        }

        ApplyTargets();
        lastBodyPosition = bodyRoot.position;
    }

    private void InitializeLegs()
    {
        initialized = legs != null && legs.Length > 0;
        if (!initialized)
        {
            return;
        }

        activeSteps = new StepState[legs.Length];
        for (int i = 0; i < legs.Length; i++)
        {
            ProceduralFootPlannerLeg leg = legs[i];
            if (leg == null || leg.target == null)
            {
                initialized = false;
                return;
            }

            if (leg.homeOffset.sqrMagnitude <= 0.0001f)
            {
                leg.homeOffset = bodyRoot.InverseTransformPoint(leg.target.position);
            }

            Vector3 homeWorld = bodyRoot.TransformPoint(leg.homeOffset);
            if (TryProjectToGround(homeWorld, out Vector3 grounded, out Vector3 normal))
            {
                leg.plantedPosition = grounded + normal * footGroundOffset;
                leg.plantedNormal = normal;
            }
            else
            {
                leg.plantedPosition = leg.target.position;
                leg.plantedNormal = Vector3.up;
            }

            leg.currentPosition = leg.plantedPosition;
            leg.initialized = true;
        }

        lastBodyPosition = bodyRoot.position;
    }

    private Vector3 GetPlanarVelocity()
    {
        Vector3 bestVelocity = Vector3.zero;

        if (cachedAgent != null && cachedAgent.enabled)
        {
            Vector3 velocity = cachedAgent.velocity;
            velocity.y = 0f;
            if (velocity.sqrMagnitude > bestVelocity.sqrMagnitude)
            {
                bestVelocity = velocity;
            }
        }

        if (cachedRigidbody != null && cachedRigidbody.gameObject.activeInHierarchy)
        {
            Vector3 velocity = cachedRigidbody.linearVelocity;
            velocity.y = 0f;
            if (velocity.sqrMagnitude > bestVelocity.sqrMagnitude)
            {
                bestVelocity = velocity;
            }
        }

        Vector3 deltaVelocity = (bodyRoot.position - lastBodyPosition) / Mathf.Max(Time.deltaTime, 0.0001f);
        deltaVelocity.y = 0f;
        if (deltaVelocity.sqrMagnitude > bestVelocity.sqrMagnitude)
        {
            bestVelocity = deltaVelocity;
        }

        return bestVelocity;
    }

    private void UpdateActiveSteps()
    {
        for (int i = 0; i < legs.Length; i++)
        {
            ProceduralFootPlannerLeg leg = legs[i];
            if (leg == null || !leg.initialized)
            {
                continue;
            }

            StepState step = activeSteps[i];
            if (!step.active)
            {
                leg.currentPosition = leg.plantedPosition;
                continue;
            }

            float t = Mathf.Clamp01((Time.time - step.startTime) / Mathf.Max(0.01f, step.duration));
            Vector3 pos = Vector3.Lerp(step.startPosition, step.endPosition, t);
            pos.y += Mathf.Sin(t * Mathf.PI) * Mathf.Max(0f, stepHeight);
            leg.currentPosition = ClampToHomeReach(leg, pos);

            if (t >= 1f)
            {
                leg.plantedPosition = ClampToHomeReach(leg, step.endPosition);
                leg.plantedNormal = step.endNormal;
                leg.currentPosition = leg.plantedPosition;
                step.active = false;
                activeSteps[i] = step;
            }
        }
    }

    private void QueueSteps(Vector3 planarVelocity, float normalizedSpeed)
    {
        if (GetActiveStepCount() >= Mathf.Max(1, maxSimultaneousSteps))
        {
            return;
        }

        Vector3 forward = ResolveForward(planarVelocity);

        int bestLegIndex = -1;
        float bestDistance = Mathf.Max(0.01f, stepTriggerDistance);

        for (int i = 0; i < legs.Length; i++)
        {
            if (activeSteps[i].active)
            {
                continue;
            }

            ProceduralFootPlannerLeg leg = legs[i];
            Vector3 desired = GetDesiredFootPlacement(leg, forward, normalizedSpeed);
            float distance = Vector3.Distance(leg.plantedPosition, desired);
            if (distance > bestDistance)
            {
                bestDistance = distance;
                bestLegIndex = i;
            }
        }

        if (bestLegIndex < 0)
        {
            return;
        }

        ProceduralFootPlannerLeg bestLeg = legs[bestLegIndex];
        Vector3 end = GetDesiredFootPlacement(bestLeg, forward, normalizedSpeed);
        Vector3 endNormal = Vector3.up;
        if (TryProjectToGround(end, out Vector3 groundedEnd, out endNormal))
        {
            end = groundedEnd + endNormal * footGroundOffset;
        }

        end = ClampToHomeReach(bestLeg, end);

        float durationScale = Mathf.Lerp(1f, StepDurationSpeedupMultiplier, Mathf.Clamp01(normalizedSpeed));

        activeSteps[bestLegIndex] = new StepState
        {
            active = true,
            startTime = Time.time,
            duration = Mathf.Max(0.05f, stepDuration * durationScale),
            startPosition = bestLeg.plantedPosition,
            endPosition = end,
            endNormal = endNormal
        };
    }

    private Vector3 GetDesiredFootPlacement(ProceduralFootPlannerLeg leg, Vector3 forward, float normalizedSpeed)
    {
        Vector3 homeWorld = bodyRoot.TransformPoint(leg.homeOffset);
        float phasePush = Mathf.Sin((Time.time * Mathf.Max(0.01f, 1f / Mathf.Max(0.01f, stepDuration)) + leg.phaseOffset) * Mathf.PI * 2f);
        float travel = Mathf.Max(0f, stepDistance) * Mathf.Max(0.2f, normalizedSpeed);
        Vector3 desired = homeWorld + forward * ((travel * 0.5f) + (phasePush * stepOvershoot));
        return ClampToHomeReach(leg, desired);
    }

    private void PlantIdleFeet()
    {
        for (int i = 0; i < legs.Length; i++)
        {
            if (activeSteps[i].active)
            {
                continue;
            }

            ProceduralFootPlannerLeg leg = legs[i];
            Vector3 homeWorld = bodyRoot.TransformPoint(leg.homeOffset);
            if (TryProjectToGround(homeWorld, out Vector3 grounded, out Vector3 normal))
            {
                leg.plantedPosition = ClampToHomeReach(leg, grounded + normal * footGroundOffset);
                leg.plantedNormal = normal;
                float t = 1f - Mathf.Exp(-IdleBlendSpeed * Time.deltaTime);
                leg.currentPosition = Vector3.Lerp(leg.currentPosition, leg.plantedPosition, t);
            }
        }
    }

    private void ApplyTargets()
    {
        for (int i = 0; i < legs.Length; i++)
        {
            ProceduralFootPlannerLeg leg = legs[i];
            if (leg == null || leg.target == null || !leg.initialized)
            {
                continue;
            }

            leg.target.position = leg.currentPosition;
        }
    }

    private int GetActiveStepCount()
    {
        int count = 0;
        for (int i = 0; i < activeSteps.Length; i++)
        {
            if (activeSteps[i].active)
            {
                count++;
            }
        }
        return count;
    }

    private Vector3 ResolveForward(Vector3 planarVelocity)
    {
        Vector3 forward = planarVelocity.sqrMagnitude > 0.0001f ? planarVelocity.normalized : bodyRoot.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.0001f)
        {
            forward = bodyRoot.forward;
            forward.y = 0f;
        }
        if (forward.sqrMagnitude <= 0.0001f)
        {
            forward = Vector3.forward;
        }

        return forward.normalized;
    }

    private Vector3 ClampToHomeReach(ProceduralFootPlannerLeg leg, Vector3 worldPoint)
    {
        Vector3 homeWorld = bodyRoot.TransformPoint(leg.homeOffset);
        float maxReach = Mathf.Max(0.01f, maxFootReachFromHome);
        Vector3 offset = worldPoint - homeWorld;
        if (offset.sqrMagnitude <= maxReach * maxReach)
        {
            return worldPoint;
        }

        return homeWorld + offset.normalized * maxReach;
    }

    private bool TryProjectToGround(Vector3 worldPoint, out Vector3 groundedPoint, out Vector3 groundedNormal)
    {
        Vector3 origin = worldPoint + Vector3.up * groundRayStartHeight;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, groundRayLength, groundLayers, QueryTriggerInteraction.Ignore))
        {
            groundedPoint = hit.point;
            groundedNormal = hit.normal;
            return true;
        }

        groundedPoint = worldPoint;
        groundedNormal = Vector3.up;
        return false;
    }

    [ContextMenu("Capture Home Offsets From Targets")]
    public void CaptureHomeOffsetsFromTargets()
    {
        if (bodyRoot == null)
        {
            bodyRoot = transform;
        }

        if (legs == null)
        {
            return;
        }

        for (int i = 0; i < legs.Length; i++)
        {
            ProceduralFootPlannerLeg leg = legs[i];
            if (leg == null || leg.target == null)
            {
                continue;
            }

            leg.homeOffset = bodyRoot.InverseTransformPoint(leg.target.position);
        }

        initialized = false;
        InitializeLegs();
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawDebug || legs == null)
        {
            return;
        }

        Transform root = bodyRoot != null ? bodyRoot : transform;
        for (int i = 0; i < legs.Length; i++)
        {
            ProceduralFootPlannerLeg leg = legs[i];
            if (leg == null)
            {
                continue;
            }

            Vector3 homeWorld = root.TransformPoint(leg.homeOffset);
            Gizmos.color = homeColor;
            Gizmos.DrawWireSphere(homeWorld, 0.04f);

            Gizmos.color = plantedColor;
            Gizmos.DrawSphere(leg.plantedPosition, 0.03f);

            Gizmos.color = targetColor;
            if (leg.target != null)
            {
                Gizmos.DrawLine(homeWorld, leg.target.position);
                Gizmos.DrawSphere(leg.target.position, 0.03f);
            }
        }
    }
}
