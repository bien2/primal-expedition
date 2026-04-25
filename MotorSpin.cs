using UnityEngine;

public class MotorSpin : MonoBehaviour
{
    public Transform pivotPoint;  
    public Vector3 axis = Vector3.up;
    public float speed = 1200f;

    [Min(0f)]
    [SerializeField] private float currentSpeed;
    private bool isRunning;
    private float startupSeconds;

    public bool IsAtMaxSpeed => isRunning && currentSpeed >= speed - 0.01f;

    public void StartStartup(float seconds)
    {
        startupSeconds = Mathf.Max(0f, seconds);
        currentSpeed = 0f;
        isRunning = true;
    }

    public void StopMotor()
    {
        isRunning = false;
        currentSpeed = 0f;
        startupSeconds = 0f;
    }

    private void Update()
    {
        if (pivotPoint == null) return;
        if (!isRunning) return;

        float max = Mathf.Max(0f, speed);
        if (startupSeconds <= 0.0001f)
        {
            currentSpeed = max;
        }
        else
        {
            float accel = max / startupSeconds;
            currentSpeed = Mathf.MoveTowards(currentSpeed, max, accel * Time.deltaTime);
        }

        transform.RotateAround(
            pivotPoint.position,
            pivotPoint.TransformDirection(axis.normalized),
            currentSpeed * Time.deltaTime
        );
    }
}
