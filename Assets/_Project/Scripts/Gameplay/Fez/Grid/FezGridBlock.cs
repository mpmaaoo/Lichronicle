using UnityEngine;

/// <summary>
/// 一格 1×1×1 方塊。Cell 是最小角的整數座標，方塊佔 [x, x+1) × [y, y+1) × [z, z+1)。
/// </summary>
[DisallowMultipleComponent]
public sealed class FezGridBlock : MonoBehaviour
{
    public Vector3Int Cell { get; private set; }
    public bool Solid { get; private set; }

    public Vector3 Center => (Vector3)Cell + Vector3.one * 0.5f;
    public float TopY => Cell.y + 1f;

    public void Initialize(Vector3Int cell, bool solid)
    {
        Cell = cell;
        Solid = solid;
    }
}
