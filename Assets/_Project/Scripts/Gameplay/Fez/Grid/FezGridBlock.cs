using UnityEngine;

/// <summary>
/// 一格 1×1×1 方塊。Cell 是最小角的整數座標，方塊佔 [x, x+1) × [y, y+1) × [z, z+1)。
/// </summary>
[DisallowMultipleComponent]
public sealed class FezGridBlock : MonoBehaviour
{
    [SerializeField] private Vector3Int cell;
    [SerializeField] private bool cellAssigned;

    /// <summary>格子最小角。有寫入過才可當來源；否則仍由世界位置換算。</summary>
    public Vector3Int Cell => cell;

    public bool HasCell => cellAssigned;

    public FezGridBlockKind Kind { get; private set; }

    /// <summary>可踩的實體平台。</summary>
    public bool Solid => Kind == FezGridBlockKind.Platform;

    public bool IsBoundaryWall => Kind == FezGridBlockKind.BoundaryWall;

    public bool IsOccluderWall => Kind == FezGridBlockKind.Occluder;

    public Vector3 Center => (Vector3)Cell + Vector3.one * 0.5f;
    public float TopY => Cell.y + 1f;

    public void AssignCell(Vector3Int value)
    {
        cell = value;
        cellAssigned = true;
    }

    public void Initialize(Vector3Int value, FezGridBlockKind kind)
    {
        AssignCell(value);
        Kind = kind;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!cellAssigned || Application.isPlaying)
            return;

        var min = (Vector3)cell;
        if ((transform.position - min).sqrMagnitude < 1e-8f)
            return;

        transform.position = min;
    }
#endif

    public void Initialize(Vector3Int cell, bool solid)
    {
        Initialize(cell, solid ? FezGridBlockKind.Platform : FezGridBlockKind.Occluder);
    }
}
