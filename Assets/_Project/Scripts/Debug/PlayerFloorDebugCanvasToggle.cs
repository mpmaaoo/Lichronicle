// TEMP-DEV-DELETE: 僅供除錯穿透時對照 isGrounded；測試完請刪除此檔並從場景移除掛載。
using UnityEngine;

/// <summary>
/// 【臨時／開發用】依 <see cref="CharacterController.isGrounded"/> 開關指定 <see cref="Canvas"/>（true＝顯示／啟用，false＝關閉）。<br/>
/// 在 <see cref="LateUpdate"/> 更新，盡量貼近本幀所有 <c>Move</c> 之後的落地狀態。
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerFloorDebugCanvasToggle : MonoBehaviour
{
    [SerializeField] private CharacterController characterController;
    [SerializeField] private Canvas floorIndicatorCanvas;

    private void Awake()
    {
        if (characterController == null)
            characterController = GetComponent<CharacterController>();
    }

    private void LateUpdate()
    {
        if (floorIndicatorCanvas == null || characterController == null)
            return;

        floorIndicatorCanvas.enabled = characterController.isGrounded;
    }
}
