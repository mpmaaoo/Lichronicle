using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玩家外圈探測箱：與背景牆 MoveTrigger 相交時，通知
/// <see cref="FezPlayerBackgroundWallRouter"/> 把玩家推至牆本體淺側。
/// 只做 Overlap 偵測，不參與物理碰撞。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class FezPlayerBackgroundWallRouteProbe : MonoBehaviour
{
    private static readonly Collider[] OverlapBuffer = new Collider[32];

    [SerializeField] private LayerMask moveTriggerMask;

    private BoxCollider _box;
    private readonly HashSet<FezBackgroundWall> _touchingWalls = new HashSet<FezBackgroundWall>(8);

    public bool IsTouchingMoveTrigger(FezBackgroundWall wall) =>
        wall != null && _touchingWalls.Contains(wall);

    public bool IsTouchingAnyMoveTrigger => _touchingWalls.Count > 0;

    public Bounds GetWorldBounds()
    {
        if (_box == null)
            return new Bounds(transform.position, Vector3.one);

        var center = transform.TransformPoint(_box.center);
        var size = Vector3.Scale(_box.size, Abs(transform.lossyScale));
        return new Bounds(center, size);
    }

    public void CopyTouchingWalls(List<FezBackgroundWall> results)
    {
        results.Clear();
        foreach (var wall in _touchingWalls)
        {
            if (wall != null)
                results.Add(wall);
        }
    }

    private void Awake()
    {
        _box = GetComponent<BoxCollider>();
        if (moveTriggerMask.value == 0)
            moveTriggerMask = LayerMask.GetMask("BackGroundWall");

        if (_box != null)
            _box.enabled = false;
    }

    internal void RefreshTouchingWalls()
    {
        _touchingWalls.Clear();
        if (_box == null)
            return;

        var center = transform.TransformPoint(_box.center);
        var halfExtents = Vector3.Scale(_box.size * 0.5f, Abs(transform.lossyScale));
        var count = Physics.OverlapBoxNonAlloc(
            center,
            halfExtents,
            OverlapBuffer,
            Quaternion.identity,
            moveTriggerMask,
            QueryTriggerInteraction.Collide);

        for (var i = 0; i < count; i++)
        {
            var col = OverlapBuffer[i];
            if (col == null || !col.isTrigger)
                continue;

            var moveTrigger = col.GetComponent<FezBackgroundWallMoveTrigger>()
                ?? col.GetComponentInParent<FezBackgroundWallMoveTrigger>();
            var wall = moveTrigger != null ? moveTrigger.OwnerWall : null;
            if (wall != null && wall.isActiveAndEnabled)
                _touchingWalls.Add(wall);
        }
    }

    private static Vector3 Abs(Vector3 v) =>
        new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        var box = _box != null ? _box : GetComponent<BoxCollider>();
        if (box == null)
            return;

        var center = transform.TransformPoint(box.center);
        var ext = Vector3.Scale(box.size * 0.5f, Abs(transform.lossyScale));
        Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.25f);
        Gizmos.DrawCube(center, ext * 2f);
    }
#endif
}
