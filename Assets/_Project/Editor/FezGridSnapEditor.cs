#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 關卡編輯輔助：將選取物件的世界座標吸附到整數公尺格點（配合 1x1 Block／Wall）。
/// </summary>
public static class FezGridSnapEditor
{
    private const float GridSize = 1f;

    [MenuItem("Lichronicle/Grid/Snap Selection To 1m Grid %#g")]
    public static void SnapSelectionToGrid()
    {
        var transforms = Selection.transforms;
        if (transforms == null || transforms.Length == 0)
        {
            EditorUtility.DisplayDialog("Snap to 1m Grid", "請先在 Hierarchy 或 Scene 選取要對齊的物件。", "OK");
            return;
        }

        var snapped = 0;
        Undo.RecordObjects(transforms, "Snap Selection To 1m Grid");

        for (var i = 0; i < transforms.Length; i++)
        {
            var t = transforms[i];
            if (t == null)
                continue;

            var p = t.position;
            var target = new Vector3(
                SnapAxis(p.x),
                SnapAxis(p.y),
                SnapAxis(p.z));

            if ((target - p).sqrMagnitude < 1e-8f)
                continue;

            t.position = target;
            EditorUtility.SetDirty(t);
            snapped++;
        }

        Debug.Log($"[FezGridSnap] 已對齊 {snapped}/{transforms.Length} 個物件到 {GridSize}m 網格。");
    }

    [MenuItem("Lichronicle/Grid/Snap Selection To 1m Grid %#g", true)]
    private static bool SnapSelectionToGridValidate()
    {
        return Selection.transforms != null && Selection.transforms.Length > 0;
    }

    private static float SnapAxis(float value)
    {
        return Mathf.Round(value / GridSize) * GridSize;
    }
}
#endif
