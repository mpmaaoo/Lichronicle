using UnityEngine;

/// <summary>
/// 掛在「純視覺」子物件（Visual）上，讓貼圖／Quad 朝向鏡頭。
/// 不要掛在 Player 根物件或含 Virtual Camera 的樞紐上。
/// </summary>
[DisallowMultipleComponent]
public sealed class CharacterBillboard : MonoBehaviour
{
    [SerializeField] private Camera targetCamera;
    [Tooltip("只沿 Y 軸轉向，避免角色歪頭。")]
    [SerializeField] private bool lockYAxis = true;
    [Tooltip("FEZ 四向：朝向鎖在 90° 倍數。")]
    [SerializeField] private bool snapTo90Degrees;

    private void LateUpdate()
    {
        var cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam == null)
            return;

        var toCamera = cam.transform.position - transform.position;
        if (lockYAxis)
            toCamera.y = 0f;

        if (toCamera.sqrMagnitude < 0.0001f)
            return;

        // Quad 預設面朝 +Z；用 -toCamera 讓正面朝向鏡頭
        var rot = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);

        if (snapTo90Degrees)
        {
            var y = rot.eulerAngles.y;
            y = Mathf.Round(y / 90f) * 90f;
            rot = Quaternion.Euler(0f, y, 0f);
        }

        transform.rotation = rot;
    }
}
