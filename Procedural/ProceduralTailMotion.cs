using UnityEngine;

public class ProceduralTailMotion : MonoBehaviour
{
    public Transform[] bones;

    [Header("Auto Scaling")]
    public bool autoScaleToTailLength = true;
    public float referenceTailLength = 3f;
    public float minAutoScale = 0.4f;
    public float maxAutoScale = 1.6f;

    [Header("Side Wiggle")]
    public float amplitude = 20f;
    public float frequency = 2f;

    [Header("Up/Down Motion")]
    public float verticalAmplitude = 5f;
    public float verticalFrequency = 1.5f;

    [Header("Tip Influence")]
    public float tipMultiplier = 2f; // how much stronger the tip moves

    void Update()
    {
        if (bones == null || bones.Length == 0)
        {
            return;
        }

        float autoScale = 1f;
        if (autoScaleToTailLength && referenceTailLength > 0.0001f && bones.Length > 1)
        {
            float tailLength = 0f;
            for (int i = 1; i < bones.Length; i++)
            {
                Transform prev = bones[i - 1];
                Transform cur = bones[i];
                if (prev == null || cur == null)
                {
                    continue;
                }

                tailLength += Vector3.Distance(prev.position, cur.position);
            }

            if (tailLength > 0.0001f)
            {
                autoScale = Mathf.Clamp(tailLength / referenceTailLength, minAutoScale, maxAutoScale);
            }
        }

        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] == null)
            {
                continue;
            }

            float t = (float)i / (bones.Length - 1); // 0 = base, 1 = tip
            float tipFactor = Mathf.Lerp(1f, tipMultiplier, t);

            float offset = i * 0.3f;

            // Side-to-side
            float sideAngle = Mathf.Sin(Time.time * frequency + offset)
                            * (amplitude * autoScale)
                            * tipFactor;

            // Up/down (smaller, different speed)
            float verticalAngle = Mathf.Sin(Time.time * verticalFrequency + offset)
                                * (verticalAmplitude * autoScale)
                                * tipFactor;

            // CHANGE AXIS if needed (Z is common)
            bones[i].localRotation = Quaternion.Euler(verticalAngle, 0, sideAngle);
        }
    }
}
