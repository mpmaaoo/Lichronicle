using UnityEngine;

/// <summary>
/// 背景牆子物件的移動碰撞箱（Is Trigger）。跟隨玩家深度，但不得比本體更淺。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class FezBackgroundWallMoveTrigger : MonoBehaviour
{
    [SerializeField] private FezBackgroundWall ownerWall;

    private Collider _collider;

    public FezBackgroundWall OwnerWall => ownerWall;
    public Collider TriggerCollider => _collider;

    private void Awake()
    {
        _collider = GetComponent<Collider>();
        if (ownerWall == null)
            ownerWall = GetComponentInParent<FezBackgroundWall>();
    }

    private void OnEnable()
    {
        if (ownerWall == null)
            ownerWall = GetComponentInParent<FezBackgroundWall>();
        if (ownerWall != null)
            ownerWall.RegisterMoveTrigger(this);
    }

    private void OnDisable()
    {
        if (ownerWall != null)
            ownerWall.UnregisterMoveTrigger(this);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        var col = GetComponent<Collider>();
        if (col != null && !col.isTrigger)
            Debug.LogWarning($"{name} 的 Collider 應設為 Is Trigger。", this);
    }
#endif
}
