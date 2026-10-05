#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 簡易 1x1 方塊地圖編輯器：在 Scene 視圖以網格放置／清除平台與背景牆。
/// </summary>
public sealed class FezBlockMapEditorWindow : EditorWindow
{
    private const string PrefPlatformKey = "Lichronicle.FezBlockMap.PlatformGuid";
    private const string PrefWallKey = "Lichronicle.FezBlockMap.WallGuid";
    private const string PrefRootKey = "Lichronicle.FezBlockMap.RootId";
    private const string PrefDragKey = "Lichronicle.FezBlockMap.DragPaint";
    private const float GridSize = 1f;
    private const float PaintHoldIntervalSeconds = 0.2f;
    private const string DefaultPlatformGuid = "5dc2e7616cd0eff46924a320585aa99c";
    private const string DefaultWallGuid = "de47f2bc00a23f348a62d07093edd238";

    private enum BrushKind
    {
        Platform = 0,
        Wall = 1,
        Erase = 2
    }

    private GameObject _platformPrefab;
    private GameObject _wallPrefab;
    private Transform _mapRoot;
    private BrushKind _brush = BrushKind.Platform;
    private bool _paintEnabled;
    private bool _replaceExisting = true;
    private bool _dragPaint = true;
    private Vector3Int? _hoverCell;
    private Vector3Int? _lastPaintCell;
    private double _lastPaintTime;
    private int _lastErasedInstanceId;

    [MenuItem("Lichronicle/Map/Block Map Editor")]
    public static void Open()
    {
        var win = GetWindow<FezBlockMapEditorWindow>("Block Map");
        win.minSize = new Vector2(320, 320);
        win.Show();
    }

    private void OnEnable()
    {
        LoadPrefs();
        SceneView.duringSceneGui += OnSceneGUI;
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        SavePrefs();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Fez 1×1 方塊地圖編輯器", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "開啟「繪製模式」後，在 Scene 視圖：\n" +
            "• 左鍵：依格點放置一格（對準可見模型表面）\n" +
            "• 右鍵：刪除游標下可見模型對應的方塊\n" +
            "• 中鍵：吸取游標下可見模型的方塊種類\n" +
            "• Shift＋左鍵：清除游標格（格點模式）\n" +
            "• Alt＋滑鼠：照常旋轉視角\n" +
            "• 左鍵按住拖曳：每 0.2 秒放置一格\n" +
            "• 判定以模型為準，不吃隱形碰撞箱\n" +
            "在 GridProjectionTest：方塊畫在 FezMapRoot 底下再 Play，會改走格子投影。平台是地形，牆筆刷是遮片，人會繞到牆的淺側，不會穿過去。SampleScene 不受影響。",
            MessageType.Info);

        _paintEnabled = EditorGUILayout.ToggleLeft("繪製模式（Scene 可繪）", _paintEnabled);
        _brush = (BrushKind)EditorGUILayout.EnumPopup("筆刷", _brush);
        _replaceExisting = EditorGUILayout.ToggleLeft("同格已有方塊則取代", _replaceExisting);
        _dragPaint = EditorGUILayout.ToggleLeft("右鍵拖曳連續刪除", _dragPaint);

        EditorGUILayout.Space(6);
        _mapRoot = (Transform)EditorGUILayout.ObjectField("地圖根節點", _mapRoot, typeof(Transform), true);
        if (_mapRoot == null && GUILayout.Button("建立 / 尋找 MapRoot"))
            EnsureMapRoot();

        EditorGUILayout.Space(6);
        _platformPrefab = (GameObject)EditorGUILayout.ObjectField("平台 Prefab（黃／紅）", _platformPrefab, typeof(GameObject), false);
        _wallPrefab = (GameObject)EditorGUILayout.ObjectField("牆 Prefab（橘／紫）", _wallPrefab, typeof(GameObject), false);

        if (GUILayout.Button("載入預設 TestBlocks Prefab"))
            LoadDefaultPrefabs();

        EditorGUILayout.Space(8);
        using (new EditorGUI.DisabledScope(!_paintEnabled))
        {
            EditorGUILayout.LabelField(_hoverCell.HasValue
                ? $"游標格子：{_hoverCell.Value}"
                : "游標格子：（移到 Scene 場景面）");
        }

        EditorGUILayout.Space(8);
        if (GUILayout.Button("將地圖根下所有子物件 Snap 到 1m 格"))
            SnapMapRootChildren();

        if (GUI.changed)
            SavePrefs();
    }

    private void OnSceneGUI(SceneView sceneView)
    {
        if (!_paintEnabled)
            return;

        var e = Event.current;

        // Alt：視角操作放行（旋轉常用 Alt＋左鍵）
        if (e.alt)
        {
            UpdateHoverCell(e);
            if (_hoverCell.HasValue && e.type == EventType.Repaint)
                DrawCellPreview(_hoverCell.Value);
            return;
        }

        UpdateHoverCell(e);

        if (_hoverCell.HasValue && (e.type == EventType.Repaint || e.type == EventType.Layout))
            DrawCellPreview(_hoverCell.Value);

        // 中鍵：吸取射線打到的方塊種類
        if (e.button == 2 && e.type == EventType.MouseDown)
        {
            var controlId = GUIUtility.GetControlID(FocusType.Passive);
            HandleUtility.AddDefaultControl(controlId);
            if (TryPickBrushFromRaycast(e.mousePosition))
                e.Use();
            return;
        }

        // 右鍵：射線打到的方塊刪除（與放置格點邏輯分離）
        if (e.button == 1 && (e.type == EventType.MouseDown || (_dragPaint && e.type == EventType.MouseDrag)))
        {
            var controlId = GUIUtility.GetControlID(FocusType.Passive);
            HandleUtility.AddDefaultControl(controlId);

            if (TryEraseByRaycast(e.mousePosition, force: e.type == EventType.MouseDown))
                e.Use();
            return;
        }

        // 左鍵：點下放一格；按住拖曳則每 0.2 秒放一格
        if (e.button == 0 && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag))
        {
            var controlId = GUIUtility.GetControlID(FocusType.Passive);
            HandleUtility.AddDefaultControl(controlId);

            if (!_hoverCell.HasValue)
                return;

            var erase = e.shift || _brush == BrushKind.Erase;
            var kind = erase ? BrushKind.Erase : _brush;

            if (e.type == EventType.MouseDown)
            {
                TryPaint(_hoverCell.Value, kind, force: true);
                e.Use();
            }
            else if (e.type == EventType.MouseDrag)
            {
                if (TryPaint(_hoverCell.Value, kind, force: false))
                    e.Use();
            }
        }

        if (e.type == EventType.MouseUp && (e.button == 0 || e.button == 1))
        {
            _lastPaintCell = null;
            _lastPaintTime = 0;
            _lastErasedInstanceId = 0;
        }

        if (e.type == EventType.MouseMove)
            sceneView.Repaint();
    }

    private bool TryPaint(Vector3Int cell, BrushKind brush, bool force)
    {
        if (!force)
        {
            if (_lastPaintCell.HasValue && _lastPaintCell.Value == cell)
                return false;

            if (EditorApplication.timeSinceStartup - _lastPaintTime < PaintHoldIntervalSeconds)
                return false;
        }

        PaintAt(cell, brush);
        _lastPaintCell = cell;
        _lastPaintTime = EditorApplication.timeSinceStartup;
        return true;
    }

    /// <summary>
    /// 右鍵刪除：以 Scene 可見模型挑選（非物理碰撞），再解析成地圖方塊根物件。
    /// </summary>
    private bool TryEraseByRaycast(Vector2 guiPoint, bool force)
    {
        EnsureMapRoot();
        if (_mapRoot == null)
            return false;

        if (!TryPickVisibleBlock(guiPoint, out var block))
            return false;

        if (!force)
        {
            if (block.GetInstanceID() == _lastErasedInstanceId)
                return false;

            if (EditorApplication.timeSinceStartup - _lastPaintTime < PaintHoldIntervalSeconds)
                return false;
        }

        _lastErasedInstanceId = block.GetInstanceID();
        _lastPaintTime = EditorApplication.timeSinceStartup;
        Undo.DestroyObjectImmediate(block);
        EditorUtility.SetDirty(_mapRoot);
        return true;
    }

    /// <summary>
    /// Scene 視圖可見物件挑選（忽略純碰撞／隱形碰撞箱）。
    /// </summary>
    private bool TryPickVisibleBlock(Vector2 guiPoint, out GameObject block)
    {
        block = null;
        var picked = HandleUtility.PickGameObject(guiPoint, false);
        if (picked == null)
            return false;

        block = ResolveMapBlockRoot(picked);
        if (block != null)
            return true;

        var prefabRoot = PrefabUtility.GetNearestPrefabInstanceRoot(picked);
        if (prefabRoot != null && IsKnownBlockPrefab(prefabRoot))
        {
            block = prefabRoot;
            return true;
        }

        return false;
    }

    private GameObject ResolveMapBlockRoot(GameObject go)
    {
        if (go == null || _mapRoot == null)
            return null;

        var t = go.transform;
        while (t != null)
        {
            if (t.parent == _mapRoot)
                return t.gameObject;
            if (t == _mapRoot)
                return null;
            t = t.parent;
        }

        // 若不在 MapRoot 下，但命中的是平台／牆 Prefab 實例，仍允許刪除其最外層根源
        var prefabRoot = PrefabUtility.GetNearestPrefabInstanceRoot(go);
        if (prefabRoot != null && IsKnownBlockPrefab(prefabRoot))
            return prefabRoot;

        return null;
    }

    private bool IsKnownBlockPrefab(GameObject go)
    {
        if (go == null)
            return false;

        var source = PrefabUtility.GetCorrespondingObjectFromSource(go);
        if (source == null)
            return false;

        return source == _platformPrefab || source == _wallPrefab
            || source.name.IndexOf("Block", System.StringComparison.OrdinalIgnoreCase) >= 0
            || source.name.IndexOf("Wall", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// 中鍵吸取：可見模型命中平台／牆時，切換筆刷與對應 Prefab。
    /// </summary>
    private bool TryPickBrushFromRaycast(Vector2 guiPoint)
    {
        if (!TryPickVisibleBlock(guiPoint, out var block))
            return false;

        var source = PrefabUtility.GetCorrespondingObjectFromSource(block) as GameObject;
        if (source == null)
            source = block;

        if (IsWallLike(source, block))
        {
            _brush = BrushKind.Wall;
            _wallPrefab = source;
            Debug.Log($"[FezBlockMap] 筆刷切換為牆：{source.name}");
        }
        else
        {
            _brush = BrushKind.Platform;
            _platformPrefab = source;
            Debug.Log($"[FezBlockMap] 筆刷切換為平台：{source.name}");
        }

        SavePrefs();
        Repaint();
        return true;
    }

    private static bool IsWallLike(GameObject source, GameObject instance)
    {
        if (source != null)
        {
            if (source.GetComponent<FezBackgroundWall>() != null
                || source.GetComponentInChildren<FezBackgroundWall>(true) != null)
                return true;
            if (source.name.IndexOf("Wall", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        if (instance != null)
        {
            if (instance.GetComponent<FezBackgroundWall>() != null
                || instance.GetComponentInChildren<FezBackgroundWall>(true) != null)
                return true;
            if (instance.name.IndexOf("Wall", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    private void UpdateHoverCell(Event e)
    {
        if (e.type != EventType.MouseMove
            && e.type != EventType.MouseDown
            && e.type != EventType.MouseDrag
            && e.type != EventType.Repaint
            && e.type != EventType.Layout)
            return;

        // 以 Scene 可見表面為準（PlaceObject），避開隱形碰撞箱
        if (HandleUtility.PlaceObject(e.mousePosition, out var point, out var normal))
        {
            _hoverCell = WorldToCell(point + normal * 0.01f);
            return;
        }

        var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        var plane = new Plane(Vector3.up, Vector3.zero);
        if (plane.Raycast(ray, out var enter))
        {
            _hoverCell = WorldToCell(ray.GetPoint(enter));
            return;
        }

        _hoverCell = null;
    }

    private static Vector3Int WorldToCell(Vector3 world)
    {
        return new Vector3Int(
            Mathf.FloorToInt(world.x / GridSize),
            Mathf.FloorToInt(world.y / GridSize),
            Mathf.FloorToInt(world.z / GridSize));
    }

    private static Vector3 CellToWorld(Vector3Int cell)
    {
        return new Vector3(cell.x * GridSize, cell.y * GridSize, cell.z * GridSize);
    }

    private void DrawCellPreview(Vector3Int cell)
    {
        var center = CellToWorld(cell) + Vector3.one * (GridSize * 0.5f);
        var size = Vector3.one * GridSize;
        Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
        Handles.color = _brush == BrushKind.Erase
            ? new Color(1f, 0.25f, 0.2f, 0.35f)
            : _brush == BrushKind.Wall
                ? new Color(1f, 0.55f, 0.15f, 0.35f)
                : new Color(0.3f, 0.85f, 0.35f, 0.35f);
        Handles.DrawWireCube(center, size);
        Handles.color = new Color(Handles.color.r, Handles.color.g, Handles.color.b, 0.12f);
        Handles.CubeHandleCap(0, center, Quaternion.identity, GridSize, EventType.Repaint);
    }

    private void PaintAt(Vector3Int cell, BrushKind brush)
    {
        EnsureMapRoot();
        if (_mapRoot == null)
            return;

        var existing = FindBlockAtCell(cell);
        if (brush == BrushKind.Erase)
        {
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing);
                EditorUtility.SetDirty(_mapRoot);
            }
            return;
        }

        var prefab = brush == BrushKind.Wall ? _wallPrefab : _platformPrefab;
        if (prefab == null)
        {
            Debug.LogWarning("[FezBlockMap] 尚未指定對應 Prefab。");
            return;
        }

        if (existing != null)
        {
            if (!_replaceExisting)
                return;
            Undo.DestroyObjectImmediate(existing);
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _mapRoot);
        if (instance == null)
            instance = Instantiate(prefab, _mapRoot);

        Undo.RegisterCreatedObjectUndo(instance, "Place Fez Block");
        instance.transform.position = CellToWorld(cell);
        instance.transform.rotation = Quaternion.identity;
        instance.name = $"{prefab.name}_{cell.x}_{cell.y}_{cell.z}";
        EditorUtility.SetDirty(_mapRoot);
    }

    private GameObject FindBlockAtCell(Vector3Int cell)
    {
        if (_mapRoot == null)
            return null;

        var target = CellToWorld(cell);
        for (var i = 0; i < _mapRoot.childCount; i++)
        {
            var child = _mapRoot.GetChild(i);
            if (child == null)
                continue;
            if ((child.position - target).sqrMagnitude < 0.01f)
                return child.gameObject;
        }

        return null;
    }

    private void EnsureMapRoot()
    {
        if (_mapRoot != null)
            return;

        var existing = GameObject.Find("FezMapRoot");
        if (existing != null)
        {
            _mapRoot = existing.transform;
            return;
        }

        var go = new GameObject("FezMapRoot");
        Undo.RegisterCreatedObjectUndo(go, "Create FezMapRoot");
        _mapRoot = go.transform;
    }

    private void SnapMapRootChildren()
    {
        EnsureMapRoot();
        if (_mapRoot == null)
            return;

        var count = 0;
        var transforms = new List<Transform>(_mapRoot.childCount);
        for (var i = 0; i < _mapRoot.childCount; i++)
            transforms.Add(_mapRoot.GetChild(i));

        Undo.RecordObjects(transforms.ToArray(), "Snap Map Root To Grid");
        for (var i = 0; i < transforms.Count; i++)
        {
            var t = transforms[i];
            if (t == null)
                continue;
            var cell = WorldToCell(t.position + Vector3.one * 0.001f);
            var snapped = CellToWorld(cell);
            if ((snapped - t.position).sqrMagnitude < 1e-8f)
                continue;
            t.position = snapped;
            EditorUtility.SetDirty(t);
            count++;
        }

        Debug.Log($"[FezBlockMap] 已對齊地圖根下 {count} 個物件。");
    }

    private void LoadDefaultPrefabs()
    {
        _platformPrefab = LoadPrefabByGuid(DefaultPlatformGuid);
        _wallPrefab = LoadPrefabByGuid(DefaultWallGuid);
        if (_platformPrefab == null || _wallPrefab == null)
            Debug.LogWarning("[FezBlockMap] 找不到預設 TestBlocks Prefab，請手動指定。");
    }

    private static GameObject LoadPrefabByGuid(string guid)
    {
        var path = AssetDatabase.GUIDToAssetPath(guid);
        if (string.IsNullOrEmpty(path))
            return null;
        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    private void LoadPrefs()
    {
        _platformPrefab = LoadPrefabByGuid(EditorPrefs.GetString(PrefPlatformKey, DefaultPlatformGuid));
        _wallPrefab = LoadPrefabByGuid(EditorPrefs.GetString(PrefWallKey, DefaultWallGuid));
        if (_platformPrefab == null || _wallPrefab == null)
            LoadDefaultPrefabs();

        _dragPaint = EditorPrefs.GetBool(PrefDragKey, true);

        var rootId = EditorPrefs.GetInt(PrefRootKey, 0);
        if (rootId != 0)
        {
            var obj = EditorUtility.InstanceIDToObject(rootId) as Transform;
            if (obj != null)
                _mapRoot = obj;
        }
    }

    private void SavePrefs()
    {
        if (_platformPrefab != null)
        {
            var path = AssetDatabase.GetAssetPath(_platformPrefab);
            var guid = AssetDatabase.AssetPathToGUID(path);
            if (!string.IsNullOrEmpty(guid))
                EditorPrefs.SetString(PrefPlatformKey, guid);
        }

        if (_wallPrefab != null)
        {
            var path = AssetDatabase.GetAssetPath(_wallPrefab);
            var guid = AssetDatabase.AssetPathToGUID(path);
            if (!string.IsNullOrEmpty(guid))
                EditorPrefs.SetString(PrefWallKey, guid);
        }

        EditorPrefs.SetBool(PrefDragKey, _dragPaint);
        EditorPrefs.SetInt(PrefRootKey, _mapRoot != null ? _mapRoot.GetInstanceID() : 0);
    }
}
#endif
