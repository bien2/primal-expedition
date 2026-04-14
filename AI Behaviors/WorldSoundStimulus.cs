using System;
using UnityEngine;

public static class WorldSoundStimulus
{
    public static event Action<Vector3, float> Emitted;

    public static void Emit(Vector3 position, float radius)
    {
        if (radius <= 0f)
        {
            return;
        }

        Emitted?.Invoke(position, radius);
    }
}
