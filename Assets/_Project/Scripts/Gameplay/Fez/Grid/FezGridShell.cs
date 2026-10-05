using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 只給玩家目前這層深度上的實體方塊碰撞盒。
/// 比較前或比較後的方塊看得到，但不擋路。
/// </summary>
[DisallowMultipleComponent]
public sealed class FezGridShell : MonoBehaviour
{
    private readonly List<BoxCollider> _boxes = new List<BoxCollider>(32);
    private readonly List<FezGridBlock> _all = new List<FezGridBlock>(64);

    public void RebuildForDepth(FezGridMap map, FezGridProjection.Axes axes, float depth, FezGridBlock also = null)
    {
        _all.Clear();
        if (map != null)
        {
            foreach (var block in map.Blocks)
            {
                if (block == null || !block.Solid)
                    continue;

                // 下落穿過：暫時拿掉該塊頂板，才能掉下去並繞過。
                if (FezGridProjection.IsIgnoredFloor(block))
                    continue;

                var blockDepth = FezGridProjection.DepthOf(block.Center, axes);
                if (Mathf.Abs(blockDepth - depth) > 0.55f)
                    continue;

                _all.Add(block);
            }
        }

        if (also != null && also.Solid && !FezGridProjection.IsIgnoredFloor(also) && !_all.Contains(also))
            _all.Add(also);

        Rebuild(_all);
    }

    /// <summary>
    /// 繞牆時清空碰撞。淺側整層或舊深度支撐都可能把人擦邊卡住；高度改由玩家自己鎖。
    /// </summary>
    public void ClearColliders()
    {
        _all.Clear();
        Rebuild(_all);
    }

    public void Rebuild(List<FezGridBlock> visible)
    {
        var count = visible != null ? visible.Count : 0;
        while (_boxes.Count < count)
            _boxes.Add(CreateBox(_boxes.Count));

        for (var i = 0; i < _boxes.Count; i++)
        {
            var box = _boxes[i];
            var active = i < count && visible[i] != null;
            box.gameObject.SetActive(active);
            if (!active)
                continue;

            // 只留頂板碰撞：頂面可踩，側面不擋，改由深度繞行處理。
            var block = visible[i];
            var center = block.Center;
            box.transform.position = new Vector3(center.x, block.TopY - 0.1f, center.z);
            box.size = new Vector3(1f, 0.2f, 1f);
        }
    }

    private BoxCollider CreateBox(int index)
    {
        var go = new GameObject($"Shell {index}");
        go.transform.SetParent(transform, false);
        var box = go.AddComponent<BoxCollider>();
        go.SetActive(false);
        return box;
    }
}
