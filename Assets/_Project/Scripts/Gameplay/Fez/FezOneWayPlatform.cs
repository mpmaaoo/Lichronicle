using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 單向平台（One-Way）：從下方往上移動時可穿過，站在頂上時可承載。
/// 掛在 DepthProxy（需有 <see cref="Collider"/>）上；可額外指定「平台本體」碰撞器。
/// 預設上穿時只暫關 DepthProxy，FezPlatform 本體維持碰撞（含跳躍中）。
/// 由 <see cref="PlayerController"/> 在 <c>CharacterController.Move</c> 前後呼叫 <see cref="BeginPlayerMove"/>／<see cref="EndPlayerMove"/>。
/// </summary>
[RequireComponent(typeof(Collider))]
[DisallowMultipleComponent]
public sealed class FezOneWayPlatform : MonoBehaviour
{
    private static readonly List<FezOneWayPlatform> Registered = new List<FezOneWayPlatform>(32);
    private static readonly List<FezOneWayPlatform> DisabledThisMove = new List<FezOneWayPlatform>(8);

    [Header("碰撞器")]
    [Tooltip("勾選且下方陣列為空時，自動把「父物件」上的 Collider 當平台本體（子物件掛 DepthProxy 時常用）。")]
    [SerializeField] private bool autoIncludeParentCollider = true;

    [Tooltip("平台本體的碰撞器（例如父物件的 BoxCollider）；可放多個。")]
    [SerializeField] private Collider[] platformBodyColliders;

    [Tooltip("開啟：上穿時本體也暫關（舊行為）。關閉（預設）：只暫關 DepthProxy，本體跳躍時仍碰撞。")]
    [SerializeField] private bool disableBodyCollidersWhenPassing = false;

    [Tooltip("用哪個 Collider 的 bounds.max.y 當「頂面」高度；不填則優先使用「平台本體」第一個，否則用本物件 DepthProxy。")]
    [SerializeField] private Collider topSurfaceReference;

    [Header("頂面（世界 Y）")]
    [Tooltip("在頂面參考 Collider 的 bounds.max.y 上再加減的偏移；正數＝頂面視為更高。")]
    [SerializeField] private float worldTopYOffset;

    [Tooltip("腳底須比「頂面」低至少這麼多，才視為在平台下方（可向上穿過）。")]
    [SerializeField] private float feetBelowTopMin = 0.08f;

    [Tooltip("垂直速度大於此值才視為「正在上升」，避免水平蹭到側邊時誤判。")]
    [SerializeField] private float riseSpeedThreshold = 0.05f;

    private enum OneWayGateMode
    {
        /// <summary>用腳底與平台頂面的相對位置判定（原本的 belowDeck）。</summary>
        FeetBelowTop = 0,
        /// <summary>用玩家座標（中心點）與平台頂面的相對位置判定：平台頂面高於玩家時才可穿透。</summary>
        PlatformAbovePlayer = 1
    }

    [Header("切換策略")]
    [Tooltip("關閉時：只要角色腳底在頂面下方，就暫時禁用碰撞（不侷限跳躍上升）。\n開啟時：保留舊行為，需要正在上升才禁用。")]
    [SerializeField] private bool gateByVerticalVelocity = false;

    [Header("判定模式")]
    [SerializeField] private OneWayGateMode gateMode = OneWayGateMode.FeetBelowTop;
    [Tooltip("PlatformAbovePlayer 模式用：平台頂面至少要高於玩家中心點這麼多才會可穿透。")]
    [SerializeField] private float platformAbovePlayerMin = 0.05f;
    [Tooltip("防止站在平台上抽搐：當玩家已 grounded 且腳底接近頂面時，強制本幀維持碰撞（不穿透）。")]
    [SerializeField] private bool keepSolidWhenGroundedOnTop = true;

    private Collider _proxyCollider;
    private Collider[] _passThroughColliders;
    private Collider[] _allPlatformColliders;

    private void Awake()
    {
        _proxyCollider = GetComponent<Collider>();
        BuildToggleSet();
    }

    private void OnValidate()
    {
        if (Application.isPlaying)
            return;
        _proxyCollider = GetComponent<Collider>();
        BuildToggleSet();
    }

    private void BuildToggleSet()
    {
        var proxy = _proxyCollider != null ? _proxyCollider : GetComponent<Collider>();
        var bodies = CollectBodyColliders();

        var pass = new List<Collider>(1 + bodies.Count);
        if (proxy != null)
            pass.Add(proxy);

        if (disableBodyCollidersWhenPassing)
        {
            for (var i = 0; i < bodies.Count; i++)
            {
                var c = bodies[i];
                if (c != null && !pass.Contains(c))
                    pass.Add(c);
            }
        }

        var all = new List<Collider>(pass.Count + bodies.Count);
        for (var i = 0; i < pass.Count; i++)
        {
            var c = pass[i];
            if (c != null && !all.Contains(c))
                all.Add(c);
        }

        for (var i = 0; i < bodies.Count; i++)
        {
            var c = bodies[i];
            if (c != null && !all.Contains(c))
                all.Add(c);
        }

        _passThroughColliders = pass.ToArray();
        _allPlatformColliders = all.ToArray();
    }

    private List<Collider> CollectBodyColliders()
    {
        var bodies = new List<Collider>(2);
        var extras = platformBodyColliders;
        if (extras != null)
        {
            for (var i = 0; i < extras.Length; i++)
            {
                var c = extras[i];
                if (IsUsableBodyCollider(c) && !bodies.Contains(c))
                    bodies.Add(c);
            }
        }

        if (autoIncludeParentCollider && transform.parent != null &&
            (extras == null || extras.Length == 0))
        {
            var root = transform.parent;
            var cols = root.GetComponentsInChildren<Collider>(includeInactive: false);
            for (var i = 0; i < cols.Length; i++)
            {
                var c = cols[i];
                if (!IsUsableBodyCollider(c) || c == _proxyCollider || bodies.Contains(c))
                    continue;
                if (c.GetComponent<FezOneWayPlatform>() != null)
                    continue;
                bodies.Add(c);
            }
        }

        return bodies;
    }

    private static bool IsUsableBodyCollider(Collider c)
    {
        return c != null && c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger;
    }

    private Collider GetTopReference()
    {
        if (topSurfaceReference != null)
            return topSurfaceReference;

        // 頂面以「可走平台」為準：優先本體（通常比透明箱貼地）
        if (platformBodyColliders != null)
        {
            for (var i = 0; i < platformBodyColliders.Length; i++)
            {
                var c = platformBodyColliders[i];
                if (c != null)
                    return c;
            }
        }

        if (autoIncludeParentCollider && transform.parent != null)
        {
            var cols = transform.parent.GetComponentsInChildren<Collider>(includeInactive: false);
            for (var i = 0; i < cols.Length; i++)
            {
                var c = cols[i];
                if (IsUsableBodyCollider(c) && c.GetComponent<FezOneWayPlatform>() == null)
                    return c;
            }
        }

        if (_proxyCollider != null)
            return _proxyCollider;
        return GetComponent<Collider>();
    }

    private void OnEnable()
    {
        if (!Registered.Contains(this))
            Registered.Add(this);
    }

    private void OnDisable()
    {
        Registered.Remove(this);
        EnablePassThroughColliders();
    }

    private void EnablePassThroughColliders()
    {
        if (_passThroughColliders == null)
            return;
        for (var i = 0; i < _passThroughColliders.Length; i++)
        {
            var c = _passThroughColliders[i];
            if (c != null)
                c.enabled = true;
        }
    }

    private void EnsurePassThroughCollidersEnabledIfNeeded()
    {
        if (_passThroughColliders == null)
            return;
        for (var i = 0; i < _passThroughColliders.Length; i++)
        {
            var c = _passThroughColliders[i];
            if (c != null && !c.enabled)
                c.enabled = true;
        }
    }

    /// <summary>在 <c>Move</c> 之前呼叫：符合條件的平台會暫時關閉相關 Collider。</summary>
    public static void BeginPlayerMove(CharacterController cc, float worldVelocityY)
    {
        DisabledThisMove.Clear();
        if (cc == null)
            return;

        var feetY = cc.bounds.min.y;
        var playerY = cc.bounds.center.y;

        for (var i = 0; i < Registered.Count; i++)
        {
            var p = Registered[i];
            if (p == null || !p.isActiveAndEnabled)
                continue;

            var topRef = p.GetTopReference();
            if (topRef == null)
                continue;

            var topY = topRef.bounds.max.y + p.worldTopYOffset;
            bool allowPass;
            switch (p.gateMode)
            {
                case OneWayGateMode.PlatformAbovePlayer:
                    allowPass = topY > playerY + p.platformAbovePlayerMin;
                    if (p.keepSolidWhenGroundedOnTop && cc.isGrounded)
                    {
                        // 站在頂面附近時，避免平台被關閉導致「掉下去又被抓回來」的上下抽搐。
                        var standingOnTop = feetY >= topY - p.feetBelowTopMin;
                        if (standingOnTop)
                            allowPass = false;
                    }
                    break;
                default:
                    allowPass = feetY < topY - p.feetBelowTopMin;
                    if (p.keepSolidWhenGroundedOnTop && cc.isGrounded)
                    {
                        var standingOnTop = feetY >= topY - p.feetBelowTopMin;
                        if (standingOnTop)
                            allowPass = false;
                    }
                    break;
            }
            var rising = worldVelocityY > p.riseSpeedThreshold;
            var shouldDisable = allowPass && (!p.gateByVerticalVelocity || rising);
            // 從上方落下時不可關閉承載碰撞（薄片頂面 + 腳底微穿入時，否則會整幀像穿透）
            if (worldVelocityY < -p.riseSpeedThreshold)
                shouldDisable = false;

            if (shouldDisable)
            {
                p.SetPassThroughCollidersEnabled(false);
                DisabledThisMove.Add(p);
            }
            else
            {
                p.EnsurePassThroughCollidersEnabledIfNeeded();
            }
        }
    }

    /// <summary>在 <c>Move</c> 之後呼叫：還原本幀關閉的 Collider。</summary>
    public static void EndPlayerMove()
    {
        for (var i = 0; i < DisabledThisMove.Count; i++)
        {
            var p = DisabledThisMove[i];
            if (p != null)
                p.EnablePassThroughColliders();
        }

        DisabledThisMove.Clear();
    }

    /// <summary>由任一 collider 反查所屬的 one-way 平台（包含本體、proxy、自動父層）。</summary>
    public static bool TryResolveByCollider(Collider col, out FezOneWayPlatform platform)
    {
        platform = null;
        if (col == null)
            return false;

        for (var i = 0; i < Registered.Count; i++)
        {
            var p = Registered[i];
            if (p == null || !p.isActiveAndEnabled || p._allPlatformColliders == null)
                continue;

            var all = p._allPlatformColliders;
            for (var j = 0; j < all.Length; j++)
            {
                if (all[j] == col)
                {
                    platform = p;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>回傳這個平台目前會一起切換/忽略的碰撞器集合。</summary>
    public Collider[] GetAllPlatformColliders()
    {
        return _allPlatformColliders;
    }

    private void SetPassThroughCollidersEnabled(bool enabled)
    {
        if (_passThroughColliders == null)
            return;
        for (var i = 0; i < _passThroughColliders.Length; i++)
        {
            var c = _passThroughColliders[i];
            if (c != null)
                c.enabled = enabled;
        }
    }
}
