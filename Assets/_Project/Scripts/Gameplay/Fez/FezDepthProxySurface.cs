using UnityEngine;

/// <summary>
/// 掛在 DepthProxy（透明碰撞箱）上，用來提供「要對齊的平台本體深度」。
/// 若需要由下往上穿過、站上頂面：再加掛 <see cref="FezOneWayPlatform"/>（單向平台）。
/// </summary>
[DisallowMultipleComponent]
public sealed class FezDepthProxySurface : MonoBehaviour
{
    [Tooltip("平台本體的深度參考點（例如本體中心或頂面中心）。有填就用它。")]
    [SerializeField] private Transform platformDepthReference;

    public Vector3 GetPlatformDepthWorldPos()
    {
        if (platformDepthReference != null)
            return platformDepthReference.position;

        // 自動：找父物件下的 Collider/Renderer，排除自己的子物件（DepthProxy 本身）
        if (transform.parent != null)
        {
            var parent = transform.parent;

            // 找父物件直系子物件中，不屬於 DepthProxy 節點的 Collider
            foreach (Transform child in parent)
            {
                if (child == transform)
                    continue;
                var col = child.GetComponent<Collider>();
                if (col != null)
                    return col.bounds.center;
            }

            // 再找 Renderer
            foreach (Transform child in parent)
            {
                if (child == transform)
                    continue;
                var rend = child.GetComponent<Renderer>();
                if (rend != null)
                    return rend.bounds.center;
            }

            return parent.position;
        }
        return transform.position;
    }

    /// <summary>此平台用於繞行時暫停深度傳送的判定區（可掛在同平台任意子物件）。</summary>
    public FezPlatformDepthSnapSuppressZone GetDepthSnapSuppressZone()
    {
        var root = transform.parent != null ? transform.parent : transform;
        var zone = GetComponent<FezPlatformDepthSnapSuppressZone>();
        if (zone != null)
            return zone;

        zone = GetComponentInChildren<FezPlatformDepthSnapSuppressZone>(true);
        if (zone != null)
            return zone;

        return root.GetComponentInChildren<FezPlatformDepthSnapSuppressZone>(true);
    }
}

