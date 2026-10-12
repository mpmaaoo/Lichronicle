#if UNITY_EDITOR
using System;
using UnityEngine;

/// <summary>
/// 地圖編輯器筆刷目錄。新增方塊種類時：在此加一筆 <see cref="FezBlockMapBrushDef"/>，
/// 並填好預設 Prefab GUID／辨識條件即可（不必改繪製主迴圈）。
/// </summary>
public static class FezBlockMapBrushCatalog
{
    public const string EraseId = "erase";

    /// <summary>
    /// 可放置的方塊筆刷（不含清除）。
    /// 較具體的種類請加在陣列末端：中鍵吸取由後往前比對，避免「BoundaryWall」被「Wall」吃掉。
    /// </summary>
    public static readonly FezBlockMapBrushDef[] PlaceBrushes =
    {
        new FezBlockMapBrushDef(
            id: "platform",
            displayName: "平台",
            prefKey: "Lichronicle.FezBlockMap.PlatformGuid",
            defaultPrefabGuid: "5dc2e7616cd0eff46924a320585aa99c",
            previewColor: new Color(0.3f, 0.85f, 0.35f, 0.35f),
            fieldLabel: "平台 Prefab",
            matches: MatchesPlatform),

        new FezBlockMapBrushDef(
            id: "occluder",
            displayName: "遮片牆",
            prefKey: "Lichronicle.FezBlockMap.WallGuid",
            defaultPrefabGuid: "de47f2bc00a23f348a62d07093edd238",
            previewColor: new Color(1f, 0.55f, 0.15f, 0.35f),
            fieldLabel: "遮片牆 Prefab",
            matches: MatchesOccluder),

        new FezBlockMapBrushDef(
            id: "boundary",
            displayName: "邊界牆",
            prefKey: "Lichronicle.FezBlockMap.BoundaryWallGuid",
            defaultPrefabGuid: "d3e4f5a6b7c8490a12b3c4d5e6f7082a",
            previewColor: new Color(0.95f, 0.25f, 0.5f, 0.35f),
            fieldLabel: "邊界牆 Prefab",
            matches: MatchesBoundary),
    };

    public static int IndexOfId(string id)
    {
        for (var i = 0; i < PlaceBrushes.Length; i++)
        {
            if (PlaceBrushes[i].Id == id)
                return i;
        }

        return 0;
    }

    public static int FindMatchingBrushIndex(GameObject source, GameObject instance)
    {
        // 先比對邊界牆等較具體的種類。
        for (var i = PlaceBrushes.Length - 1; i >= 0; i--)
        {
            if (PlaceBrushes[i].Matches(source, instance))
                return i;
        }

        return IndexOfId("platform");
    }

    private static bool MatchesBoundary(GameObject source, GameObject instance)
    {
        return HasComponentOrName(source, instance, typeof(FezGridBoundaryWall), "BoundaryWall");
    }

    private static bool MatchesOccluder(GameObject source, GameObject instance)
    {
        if (MatchesBoundary(source, instance))
            return false;

        if (HasComponent(source, instance, typeof(FezBackgroundWall)))
            return true;

        return HasName(source, instance, "Wall") && !HasName(source, instance, "BoundaryWall");
    }

    private static bool MatchesPlatform(GameObject source, GameObject instance)
    {
        if (HasComponent(source, instance, typeof(FezGridPlatform)))
            return true;
        if (HasName(source, instance, "Block"))
            return true;
        // 後備：其他種類都沒對上時當平台。
        return true;
    }

    private static bool HasComponentOrName(GameObject source, GameObject instance, Type componentType, string nameToken)
    {
        return HasComponent(source, instance, componentType) || HasName(source, instance, nameToken);
    }

    private static bool HasComponent(GameObject source, GameObject instance, Type componentType)
    {
        if (source != null
            && (source.GetComponent(componentType) != null
                || source.GetComponentInChildren(componentType, true) != null))
            return true;

        if (instance != null
            && (instance.GetComponent(componentType) != null
                || instance.GetComponentInChildren(componentType, true) != null))
            return true;

        return false;
    }

    private static bool HasName(GameObject source, GameObject instance, string token)
    {
        if (source != null
            && !string.IsNullOrEmpty(source.name)
            && source.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        if (instance != null
            && !string.IsNullOrEmpty(instance.name)
            && instance.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        return false;
    }
}

public sealed class FezBlockMapBrushDef
{
    public readonly string Id;
    public readonly string DisplayName;
    public readonly string PrefKey;
    public readonly string DefaultPrefabGuid;
    public readonly Color PreviewColor;
    public readonly string FieldLabel;
    public readonly Func<GameObject, GameObject, bool> Matches;

    public FezBlockMapBrushDef(
        string id,
        string displayName,
        string prefKey,
        string defaultPrefabGuid,
        Color previewColor,
        string fieldLabel,
        Func<GameObject, GameObject, bool> matches)
    {
        Id = id;
        DisplayName = displayName;
        PrefKey = prefKey;
        DefaultPrefabGuid = defaultPrefabGuid;
        PreviewColor = previewColor;
        FieldLabel = fieldLabel;
        Matches = matches ?? ((_, __) => false);
    }
}
#endif
