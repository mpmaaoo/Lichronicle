using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 外圈探測箱碰到背景牆 MoveTrigger 時，
/// 沿深度軸把玩家推至牆本體淺側以繞過牆。
/// </summary>
[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
[DefaultExecutionOrder(195)]
public sealed class FezPlayerBackgroundWallRouter : MonoBehaviour
{
    [Header("深度軸")]
    [SerializeField] private FezDepthAxisSource depthAxisSource = FezDepthAxisSource.WorldViewState;
    [SerializeField] private Transform explicitAxisTransform;
    [SerializeField] private Camera explicitCamera;

    [Header("繞行")]
    [Tooltip("0 = 單幀抵達繞行目標；>0 = 每幀沿深度軸最多移動這麼多（公尺）。")]
    [SerializeField] private float maxShallowPushPerFrame = 0f;
    [SerializeField] private float shallowResolvedEpsilon = 0.02f;
    [SerializeField] private bool routingEnabled = true;

    [Header("外圈探測")]
    [Tooltip("留空則自動找子物件；有設定時僅在探測箱碰到 MoveTrigger 才繞行。")]
    [SerializeField] private FezPlayerBackgroundWallRouteProbe routeProbe;
    [SerializeField] private LayerMask moveTriggerMask = ~0;
    [Tooltip("無探測箱時：沿淺向擴大 CC bounds 查詢；有探測箱時忽略。")]
    [SerializeField] private float routeLeadDepth = 0.5f;
    [Tooltip("牆本體底面高於腳底超過此值（公尺）時不參與繞行。")]
    [SerializeField] private float maxWallBottomAboveFeet = 0.45f;
    [Tooltip("牆頂低於腳底超過此值（公尺）時不參與繞行。")]
    [SerializeField] private float maxWallTopBelowFeet = 0.05f;

    [Header("可見性")]
    [SerializeField] private bool blockRoutingWhenOccluded = true;
    [SerializeField] private FezRotationOcclusionGate occlusionGate;

    private static readonly List<FezBackgroundWall> WallBuffer = new List<FezBackgroundWall>(8);
    private static readonly List<FezBackgroundWall> QualifyingWallBuffer = new List<FezBackgroundWall>(8);
    private static readonly List<FezBackgroundWall> BypassWallRemovalBuffer = new List<FezBackgroundWall>(4);
    private static readonly List<FezDepthProxySurface> FootSurfaceBuffer = new List<FezDepthProxySurface>(4);
    private static readonly Collider[] OverlapBuffer = new Collider[32];

    private CharacterController _cc;
    private FezPlayerDepthSnapper _depthSnapper;
    private readonly HashSet<FezBackgroundWall> _trackedBypassWalls = new HashSet<FezBackgroundWall>(4);
    private int _bypassStateFrame = -1;

    public bool IsRoutingAroundShallow { get; private set; }
    public bool IsDepthSnapSuppressActive => ShouldSuppressPlatformDepthSnap();
    /// <summary>抑制區碰牆等硬規則啟用中（完整擋住往深的深度對齊）。</summary>
    public bool IsDepthSnapHardSuppressed() => ShouldSuppressPlatformDepthSnap();
    private bool UsesRouteProbe => routeProbe != null && routeProbe.isActiveAndEnabled;

    private void Awake()
    {
        _cc = GetComponent<CharacterController>();
        _depthSnapper = GetComponent<FezPlayerDepthSnapper>();
        if (occlusionGate == null)
            occlusionGate = GetComponent<FezRotationOcclusionGate>();
        if (routeProbe == null)
            routeProbe = GetComponentInChildren<FezPlayerBackgroundWallRouteProbe>(true);
    }

    private void LateUpdate()
    {
        IsRoutingAroundShallow = false;
        if (!routingEnabled || _cc == null)
            return;

        routeProbe?.RefreshTouchingWalls();

        EnsureBypassTrackingForFrame();
        var viewForward = GetViewForward();

        if (!CanRouteByVisibility() || !viewForward.HasValue)
            return;

        if (!TryBuildShallowPushMove(viewForward.Value, out var totalMove))
            return;

        IsRoutingAroundShallow = true;

        if (maxShallowPushPerFrame > 0f)
        {
            var step = maxShallowPushPerFrame;
            if (totalMove.sqrMagnitude > step * step)
                totalMove = totalMove.normalized * step;
        }

        _cc.Move(totalMove);
        FezBackgroundWallMoveTriggerFollower.SyncAllAfterBackgroundWallRouter(Time.deltaTime);
    }

    public bool ShouldSuppressDepthSnapForPlatform(FezDepthProxySurface surface)
    {
        if (!routingEnabled || _cc == null || surface == null)
            return false;

        var zone = surface.GetDepthSnapSuppressZone();
        if (zone == null || !zone.isActiveAndEnabled)
            return false;

        var viewForward = GetViewForward();
        if (!viewForward.HasValue)
            return false;

        EnsureBypassTrackingForFrame();

        foreach (var wall in _trackedBypassWalls)
        {
            if (wall != null && wall.isActiveAndEnabled && zone.OverlapsBypassObject(wall))
                return true;
        }

        return false;
    }

    public bool ShouldSuppressPlatformDepthSnap()
    {
        if (!routingEnabled || _cc == null)
            return false;
        EnsureBypassTrackingForFrame();

        FootSurfaceBuffer.Clear();
        if (_depthSnapper != null)
            _depthSnapper.CollectFootPlatformSurfaces(FootSurfaceBuffer);

        for (var i = 0; i < FootSurfaceBuffer.Count; i++)
        {
            if (ShouldSuppressDepthSnapForPlatform(FootSurfaceBuffer[i]))
                return true;
        }

        // 硬規則：只要任一啟用中的 suppress 區塊碰到背景牆，就禁止深度傳送。
        foreach (var zone in FezPlatformDepthSnapSuppressZone.EnumerateActiveZones())
        {
            if (zone == null || !zone.isActiveAndEnabled)
                continue;

            var zoneBounds = zone.GetWorldBounds();
            FezBackgroundWall.CollectWallsWithMoveTriggerOverlap(zoneBounds, WallBuffer);
            for (var i = 0; i < WallBuffer.Count; i++)
            {
                var wall = WallBuffer[i];
                if (wall != null && wall.isActiveAndEnabled && zone.OverlapsBypassObject(wall))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 玩家是否已位於「目前與抑制區重疊的背景牆」的淺側（deep face ≤ 各牆繞行目標）。
    /// 若沒有與此條件對應的牆，回傳 false（不得套用淺向放行例外）。
    /// </summary>
    public bool IsPlayerAlreadyShallowOfOverlappingSuppressWalls(Vector3 viewForward)
    {
        if (!routingEnabled || _cc == null)
            return false;
        if (viewForward.sqrMagnitude < 0.0001f)
            return false;

        EnsureBypassTrackingForFrame();

        if (!TryCollectWallsOverlappingActiveSuppressZones(QualifyingWallBuffer))
        {
            QualifyingWallBuffer.Clear();
            foreach (var wall in _trackedBypassWalls)
            {
                if (wall != null && wall.isActiveAndEnabled)
                    QualifyingWallBuffer.Add(wall);
            }
        }

        if (QualifyingWallBuffer.Count == 0)
            return false;

        var playerDeep = FezBackgroundWall.ProjectBoundsDeepDepth(_cc.bounds, viewForward);
        var epsilon = Mathf.Max(0.001f, shallowResolvedEpsilon);

        for (var i = 0; i < QualifyingWallBuffer.Count; i++)
        {
            var wall = QualifyingWallBuffer[i];
            if (wall == null)
                continue;

            var routeTarget = wall.GetPlayerRouteTargetDepth(viewForward);
            if (playerDeep > routeTarget + epsilon)
                return false;
        }

        return true;
    }

    bool TryCollectWallsOverlappingActiveSuppressZones(List<FezBackgroundWall> results)
    {
        results.Clear();
        foreach (var zone in FezPlatformDepthSnapSuppressZone.EnumerateActiveZones())
        {
            if (zone == null || !zone.isActiveAndEnabled)
                continue;

            var zoneBounds = zone.GetWorldBounds();
            FezBackgroundWall.CollectWallsWithMoveTriggerOverlap(zoneBounds, WallBuffer);
            for (var i = 0; i < WallBuffer.Count; i++)
            {
                var wall = WallBuffer[i];
                if (wall == null || !wall.isActiveAndEnabled)
                    continue;
                if (!zone.OverlapsBypassObject(wall))
                    continue;
                if (!results.Contains(wall))
                    results.Add(wall);
            }
        }

        return results.Count > 0;
    }

    void EnsureBypassTrackingForFrame()
    {
        var frame = Time.frameCount;
        if (_bypassStateFrame == frame)
            return;
        _bypassStateFrame = frame;

        var viewForward = GetViewForward();
        if (!viewForward.HasValue)
        {
            _trackedBypassWalls.Clear();
            return;
        }

        RegisterBypassWallsFromContext(viewForward.Value);
        RefreshTrackedBypassWallsForFootPlatforms();
    }

    void RegisterBypassWallsFromContext(Vector3 viewForward)
    {
        if (UsesRouteProbe)
        {
            routeProbe.CopyTouchingWalls(WallBuffer);
            for (var i = 0; i < WallBuffer.Count; i++)
            {
                var wall = WallBuffer[i];
                if (wall != null && DoesWallAffectPlayerRouting(wall, viewForward))
                    _trackedBypassWalls.Add(wall);
            }
            return;
        }

        var fallbackQueryBounds = GetRoutingQueryBounds(viewForward);
        FezBackgroundWall.CollectWallsWithMoveTriggerOverlap(fallbackQueryBounds, WallBuffer);
        for (var i = 0; i < WallBuffer.Count; i++)
        {
            var wall = WallBuffer[i];
            if (wall == null || !OverlapsMoveTrigger(wall, fallbackQueryBounds))
                continue;
            if (!DoesWallAffectPlayerRouting(wall, viewForward))
                continue;

            var routeTarget = wall.GetPlayerRouteTargetDepth(viewForward);
            var playerDeep = FezBackgroundWall.ProjectBoundsDeepDepth(_cc.bounds, viewForward);
            var epsilon = Mathf.Max(0.001f, shallowResolvedEpsilon);
            var needsPush = playerDeep > routeTarget + epsilon;
            var holdingShallow = playerDeep <= routeTarget + epsilon;
            if (needsPush || holdingShallow)
                _trackedBypassWalls.Add(wall);
        }
    }

    void RefreshTrackedBypassWallsForFootPlatforms()
    {
        if (_trackedBypassWalls.Count == 0)
            return;

        FootSurfaceBuffer.Clear();
        if (_depthSnapper != null)
            _depthSnapper.CollectFootPlatformSurfaces(FootSurfaceBuffer);

        PruneTrackedBypassWalls(FootSurfaceBuffer);
    }

    void PruneTrackedBypassWalls(List<FezDepthProxySurface> footSurfaces)
    {
        if (_trackedBypassWalls.Count == 0)
            return;

        BypassWallRemovalBuffer.Clear();
        foreach (var wall in _trackedBypassWalls)
        {
            if (wall == null || !wall.isActiveAndEnabled)
            {
                BypassWallRemovalBuffer.Add(wall);
                continue;
            }

            if (!IsWallTrackedByAnyFootPlatform(wall, footSurfaces))
                BypassWallRemovalBuffer.Add(wall);
        }

        for (var i = 0; i < BypassWallRemovalBuffer.Count; i++)
            _trackedBypassWalls.Remove(BypassWallRemovalBuffer[i]);
    }

    static bool IsWallTrackedByAnyFootPlatform(FezBackgroundWall wall, List<FezDepthProxySurface> footSurfaces)
    {
        if (wall == null || footSurfaces == null)
            return false;

        for (var i = 0; i < footSurfaces.Count; i++)
        {
            var surface = footSurfaces[i];
            if (surface == null)
                continue;

            var zone = surface.GetDepthSnapSuppressZone();
            if (zone != null && zone.isActiveAndEnabled && zone.OverlapsBypassObject(wall))
                return true;
        }

        return false;
    }

    private bool CanRouteByVisibility()
    {
        if (!blockRoutingWhenOccluded || occlusionGate == null)
            return true;
        if (occlusionGate.IsPlayerDepthMovementBlocked)
            return false;

        var viewForward = GetViewForward();
        if (!viewForward.HasValue)
            return true;

        return !occlusionGate.IsOuterViewOccludedForWallRouting(viewForward.Value);
    }

    private bool TryBuildShallowPushMove(Vector3 viewForward, out Vector3 totalMove)
    {
        totalMove = Vector3.zero;
        if (!TryGetShallowestRouteTarget(viewForward, out var shallowestTarget))
            return false;

        var playerDeep = FezBackgroundWall.ProjectBoundsDeepDepth(_cc.bounds, viewForward);
        var epsilon = Mathf.Max(0.001f, shallowResolvedEpsilon);
        if (playerDeep <= shallowestTarget + epsilon)
            return false;

        var deltaDepth = shallowestTarget - playerDeep;
        if (Mathf.Abs(deltaDepth) < 1e-6f)
            return false;

        totalMove = viewForward * deltaDepth;
        return totalMove.sqrMagnitude > 1e-8f;
    }

    private bool TryGetShallowestRouteTarget(Vector3 viewForward, out float shallowestTarget)
    {
        shallowestTarget = float.PositiveInfinity;
        if (!CollectQualifyingWalls(viewForward, QualifyingWallBuffer))
            return false;

        for (var i = 0; i < QualifyingWallBuffer.Count; i++)
        {
            var wall = QualifyingWallBuffer[i];
            if (wall == null)
                continue;

            var target = wall.GetPlayerRouteTargetDepth(viewForward);
            if (target < shallowestTarget)
                shallowestTarget = target;
        }

        return shallowestTarget < float.PositiveInfinity;
    }

    private bool CollectQualifyingWalls(Vector3 viewForward, List<FezBackgroundWall> results)
    {
        results.Clear();
        if (_cc == null)
            return false;

        var playerDeep = FezBackgroundWall.ProjectBoundsDeepDepth(_cc.bounds, viewForward);
        var epsilon = Mathf.Max(0.001f, shallowResolvedEpsilon);

        Bounds fallbackQueryBounds = default;
        if (UsesRouteProbe)
        {
            routeProbe.CopyTouchingWalls(WallBuffer);
        }
        else
        {
            fallbackQueryBounds = GetRoutingQueryBounds(viewForward);
            FezBackgroundWall.CollectWallsWithMoveTriggerOverlap(fallbackQueryBounds, WallBuffer);
        }

        for (var i = 0; i < WallBuffer.Count; i++)
        {
            var wall = WallBuffer[i];
            if (wall == null)
                continue;
            if (!UsesRouteProbe && !OverlapsMoveTrigger(wall, fallbackQueryBounds))
                continue;
            if (!DoesWallAffectPlayerRouting(wall, viewForward))
                continue;

            var routeTarget = wall.GetPlayerRouteTargetDepth(viewForward);
            if (playerDeep <= routeTarget + epsilon)
                continue;

            results.Add(wall);
        }

        return results.Count > 0;
    }

    private Bounds GetRoutingQueryBounds(Vector3 viewForward)
    {
        if (UsesRouteProbe)
            return routeProbe.GetWorldBounds();

        var b = _cc.bounds;
        var lead = Mathf.Max(0f, routeLeadDepth);
        if (lead <= 0.0001f)
            return b;

        var shallowCenter = b.center - viewForward * lead;
        var expanded = new Bounds(shallowCenter, b.size);
        expanded.Encapsulate(b);
        return expanded;
    }

    private Vector3? GetViewForward()
    {
        var viewForward = FezDepthAxis.GetViewForward(depthAxisSource, explicitAxisTransform, explicitCamera);
        if (viewForward.sqrMagnitude < 0.0001f)
            return null;

        viewForward.Normalize();
        return viewForward;
    }

    private bool DoesWallAffectPlayerRouting(FezBackgroundWall wall, Vector3 viewForward)
    {
        if (_cc == null || wall == null)
            return false;

        var wallBounds = wall.GetBodyWorldBounds();
        var playerBounds = _cc.bounds;
        if (!FezBackgroundWall.OverlapsViewColumn(playerBounds, wallBounds, viewForward))
            return false;

        var footY = playerBounds.min.y;
        var headY = playerBounds.max.y;
        var topBelowFeet = Mathf.Max(0.01f, maxWallTopBelowFeet);
        if (wallBounds.max.y < footY - topBelowFeet)
            return false;

        var maxGap = Mathf.Max(0.05f, maxWallBottomAboveFeet);
        if (wallBounds.min.y > footY + maxGap)
            return false;
        if (wallBounds.min.y > headY + 0.05f)
            return false;

        return true;
    }

    private bool OverlapsMoveTrigger(FezBackgroundWall wall, Bounds queryBounds)
    {
        if (UsesRouteProbe)
            return routeProbe.IsTouchingMoveTrigger(wall);

        var triggers = wall.GetMoveTriggerColliders();
        if (triggers.Length == 0)
            return false;

        var b = queryBounds;
        var count = Physics.OverlapBoxNonAlloc(
            b.center, b.extents * 0.98f, OverlapBuffer, Quaternion.identity,
            moveTriggerMask, QueryTriggerInteraction.Collide);

        for (var i = 0; i < count; i++)
        {
            var col = OverlapBuffer[i];
            if (col == null || !col.isTrigger)
                continue;

            for (var j = 0; j < triggers.Length; j++)
            {
                if (col == triggers[j])
                    return true;
            }
        }

        for (var j = 0; j < triggers.Length; j++)
        {
            var col = triggers[j];
            if (col != null && b.Intersects(col.bounds))
                return true;
        }

        return false;
    }
}
