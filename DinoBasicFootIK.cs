using UnityEngine;

/// <summary>
/// Very basic foot IK/grounding for non-humanoid rigs.
/// Layers on top of animation in LateUpdate by nudging foot bones toward ground hits.
/// </summary>
public class DinoBasicFootIK : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform leftFoot;   // Ankle.L
    [SerializeField] private Transform rightFoot;  // Ankle.R

    [Header("Grounding")]
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private float rayStartHeight = 0.7f;
    [SerializeField] private float rayLength = 2.0f;
    [SerializeField] private float footHeightOffset = 0.03f;
    [SerializeField, Range(0f, 1f)] private float ikWeight = 0.75f;
    [SerializeField] private float positionSmooth = 12f;

    [Header("Debug")]
    [SerializeField] private bool showDebugRays = false;

    private Vector3 _leftAnimatedLocalPos;
    private Vector3 _rightAnimatedLocalPos;
    private Transform _leftParent;
    private Transform _rightParent;

    private void Awake()
    {
        if (leftFoot != null) _leftParent = leftFoot.parent;
        if (rightFoot != null) _rightParent = rightFoot.parent;
    }

    private void LateUpdate()
    {
        if (leftFoot == null || rightFoot == null || _leftParent == null || _rightParent == null)
        {
            return;
        }

        // Capture animated pose first (already evaluated by Animator this frame).
        _leftAnimatedLocalPos = leftFoot.localPosition;
        _rightAnimatedLocalPos = rightFoot.localPosition;

        Vector3 leftTargetWorld;
        bool leftGrounded = TryGetGroundPoint(leftFoot.position, out leftTargetWorld);

        Vector3 rightTargetWorld;
        bool rightGrounded = TryGetGroundPoint(rightFoot.position, out rightTargetWorld);

        ApplyFootPosition(leftFoot, _leftParent, _leftAnimatedLocalPos, leftGrounded, leftTargetWorld);
        ApplyFootPosition(rightFoot, _rightParent, _rightAnimatedLocalPos, rightGrounded, rightTargetWorld);
    }

    private bool TryGetGroundPoint(Vector3 footWorldPos, out Vector3 targetPoint)
    {
        Vector3 origin = footWorldPos + Vector3.up * rayStartHeight;
        Ray ray = new Ray(origin, Vector3.down);

        if (Physics.Raycast(ray, out RaycastHit hit, rayLength, groundMask, QueryTriggerInteraction.Ignore))
        {
            targetPoint = hit.point + Vector3.up * footHeightOffset;

            if (showDebugRays)
            {
                Debug.DrawLine(origin, hit.point, Color.green);
            }

            return true;
        }

        targetPoint = footWorldPos;
        if (showDebugRays)
        {
            Debug.DrawRay(origin, Vector3.down * rayLength, Color.red);
        }
        return false;
    }

    private void ApplyFootPosition(
        Transform foot,
        Transform footParent,
        Vector3 animatedLocalPos,
        bool grounded,
        Vector3 groundTargetWorld)
    {
        Vector3 desiredLocalPos = animatedLocalPos;

        if (grounded)
        {
            Vector3 localGround = footParent.InverseTransformPoint(groundTargetWorld);
            desiredLocalPos = Vector3.Lerp(animatedLocalPos, localGround, ikWeight);
        }

        float t = 1f - Mathf.Exp(-positionSmooth * Time.deltaTime);
        foot.localPosition = Vector3.Lerp(foot.localPosition, desiredLocalPos, t);
    }
}
