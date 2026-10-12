using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玩家目前深度上的碰撞：平台頂板可踩；遮片牆與邊界牆為完整 1×1×1。
/// 繞行進行中會暫時拿掉遮片牆，讓玩家穿過去。
/// </summary>
[DisallowMultipleComponent]
public sealed class FezGridShell : MonoBehaviour
{
    private readonly List<BoxCollider> _boxes = new List<BoxCollider>(32);
    private readonly List<FezGridBlock> _all = new List<FezGridBlock>(64);

    public void RebuildForDepth(
        FezGridMap map,
        FezGridProjection.Axes axes,
        float depth,
        FezGridBlock also = null,
        bool suppressOccluders = false)
    {
        _all.Clear();
        if (map != null)
        {
            FezGridProjection.RefreshShallowest(map, axes);

            foreach (var block in map.Blocks)
            {
                if (block == null)
                    continue;

                var blockDepth = FezGridProjection.DepthOf(block.Center, axes);
                if (Mathf.Abs(blockDepth - depth) > 0.55f)
                    continue;

                // 遮片牆：同深度硬擋；繞行中暫時不進 Shell。
                if (block.IsOccluderWall)
                {
                    if (!suppressOccluders)
                        _all.Add(block);
                    continue;
                }

                if (block.Solid)
                {
                    // 下落穿過：暫時拿掉該塊頂板，才能掉下去並繞過。
                    if (FezGridProjection.IsIgnoredFloor(block))
                        continue;

                    _all.Add(block);
                    continue;
                }

                // 邊界牆：與玩家同深度層完整格硬擋。
                if (block.IsBoundaryWall)
                    _all.Add(block);
            }
        }

        if (also != null
            && also.Solid
            && !FezGridProjection.IsIgnoredFloor(also)
            && !_all.Contains(also))
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

            var block = visible[i];
            var center = block.Center;
            if (block.IsBoundaryWall || block.IsOccluderWall)
            {
                box.transform.position = center;
                box.size = Vector3.one;
            }
            else
            {
                // 只留頂板碰撞：頂面可踩，側面不擋，改由深度繞行處理。
                box.transform.position = new Vector3(center.x, block.TopY - 0.1f, center.z);
                box.size = new Vector3(1f, 0.2f, 1f);
            }
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
