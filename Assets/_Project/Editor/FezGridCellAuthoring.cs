#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 把格子最小角寫進方塊上的 <see cref="FezGridBlock"/>。位置跟著這個整數走。
/// </summary>
public static class FezGridCellAuthoring
{
    [MenuItem("Lichronicle/Map/將座標寫入方塊", false, 20)]
    public static void WriteOpenMap()
    {
        var root = GameObject.Find("FezMapRoot");
        if (root == null)
        {
            EditorUtility.DisplayDialog("將座標寫入方塊", "場景裡沒有 FezMapRoot。", "OK");
            return;
        }

        var transforms = new List<Transform>(root.transform.childCount);
        for (var i = 0; i < root.transform.childCount; i++)
            transforms.Add(root.transform.GetChild(i));

        var count = WriteMapRoot(transforms, "Write Block Cells");
        Debug.Log($"[FezGridCell] 已寫入 {count} 個方塊。請存檔場景。");
    }

    public static void WriteCell(GameObject blockRoot, Vector3Int cell)
    {
        if (blockRoot == null)
            return;

        var block = blockRoot.GetComponent<FezGridBlock>();
        if (block == null)
            block = Undo.AddComponent<FezGridBlock>(blockRoot);

        Undo.RecordObject(block, "Write Block Cell");
        block.AssignCell(cell);
        var min = new Vector3(cell.x, cell.y, cell.z);
        if ((blockRoot.transform.position - min).sqrMagnitude > 1e-8f)
        {
            Undo.RecordObject(blockRoot.transform, "Write Block Cell");
            blockRoot.transform.position = min;
        }

        EditorUtility.SetDirty(block);
    }

    public static int WriteMapRoot(List<Transform> children, string undoName)
    {
        if (children == null || children.Count == 0)
            return 0;

        Undo.RecordObjects(children.ToArray(), undoName);
        var count = 0;
        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            if (child == null || child.GetComponentInChildren<Renderer>(true) == null)
                continue;

            var cell = FezGridMap.CellFromMinCorner(child.position);
            var min = new Vector3(cell.x, cell.y, cell.z);
            if ((child.position - min).sqrMagnitude > 1e-8f)
                child.position = min;

            WriteCell(child.gameObject, cell);
            count++;
        }

        return count;
    }
}
#endif
