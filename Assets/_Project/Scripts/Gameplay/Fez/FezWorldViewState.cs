using UnityEngine;

/// <summary>
/// FEZ 式「四向視角」的深度軸來源（不依賴 Camera.forward）。
/// 規則：讀取 <see cref="viewPivot"/> 的 Y 旋轉，四捨五入到 90° 倍數，對應四條世界水平深度軸之一。
///
/// 對應（與 Unity 預設 forward=+Z 一致）：
/// - Y ≈ 0°   → 深度軸 +Z（北）
/// - Y ≈ 90°  → 深度軸 +X（東）
/// - Y ≈ 180° → 深度軸 -Z（南）
/// - Y ≈ 270° → 深度軸 -X（西）
/// </summary>
[DisallowMultipleComponent]
public sealed class FezWorldViewState : MonoBehaviour
{
    /// <summary>場景中主要使用的實例（多個時以最後 Enable 的為準）。</summary>
    public static FezWorldViewState Active { get; private set; }

    [Tooltip("與視角旋轉技能相同的樞紐（例如 Player）。留空則用本物件 Transform。")]
    [SerializeField] private Transform viewPivot;

    private Vector3 _depthAxisWorld = Vector3.forward;

    /// <summary>目前視角下的「深度方向」（世界座標、水平、單位向量）。</summary>
    public Vector3 DepthAxisWorld => _depthAxisWorld;

    private void OnEnable()
    {
        Active = this;
        if (viewPivot == null)
            viewPivot = transform;
        RefreshFromPivot();
    }

    private void OnDisable()
    {
        if (Active == this)
            Active = null;
    }

    private void Awake()
    {
        if (viewPivot == null)
            viewPivot = transform;
        RefreshFromPivot();
    }

    /// <summary>依樞紐當前 Y 角更新四向深度軸（請在視角旋轉「結束」後呼叫）。</summary>
    public void RefreshFromPivot()
    {
        if (viewPivot == null)
            return;

        var y = viewPivot.eulerAngles.y;
        var snappedY = Mathf.Round(y / 90f) * 90f;
        var norm = Mathf.Repeat(snappedY, 360f);
        var q = Mathf.RoundToInt(norm / 90f) % 4;

        _depthAxisWorld = QuadrantToDepthAxis(q);
    }

    private static Vector3 QuadrantToDepthAxis(int q)
    {
        switch (q)
        {
            case 0: return new Vector3(0f, 0f, 1f);  // +Z
            case 1: return new Vector3(1f, 0f, 0f);  // +X
            case 2: return new Vector3(0f, 0f, -1f); // -Z
            default: return new Vector3(-1f, 0f, 0f); // -X
        }
    }
}
