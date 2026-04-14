using UnityEngine;

public class DinoDespawnPoint : MonoBehaviour
{
    public static Transform Point { get; private set; }

    private void Awake()
    {
        Point = transform;
    }

    private void OnDisable()
    {
        if (Point == transform)
        {
            Point = null;
        }
    }
}

