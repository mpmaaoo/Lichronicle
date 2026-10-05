using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 這一關的三維格子表。四個視角都讀同一張表，不另存四張二維地圖。
/// </summary>
[DisallowMultipleComponent]
public sealed class FezGridMap : MonoBehaviour
{
    private readonly Dictionary<Vector3Int, FezGridBlock> _cells = new Dictionary<Vector3Int, FezGridBlock>();

    public IEnumerable<FezGridBlock> Blocks => _cells.Values;

    public bool HasSolid(Vector3Int cell)
    {
        return _cells.TryGetValue(cell, out var block) && block != null && block.Solid;
    }

    public FezGridBlock AddBlock(Vector3Int cell, Color color, bool solid = true)
    {
        if (_cells.TryGetValue(cell, out var existing))
            return existing;

        var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.name = $"Cell {cell.x},{cell.y},{cell.z}";
        visual.transform.SetParent(transform, false);
        visual.transform.position = (Vector3)cell + Vector3.one * 0.5f;
        visual.transform.localScale = Vector3.one * 0.96f;

        var collider = visual.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);

        var renderer = visual.GetComponent<Renderer>();
        if (renderer != null)
        {
            var shader = Shader.Find("Unlit/Color");
            if (shader != null)
                renderer.sharedMaterial = new Material(shader) { color = color };
        }

        return Adopt(visual, cell, solid);
    }

    /// <summary>
    /// 把地圖編輯器放好的方塊收進同一張格子表。不另做一顆方塊。
    /// </summary>
    public int ImportPlacedBlocks(Transform root)
    {
        if (root == null)
            return 0;

        var count = 0;
        for (var i = 0; i < root.childCount; i++)
        {
            var child = root.GetChild(i);
            if (child == null || child.GetComponentInChildren<Renderer>(true) == null)
                continue;

            var cell = CellFromMinCorner(child.position);
            if (_cells.ContainsKey(cell))
                continue;

            Adopt(child.gameObject, cell, !IsOccluder(child.gameObject));
            count++;
        }

        return count;
    }

    public static Vector3Int CellFromMinCorner(Vector3 worldMinCorner)
    {
        const float epsilon = 0.001f;
        return new Vector3Int(
            Mathf.FloorToInt(worldMinCorner.x + epsilon),
            Mathf.FloorToInt(worldMinCorner.y + epsilon),
            Mathf.FloorToInt(worldMinCorner.z + epsilon));
    }

    private static bool IsOccluder(GameObject visual)
    {
        if (visual == null)
            return false;

        if (visual.GetComponentInChildren<FezBackgroundWall>(true) != null)
            return true;

        return visual.name.IndexOf("Wall", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private FezGridBlock Adopt(GameObject visual, Vector3Int cell, bool solid)
    {
        SilenceLegacyBlock(visual);

        var block = visual.GetComponent<FezGridBlock>();
        if (block == null)
            block = visual.AddComponent<FezGridBlock>();
        block.Initialize(cell, solid);
        _cells.Add(cell, block);
        return block;
    }

    private static void SilenceLegacyBlock(GameObject visual)
    {
        var behaviours = visual.GetComponentsInChildren<MonoBehaviour>(true);
        for (var i = 0; i < behaviours.Length; i++)
        {
            var behaviour = behaviours[i];
            if (behaviour == null || behaviour is FezGridBlock)
                continue;
            behaviour.enabled = false;
        }

        var colliders = visual.GetComponentsInChildren<Collider>(true);
        for (var i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = false;
        }
    }
}
