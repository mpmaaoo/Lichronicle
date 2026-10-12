#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 簡易 1x1 方塊地圖編輯器：在 Scene 視圖以網格放置／清除方塊。
/// 筆刷種類來自 <see cref="FezBlockMapBrushCatalog"/>；新方塊加目錄即可。
/// </summary>
public sealed class FezBlockMapEditorWindow : EditorWindow
{
    private const string PrefRootKey = "Lichronicle.FezBlockMap.RootId";
    private const string PrefDragKey = "Lichronicle.FezBlockMap.DragPaint";
    private const string PrefBrushKey = "Lichronicle.FezBlockMap.BrushId";
    private const float GridSize = 1f;
    private const float PaintHoldIntervalSeconds = 0.2f;

    private static readonly Color ErasePreviewColor = new Color(1f, 0.25f, 0.2f, 0.35f);

    private readonly GameObject[] _brushPrefabs = new GameObject[FezBlockMapBrushCatalog.PlaceBrushes.Length];
    private readonly string[] _brushPopupNames;

    private Transform _mapRoot;
    private int _brushIndex; // PlaceBrushes index；Erase 時為 -1
    private bool _eraseMode;
    private bool _paintEnabled;
    private bool _replaceExisting = true;
    private bool _dragPaint = true;
    private Vector3Int? _hoverCell;
    private Vector3Int? _lastPaintCell;
    private double _lastPaintTime;
    private int _lastErasedInstanceId;

    public FezBlockMapEditorWindow()
    {
        var brushes = FezBlockMapBrushCatalog.PlaceBrushes;
        _brushPopupNames = new string[brushes.Length + 1];
        for (var i = 0; i < brushes.Length; i++)
            _brushPopupNames[i] = brushes[i].DisplayName;
        _brushPopupNames[brushes.Length] = "清除";
    }

    [MenuItem("Lichronicle/Map/Block Map Editor")]
    public static void Open()
    {
        var win = GetWindow<FezBlockMapEditorWindow>("Block Map");
        win.minSize = new Vector2(320, 360);
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
            "平台＝地形；遮片牆＝繞到淺側；邊界牆＝當前視角看得見才硬擋。\n" +
            "新方塊：在 FezBlockMapBrushCatalog.PlaceBrushes 加一筆即可。",
            MessageType.Info);

        _paintEnabled = EditorGUILayout.ToggleLeft("繪製模式（Scene 可繪）", _paintEnabled);

        var popupIndex = _eraseMode ? _brushPopupNames.Length - 1 : Mathf.Clamp(_brushIndex, 0, FezBlockMapBrushCatalog.PlaceBrushes.Length - 1);
        var nextPopup = EditorGUILayout.Popup("筆刷", popupIndex, _brushPopupNames);
        if (nextPopup != popupIndex)
        {
            if (nextPopup >= FezBlockMapBrushCatalog.PlaceBrushes.Length)
            {
                _eraseMode = true;
            }
            else
            {
                _eraseMode = false;
                _brushIndex = nextPopup;
            }
        }

        _replaceExisting = EditorGUILayout.ToggleLeft("同格已有方塊則取代", _replaceExisting);
        _dragPaint = EditorGUILayout.ToggleLeft("右鍵拖曳連續刪除", _dragPaint);

        EditorGUILayout.Space(6);
        _mapRoot = (Transform)EditorGUILayout.ObjectField("地圖根節點", _mapRoot, typeof(Transform), true);
        if (_mapRoot == null && GUILayout.Button("建立 / 尋找 MapRoot"))
            EnsureMapRoot();

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("筆刷 Prefab", EditorStyles.boldLabel);
        var brushes = FezBlockMapBrushCatalog.PlaceBrushes;
        for (var i = 0; i < brushes.Length; i++)
        {
            _brushPrefabs[i] = (GameObject)EditorGUILayout.ObjectField(
                brushes[i].FieldLabel,
                _brushPrefabs[i],
                typeof(GameObject),
                false);
        }

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
        if (GUILayout.Button("將地圖根下的格子座標寫入方塊"))
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

        // 中鍵：吸取筆刷
        if (e.type == EventType.MouseDown && e.button == 2)
        {
            if (TryPickBrushFromRaycast(e.mousePosition))
            {
                e.Use();
                sceneView.Repaint();
                Repaint();
            }

            return;
        }

        // 右鍵：刪除可見方塊
        if ((e.type == EventType.MouseDown || (_dragPaint && e.type == EventType.MouseDrag)) && e.button == 1)
        {
            if (TryEraseByRaycast(e.mousePosition, e.type == EventType.MouseDown))
            {
                e.Use();
                sceneView.Repaint();
            }

            return;
        }

        if (e.type == EventType.MouseUp)
        {
            _lastPaintCell = null;
            _lastErasedInstanceId = 0;
        }

        if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0)
        {
            if (!_hoverCell.HasValue)
                return;

            var erase = e.shift || _eraseMode;
            if (erase)
            {
                if (TryPaint(_hoverCell.Value, erase: true, force: e.type == EventType.MouseDown))
                {
                    e.Use();
                    sceneView.Repaint();
                }
            }
            else if (TryPaint(_hoverCell.Value, erase: false, force: e.type == EventType.MouseDown))
            {
                e.Use();
                sceneView.Repaint();
            }
        }

        if (_hoverCell.HasValue && e.type == EventType.Repaint)
            DrawCellPreview(_hoverCell.Value);

        if (e.type == EventType.MouseMove)
            sceneView.Repaint();
    }

    private bool TryPaint(Vector3Int cell, bool erase, bool force)
    {
        if (!force)
        {
            if (_lastPaintCell.HasValue && _lastPaintCell.Value == cell)
                return false;

            if (EditorApplication.timeSinceStartup - _lastPaintTime < PaintHoldIntervalSeconds)
                return false;
        }

        PaintAt(cell, erase);
        _lastPaintCell = cell;
        _lastPaintTime = EditorApplication.timeSinceStartup;
        return true;
    }

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

        for (var i = 0; i < _brushPrefabs.Length; i++)
        {
            if (_brushPrefabs[i] != null && source == _brushPrefabs[i])
                return true;
        }

        return source.name.IndexOf("Block", System.StringComparison.OrdinalIgnoreCase) >= 0
            || source.name.IndexOf("Wall", System.StringComparison.OrdinalIgnoreCase) >= 0
            || source.name.IndexOf("Boundary", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private bool TryPickBrushFromRaycast(Vector2 guiPoint)
    {
        if (!TryPickVisibleBlock(guiPoint, out var block))
            return false;

        var source = PrefabUtility.GetCorrespondingObjectFromSource(block) as GameObject;
        if (source == null)
            source = block;

        var index = FezBlockMapBrushCatalog.FindMatchingBrushIndex(source, block);
        _eraseMode = false;
        _brushIndex = index;
        if (index >= 0 && index < _brushPrefabs.Length && source != null)
            _brushPrefabs[index] = source;

        Debug.Log($"[FezBlockMap] 筆刷切換為 {FezBlockMapBrushCatalog.PlaceBrushes[index].DisplayName}：{source.name}");
        SavePrefs();
        Repaint();
        return true;
    }

    private void UpdateHoverCell(Event e)
    {
        if (e.type != EventType.MouseMove
            && e.type != EventType.MouseDown
            && e.type != EventType.MouseDrag
            && e.type != EventType.Repaint
            && e.type != EventType.Layout)
            return;

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

        Color preview;
        if (_eraseMode)
            preview = ErasePreviewColor;
        else
        {
            var brushes = FezBlockMapBrushCatalog.PlaceBrushes;
            var i = Mathf.Clamp(_brushIndex, 0, brushes.Length - 1);
            preview = brushes[i].PreviewColor;
        }

        Handles.color = preview;
        Handles.DrawWireCube(center, size);
        Handles.color = new Color(preview.r, preview.g, preview.b, 0.12f);
        Handles.CubeHandleCap(0, center, Quaternion.identity, GridSize, EventType.Repaint);
    }

    private void PaintAt(Vector3Int cell, bool erase)
    {
        EnsureMapRoot();
        if (_mapRoot == null)
            return;

        var existing = FindBlockAtCell(cell);
        if (erase)
        {
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing);
                EditorUtility.SetDirty(_mapRoot);
            }

            return;
        }

        var brushes = FezBlockMapBrushCatalog.PlaceBrushes;
        var index = Mathf.Clamp(_brushIndex, 0, brushes.Length - 1);
        var prefab = _brushPrefabs[index];
        if (prefab == null)
        {
            Debug.LogWarning($"[FezBlockMap] 尚未指定「{brushes[index].DisplayName}」Prefab。");
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
        FezGridCellAuthoring.WriteCell(instance, cell);
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
            if (FezGridMap.TryReadStoredCell(child, out var stored) && stored == cell)
                return child.gameObject;
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

        count = FezGridCellAuthoring.WriteMapRoot(transforms, "Snap Map Root To Grid");
        Debug.Log($"[FezBlockMap] 已把地圖根下 {count} 個方塊的格子寫入方塊。");
    }

    private void LoadDefaultPrefabs()
    {
        var brushes = FezBlockMapBrushCatalog.PlaceBrushes;
        var missing = false;
        for (var i = 0; i < brushes.Length; i++)
        {
            _brushPrefabs[i] = LoadPrefabByGuid(brushes[i].DefaultPrefabGuid);
            if (_brushPrefabs[i] == null)
                missing = true;
        }

        if (missing)
            Debug.LogWarning("[FezBlockMap] 部分預設 TestBlocks Prefab 找不到，請手動指定。");
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
        var brushes = FezBlockMapBrushCatalog.PlaceBrushes;
        var anyMissing = false;
        for (var i = 0; i < brushes.Length; i++)
        {
            var guid = EditorPrefs.GetString(brushes[i].PrefKey, brushes[i].DefaultPrefabGuid);
            _brushPrefabs[i] = LoadPrefabByGuid(guid);
            if (_brushPrefabs[i] == null)
                anyMissing = true;
        }

        if (anyMissing)
            LoadDefaultPrefabs();

        _dragPaint = EditorPrefs.GetBool(PrefDragKey, true);
        var brushId = EditorPrefs.GetString(PrefBrushKey, "platform");
        if (brushId == FezBlockMapBrushCatalog.EraseId)
        {
            _eraseMode = true;
            _brushIndex = 0;
        }
        else
        {
            _eraseMode = false;
            _brushIndex = FezBlockMapBrushCatalog.IndexOfId(brushId);
        }

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
        var brushes = FezBlockMapBrushCatalog.PlaceBrushes;
        for (var i = 0; i < brushes.Length; i++)
        {
            if (_brushPrefabs[i] == null)
                continue;
            var path = AssetDatabase.GetAssetPath(_brushPrefabs[i]);
            var guid = AssetDatabase.AssetPathToGUID(path);
            if (!string.IsNullOrEmpty(guid))
                EditorPrefs.SetString(brushes[i].PrefKey, guid);
        }

        EditorPrefs.SetBool(PrefDragKey, _dragPaint);
        EditorPrefs.SetString(
            PrefBrushKey,
            _eraseMode
                ? FezBlockMapBrushCatalog.EraseId
                : brushes[Mathf.Clamp(_brushIndex, 0, brushes.Length - 1)].Id);
        EditorPrefs.SetInt(PrefRootKey, _mapRoot != null ? _mapRoot.GetInstanceID() : 0);
    }
}
#endif
