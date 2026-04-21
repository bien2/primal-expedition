using UnityEngine;

public static class DinoDespawnService
{
    public static Transform Point { get; private set; }

    public static void SetPoint(Transform point)
    {
        Point = point;
    }

    public static void ClearPoint(Transform point)
    {
        if (Point == point)
        {
            Point = null;
        }
    }
}

