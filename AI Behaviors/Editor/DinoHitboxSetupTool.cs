using UnityEditor;
using UnityEngine;

public static class DinoHitboxSetupTool
{
    private const string HitboxesRootName = "Hitboxes";

    [MenuItem("Tools/Dino/Generate Default Hitboxes")]
    private static void GenerateDefaultHitboxes()
    {
        if (Selection.gameObjects == null || Selection.gameObjects.Length == 0)
        {
            Debug.LogWarning("Select at least one dino root object.");
            return;
        }

        Undo.SetCurrentGroupName("Generate Dino Hitboxes");
        int group = Undo.GetCurrentGroup();

        for (int i = 0; i < Selection.gameObjects.Length; i++)
        {
            GameObject root = Selection.gameObjects[i];
            if (root == null)
            {
                continue;
            }

            CreateDefaultHitboxes(root);
        }

        Undo.CollapseUndoOperations(group);
    }

    private static void CreateDefaultHitboxes(GameObject root)
    {
        Transform hitboxesRoot = root.transform.Find(HitboxesRootName);
        if (hitboxesRoot == null)
        {
            GameObject go = new GameObject(HitboxesRootName);
            Undo.RegisterCreatedObjectUndo(go, "Create Hitboxes Root");
            go.transform.SetParent(root.transform, false);
            hitboxesRoot = go.transform;
        }

        int targetLayer = LayerMask.NameToLayer("CanExtract");
        if (targetLayer < 0)
        {
            targetLayer = root.layer;
        }

        CreateBodyCapsule(hitboxesRoot, targetLayer);
        CreateHeadBox(hitboxesRoot, targetLayer);
        CreateTailBox(hitboxesRoot, targetLayer);
        CreateLeftLegBox(hitboxesRoot, targetLayer);
        CreateRightLegBox(hitboxesRoot, targetLayer);
    }

    private static void CreateBodyCapsule(Transform parent, int layer)
    {
        GameObject go = GetOrCreateChild(parent, "HB_Body");
        go.layer = layer;
        CapsuleCollider c = GetOrAdd<CapsuleCollider>(go);
        c.isTrigger = true;
        c.direction = 2;
        c.center = new Vector3(0f, 1.0f, -0.15f);
        c.radius = 0.55f;
        c.height = 2.5f;
    }

    private static void CreateHeadBox(Transform parent, int layer)
    {
        GameObject go = GetOrCreateChild(parent, "HB_HeadNeck");
        go.layer = layer;
        BoxCollider c = GetOrAdd<BoxCollider>(go);
        c.isTrigger = true;
        c.center = new Vector3(0f, 1.35f, 1.25f);
        c.size = new Vector3(0.8f, 0.7f, 1.2f);
    }

    private static void CreateTailBox(Transform parent, int layer)
    {
        GameObject go = GetOrCreateChild(parent, "HB_Tail");
        go.layer = layer;
        BoxCollider c = GetOrAdd<BoxCollider>(go);
        c.isTrigger = true;
        c.center = new Vector3(0f, 0.85f, -1.9f);
        c.size = new Vector3(0.65f, 0.5f, 1.7f);
    }

    private static void CreateLeftLegBox(Transform parent, int layer)
    {
        GameObject go = GetOrCreateChild(parent, "HB_LegL");
        go.layer = layer;
        BoxCollider c = GetOrAdd<BoxCollider>(go);
        c.isTrigger = true;
        c.center = new Vector3(-0.32f, 0.45f, -0.15f);
        c.size = new Vector3(0.35f, 0.9f, 0.45f);
    }

    private static void CreateRightLegBox(Transform parent, int layer)
    {
        GameObject go = GetOrCreateChild(parent, "HB_LegR");
        go.layer = layer;
        BoxCollider c = GetOrAdd<BoxCollider>(go);
        c.isTrigger = true;
        c.center = new Vector3(0.32f, 0.45f, -0.15f);
        c.size = new Vector3(0.35f, 0.9f, 0.45f);
    }

    private static GameObject GetOrCreateChild(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child != null)
        {
            return child.gameObject;
        }

        GameObject go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create Hitbox");
        go.transform.SetParent(parent, false);
        return go;
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T existing = go.GetComponent<T>();
        if (existing != null)
        {
            return existing;
        }

        return Undo.AddComponent<T>(go);
    }
}
