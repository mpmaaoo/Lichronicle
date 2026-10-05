using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
[DefaultExecutionOrder(250)]
public class PlayerController : MonoBehaviour
{
    private const float GridSize = 1f;

    [Header("移動參數")]
    public float moveSpeed = 8f;
    public float jumpHeight = 5f;
    public float gravity = -25f;

    [Header("下落")]
    [Tooltip("向下終端速度上限（公尺／秒，取正值）；超過此下落速度不再增加。設為 0 或負數則不限制。")]
    public float maxFallSpeed = 38f;

    [Header("跳躍寬容")]
    public float coyoteTime = 0.1f;
    public float jumpBufferTime = 0.1f;
    [Tooltip("判定為按下方向下（S／搖桿下）的輸入閾值。")]
    public float downInputThreshold = -0.5f;
    [Tooltip("手把搖桿死區（GetAxisRaw 不會套用 InputManager deadzone）。")]
    public float stickDeadzone = 0.25f;

    [Header("下穿（S＋空白）")]
    [Tooltip("下穿時沿深度軸往淺側偏移的格數（1 格＝1 公尺）。")]
    [SerializeField] private float dropThroughShallowCells = 1f;
    [Tooltip("安全逾時：超過後強制離開原平台碰撞體再恢復。")]
    [SerializeField] private float dropThroughSafetyTimeout = 1.2f;
    [Tooltip("開始下穿時立刻硬往下擠的距離（格）。")]
    [SerializeField] private float dropThroughInitialDigCells = 0.25f;
    [Tooltip("腳下探測半徑／距離。")]
    [SerializeField] private float dropThroughFootProbeRadius = 0.28f;
    [SerializeField] private float dropThroughFootProbeDistance = 0.45f;
    [Tooltip("下穿初速（公尺／秒）。")]
    [SerializeField] private float dropThroughDownwardBoost = 3.5f;
    [Tooltip("仍卡在原平台碰撞體內時的穿出速度（僅此期間，穿出即恢復正常重力）。")]
    [SerializeField] private float dropThroughEscapeSpeed = 4.5f;
    [SerializeField] private FezDepthAxisSource dropDepthAxisSource = FezDepthAxisSource.WorldViewState;
    [SerializeField] private Transform dropExplicitAxisTransform;
    [SerializeField] private Camera dropExplicitCamera;

    [Header("加減速")]
    public float acceleration = 25f;
    public float deceleration = 35f;

    [Header("方向參考（通常是 CameraPivot）")]
    public Transform movementPlane;

    [Header("FezPlatform 本體碰撞")]
    [Tooltip("開啟後與 FezPlatform 圖層有實體碰撞。")]
    [SerializeField] private bool collideWithFezPlatformBodies = true;
    [SerializeField] private string fezPlatformLayerName = "FezPlatform";
    [SerializeField] private string depthProxyLayerName = "DepthProxy";

    [Header("背景牆碰撞")]
    [Tooltip("背景牆僅參與遮擋與深度繞行，不應以實體碰撞擋住玩家。")]
    [SerializeField] private bool ignoreBackgroundWallPhysics = true;
    [SerializeField] private string backgroundWallLayerName = "BackGroundWall";

    private CharacterController _controller;
    private Vector3 _velocity;
    private bool _isGrounded;

    private float _coyoteCounter;
    private float _jumpBufferCounter;
    private float _currentSpeed;

    // 下穿：暫時關閉（enabled=false）腳下那一格的碰撞，比 IgnoreCollision 可靠
    private readonly List<Collider> _dropDisabledColliders = new List<Collider>(8);
    private readonly HashSet<Collider> _dropDisabledSet = new HashSet<Collider>();
    private Collider[] _playerSolidColliders;
    private bool _dropThroughActive;
    private bool _pendingEndDropThrough;
    private float _dropThroughExpireAt = -999f;
    private float _dropStartFeetY;
    private float _dropPlatformMaxTopY = float.NegativeInfinity;
    private float _dropPlatformMinBottomY = float.PositiveInfinity;

    private int _fezPlatformLayer = -1;
    private int _depthProxyLayer = -1;
    private int _backgroundWallLayer = -1;

    /// <summary>
    /// 下穿進行中：DepthSnap／DepthProxy 跟隨／牆繞行應暫停，否則會被吸回平台本體深度。
    /// </summary>
    public static bool IsDropThroughActiveStatic { get; private set; }

    private void Awake()
    {
        _controller = GetComponent<CharacterController>();
        if (movementPlane == null)
            movementPlane = transform;

        _fezPlatformLayer = LayerMask.NameToLayer(fezPlatformLayerName);
        _depthProxyLayer = LayerMask.NameToLayer(depthProxyLayerName);
        _backgroundWallLayer = LayerMask.NameToLayer(backgroundWallLayerName);
        CachePlayerSolidColliders();
        if (collideWithFezPlatformBodies)
            EnableFezPlatformLayerCollisions();
        if (ignoreBackgroundWallPhysics)
            DisableBackgroundWallPhysicsCollisions();
    }

    private void CachePlayerSolidColliders()
    {
        var all = GetComponentsInChildren<Collider>(true);
        var list = new List<Collider>(all.Length);
        for (var i = 0; i < all.Length; i++)
        {
            var c = all[i];
            if (c == null || c.isTrigger)
                continue;
            // DepthSnapSuppressZone／探測箱不應參與下穿 Ignore 集合判斷
            if (c.GetComponentInParent<FezPlatformDepthSnapSuppressZone>() != null)
                continue;
            if (c.GetComponentInParent<FezPlayerDepthSnapSuppressZone>() != null)
                continue;
            list.Add(c);
        }

        _playerSolidColliders = list.ToArray();
    }

    private void Start()
    {
        if (collideWithFezPlatformBodies)
            EnableFezPlatformLayerCollisions();
        if (ignoreBackgroundWallPhysics)
            DisableBackgroundWallPhysicsCollisions();
    }

    private void DisableBackgroundWallPhysicsCollisions()
    {
        if (_backgroundWallLayer < 0 || _controller == null)
            return;

        var playerLayer = _controller.gameObject.layer;
        Physics.IgnoreLayerCollision(playerLayer, _backgroundWallLayer, true);

        var namedPlayerLayer = LayerMask.NameToLayer("player");
        if (namedPlayerLayer >= 0 && namedPlayerLayer != playerLayer)
            Physics.IgnoreLayerCollision(namedPlayerLayer, _backgroundWallLayer, true);
    }

    private void EnableFezPlatformLayerCollisions()
    {
        if (_fezPlatformLayer < 0 || _controller == null)
            return;

        Physics.IgnoreLayerCollision(_controller.gameObject.layer, _fezPlatformLayer, false);

        var playerLayer = LayerMask.NameToLayer("player");
        if (playerLayer >= 0)
            Physics.IgnoreLayerCollision(playerLayer, _fezPlatformLayer, false);

        Physics.IgnoreLayerCollision(0, _fezPlatformLayer, false);
    }

    private void Update()
    {
        _isGrounded = _controller.isGrounded;
        if (_isGrounded)
        {
            _coyoteCounter = coyoteTime;
            if (_velocity.y < 0f && !IsDropThroughActive())
                _velocity.y = -1f;
        }
        else
        {
            _coyoteCounter -= Time.deltaTime;
        }

        if (_dropThroughActive)
            TickDropThrough();

        float inputX = ApplyAxisDeadzone(Input.GetAxisRaw("Horizontal"), stickDeadzone);
        float inputZ = ApplyAxisDeadzone(Input.GetAxisRaw("Vertical"), stickDeadzone);

        Vector3 input = new Vector3(inputX, 0f, 0f);
        float inputMagnitude = Mathf.Clamp01(Mathf.Abs(input.x));
        if (inputMagnitude > 0.01f)
            input.x = Mathf.Sign(input.x);

        Vector3 moveDir =
            movementPlane.right * input.x +
            movementPlane.forward * input.z;

        moveDir.y = 0f;
        if (moveDir.sqrMagnitude > 0.0001f)
            moveDir.Normalize();
        else
            moveDir = Vector3.zero;

        float targetSpeed = inputMagnitude * moveSpeed;
        float accel = inputMagnitude > 0.01f ? acceleration : deceleration;
        _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSpeed, accel * Time.deltaTime);

        Vector3 horizontalVelocity = moveDir * _currentSpeed;

        if (Input.GetButtonDown("Jump") || Input.GetKeyDown(KeyCode.Space))
        {
            var holdingDown = inputZ <= downInputThreshold
                || Input.GetKey(KeyCode.S)
                || Input.GetKey(KeyCode.DownArrow);
            var wantsDropThrough = holdingDown && _coyoteCounter > 0f;
            if (wantsDropThrough)
            {
                _jumpBufferCounter = 0f;
                BeginDropThrough();
            }
            else
            {
                _jumpBufferCounter = jumpBufferTime;
            }
        }
        else
        {
            _jumpBufferCounter -= Time.deltaTime;
        }

        if (_jumpBufferCounter > 0f && _coyoteCounter > 0f)
        {
            _jumpBufferCounter = 0f;
            _velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        _velocity.y += gravity * Time.deltaTime;
        if (maxFallSpeed > 0f)
            _velocity.y = Mathf.Max(_velocity.y, -maxFallSpeed);

        // 僅在「還卡在原平台體內」時用穿出速度；一脫離就用正常重力，不再全程限速
        if (_dropThroughActive && StillOverlappingDisabledPlatforms())
            _velocity.y = -Mathf.Max(2.5f, dropThroughEscapeSpeed);

        Vector3 finalVelocity = horizontalVelocity;
        finalVelocity.y = _velocity.y;

        // 下穿全程暫停 Proxy／牆跟隨，結束後才恢復（避免半路吸回／補頂）
        if (!IsDropThroughActiveStatic)
        {
            FezBackgroundWallMoveTriggerFollower.SyncAllBeforeCharacterControllerMove(Time.deltaTime);
            FezDepthProxyFollower.SyncAllBeforeCharacterControllerMove(Time.deltaTime);
        }

        FezOneWayPlatform.BeginPlayerMove(_controller, finalVelocity.y);
        ReassertDropDisabledColliders();
        try
        {
            CollisionFlags flags = _controller.Move(finalVelocity * Time.deltaTime);

            // 下穿中頭頂撞到原平台內側時不要把速度歸零，否則會卡在中間
            if ((flags & CollisionFlags.Above) != 0)
            {
                if (_dropThroughActive)
                    _velocity.y = -Mathf.Max(2.5f, dropThroughEscapeSpeed);
                else if (_velocity.y > 0f)
                    _velocity.y = 0f;
            }
        }
        finally
        {
            FezOneWayPlatform.EndPlayerMove();
            ReassertDropDisabledColliders();
        }
    }

    private void OnDisable()
    {
        EndDropThrough();
    }

    static float ApplyAxisDeadzone(float value, float deadzone)
    {
        var dz = Mathf.Clamp01(deadzone);
        if (Mathf.Abs(value) < dz)
            return 0f;
        return value;
    }

    /// <summary>
    /// S＋空白：往淺側平移 1 格，並暫時關掉腳下那一格平台碰撞以穿透落下。
    /// </summary>
    private void BeginDropThrough()
    {
        if (_controller == null)
            return;

        EndDropThrough();

        // 先標示進行中，避免後續 Sync／Snap 立刻把人吸回平台中心
        _dropThroughActive = true;
        IsDropThroughActiveStatic = true;

        // 1) 先關掉腳下承載碰撞（避免下一步淺移被本體卡住）
        DisableFootPlatformColliders();

        // 2) 往淺（朝鏡頭／較小 Depth）平移 N 格 —— 下穿期間禁止 DepthProxy 跟隨
        var viewForward = FezDepthAxis.GetViewForward(
            dropDepthAxisSource, dropExplicitAxisTransform, dropExplicitCamera);
        viewForward.y = 0f;
        if (viewForward.sqrMagnitude > 0.0001f)
        {
            viewForward.Normalize();
            var shallowDelta = -viewForward * (GridSize * Mathf.Max(0f, dropThroughShallowCells));
            _controller.Move(shallowDelta);
        }

        // 淺移後再掃一次腳下（可能碰到其他格），繼續暫時關閉
        DisableFootPlatformColliders();

        // 3) 硬往下擠一小段
        var dig = GridSize * Mathf.Max(0.1f, dropThroughInitialDigCells);
        _controller.Move(Vector3.down * dig);
        ReassertDropDisabledColliders();

        // 4) 初速
        _velocity.y = -Mathf.Max(2f, dropThroughDownwardBoost);

        _dropStartFeetY = _controller.bounds.min.y;
        _dropThroughExpireAt = Time.time + Mathf.Max(0.25f, dropThroughSafetyTimeout);
    }

    private void LateUpdate()
    {
        // Snapper（order 200）之後再建碰撞，避免「剛恢復就被 Snap 吸回磚內」
        if (_pendingEndDropThrough)
        {
            _pendingEndDropThrough = false;
            // 再往下留一點空隙後才真正恢復
            if (_controller != null)
                _controller.Move(Vector3.down * 0.12f);
            FinishDropThroughRestore();
            return;
        }

        if (_dropThroughActive)
            ReassertDropDisabledColliders();
    }

    private void DisableFootPlatformColliders()
    {
        var b = _controller.bounds;
        var feet = new Vector3(b.center.x, b.min.y, b.center.z);
        var radius = Mathf.Max(0.1f, dropThroughFootProbeRadius);
        var probe = Mathf.Max(0.15f, dropThroughFootProbeDistance);

        float? closestTop = null;
        var maxTop = float.NegativeInfinity;
        const float sameLayerSlack = 0.25f;

        // Overlap：站在平台上時不依賴 SphereCast（起點常埋在碰撞體內）
        GatherAndDisableFromOverlap(feet + Vector3.up * 0.08f, radius, ref closestTop, ref maxTop, sameLayerSlack);
        GatherAndDisableFromOverlap(feet + Vector3.down * 0.1f, radius, ref closestTop, ref maxTop, sameLayerSlack);

        // 後援：從腳上方往下掃
        if (_dropDisabledColliders.Count == 0)
        {
            var castOrigin = feet + Vector3.up * 0.5f;
            var hits = Physics.SphereCastAll(
                castOrigin,
                radius * 0.75f,
                Vector3.down,
                0.5f + probe,
                ~0,
                QueryTriggerInteraction.Collide);
            if (hits != null)
            {
                System.Array.Sort(hits, (a, bh) => a.distance.CompareTo(bh.distance));
                for (var i = 0; i < hits.Length; i++)
                    TryDisableFootPlatform(hits[i].collider, ref closestTop, ref maxTop, sameLayerSlack);
            }
        }

        // 第二次掃描若沒掃到，不要覆寫掉既有的平台頂／底
        if (maxTop > float.NegativeInfinity)
            _dropPlatformMaxTopY = Mathf.Max(_dropPlatformMaxTopY, maxTop);

        RefreshDisabledPlatformBottom();
    }

    private void RefreshDisabledPlatformBottom()
    {
        var minBottom = float.PositiveInfinity;
        for (var i = 0; i < _dropDisabledColliders.Count; i++)
        {
            var col = _dropDisabledColliders[i];
            if (col == null)
                continue;
            minBottom = Mathf.Min(minBottom, col.bounds.min.y);
        }

        if (minBottom < float.PositiveInfinity)
            _dropPlatformMinBottomY = minBottom;
    }

    private void GatherAndDisableFromOverlap(
        Vector3 center,
        float radius,
        ref float? closestTop,
        ref float maxTop,
        float sameLayerSlack)
    {
        var cols = Physics.OverlapSphere(center, radius, ~0, QueryTriggerInteraction.Collide);
        if (cols == null)
            return;
        for (var i = 0; i < cols.Length; i++)
            TryDisableFootPlatform(cols[i], ref closestTop, ref maxTop, sameLayerSlack);
    }

    private void TryDisableFootPlatform(
        Collider col,
        ref float? closestTop,
        ref float maxTop,
        float sameLayerSlack)
    {
        if (col == null || col == (Collider)_controller)
            return;
        if (!LooksLikeFootPlatform(col))
            return;

        var topY = col.bounds.max.y;
        if (!closestTop.HasValue)
            closestTop = topY;
        else if (topY < closestTop.Value - sameLayerSlack)
            return; // 下一層

        if (!TryDisablePlatformColliders(col, out var platformTop))
            return;

        if (platformTop > maxTop)
            maxTop = platformTop;
        if (!closestTop.HasValue || platformTop > closestTop.Value)
            closestTop = platformTop;
    }

    private bool LooksLikeFootPlatform(Collider col)
    {
        if (col.GetComponentInParent<FezOneWayPlatform>() != null)
            return true;
        if (col.GetComponentInParent<FezDepthProxySurface>() != null)
            return true;
        if (FezOneWayPlatform.TryResolveByCollider(col, out _))
            return true;
        return IsPlatformSupportCollider(col);
    }

    private bool TryDisablePlatformColliders(Collider hitCol, out float platformTopY)
    {
        platformTopY = float.NegativeInfinity;
        var any = false;

        var oneWay = hitCol.GetComponentInParent<FezOneWayPlatform>();
        if (oneWay == null)
            FezOneWayPlatform.TryResolveByCollider(hitCol, out oneWay);

        if (oneWay != null)
        {
            var cols = oneWay.GetAllPlatformColliders();
            if (cols == null)
                return false;

            for (var i = 0; i < cols.Length; i++)
            {
                if (DisableColliderForDrop(cols[i]))
                    any = true;
                if (cols[i] != null)
                    platformTopY = Mathf.Max(platformTopY, cols[i].bounds.max.y);
            }

            return any;
        }

        if (!IsPlatformSupportCollider(hitCol))
            return false;

        var surface = hitCol.GetComponentInParent<FezDepthProxySurface>();
        Transform blockRoot;
        if (surface != null)
            blockRoot = surface.transform.parent != null ? surface.transform.parent : surface.transform;
        else
            blockRoot = hitCol.transform;

        var all = blockRoot.GetComponentsInChildren<Collider>(true);
        for (var i = 0; i < all.Length; i++)
        {
            var pc = all[i];
            if (pc == null)
                continue;
            if (!IsPlatformSupportCollider(pc) && pc != hitCol)
                continue;
            if (DisableColliderForDrop(pc))
                any = true;
            platformTopY = Mathf.Max(platformTopY, pc.bounds.max.y);
        }

        return any;
    }

    private bool DisableColliderForDrop(Collider col)
    {
        if (col == null)
            return false;
        if (!_dropDisabledSet.Add(col))
            return false;

        if (col.enabled)
            col.enabled = false;

        // 玩家身上可能有多個實體 Collider：都要 Ignore，否則 CC 穿過去了卻被別的箱子卡住
        if (_playerSolidColliders == null || _playerSolidColliders.Length == 0)
            CachePlayerSolidColliders();

        for (var i = 0; i < _playerSolidColliders.Length; i++)
        {
            var pc = _playerSolidColliders[i];
            if (pc == null || pc == col)
                continue;
            Physics.IgnoreCollision(pc, col, true);
        }

        _dropDisabledColliders.Add(col);
        return true;
    }

    /// <summary>
    /// FezOneWayPlatform 每幀會 Enable 回平台碰撞；下穿期間必須再次關掉。
    /// </summary>
    private void ReassertDropDisabledColliders()
    {
        if (!_dropThroughActive || _controller == null)
            return;

        if (_playerSolidColliders == null || _playerSolidColliders.Length == 0)
            CachePlayerSolidColliders();

        for (var i = 0; i < _dropDisabledColliders.Count; i++)
        {
            var col = _dropDisabledColliders[i];
            if (col == null)
                continue;
            if (col.enabled)
                col.enabled = false;

            for (var j = 0; j < _playerSolidColliders.Length; j++)
            {
                var pc = _playerSolidColliders[j];
                if (pc == null || pc == col)
                    continue;
                Physics.IgnoreCollision(pc, col, true);
            }
        }
    }

    private bool IsPlatformSupportCollider(Collider col)
    {
        if (col == null)
            return false;

        var layer = col.gameObject.layer;
        if (_fezPlatformLayer >= 0 && layer == _fezPlatformLayer)
            return true;
        if (_depthProxyLayer >= 0 && layer == _depthProxyLayer)
            return true;
        if (col.CompareTag("DepthProxy"))
            return true;
        if (col.GetComponentInParent<FezDepthProxySurface>() != null)
            return true;
        if (col.GetComponentInParent<FezOneWayPlatform>() != null)
            return true;

        return false;
    }

    private void TickDropThrough()
    {
        if (!_dropThroughActive || _pendingEndDropThrough)
            return;

        RefreshDisabledPlatformBottom();

        var headY = _controller.bounds.max.y;
        var feetY = _controller.bounds.min.y;
        var fallen = _dropStartFeetY - feetY;

        // 必須整個人低於原平台底面（或至少往下超過約 1 格）
        var verticallyClear = false;
        if (_dropPlatformMinBottomY < float.PositiveInfinity)
            verticallyClear = headY <= _dropPlatformMinBottomY - 0.12f;
        else
            verticallyClear = fallen >= GridSize * 1.1f;

        if (verticallyClear)
        {
            // 同幀先不要恢復碰撞；等 LateUpdate（Snapper 之後）再建
            _pendingEndDropThrough = true;
            return;
        }

        _velocity.y = -Mathf.Max(2.5f, dropThroughEscapeSpeed);

        if (Time.time < _dropThroughExpireAt)
            return;

        // 逾時：強制把頭移出底面，再排程結束
        if (_dropPlatformMinBottomY < float.PositiveInfinity)
        {
            var targetHeadY = _dropPlatformMinBottomY - 0.12f;
            var delta = headY - targetHeadY;
            if (delta > 0f)
            {
                _controller.Move(Vector3.down * delta);
                ReassertDropDisabledColliders();
            }
        }
        else
        {
            var need = GridSize * 1.1f - fallen;
            if (need > 0f)
                _controller.Move(Vector3.down * need);
        }

        _pendingEndDropThrough = true;
    }

    private bool StillOverlappingDisabledPlatforms()
    {
        if (_controller == null || _dropDisabledColliders.Count == 0)
            return false;

        if (_playerSolidColliders == null || _playerSolidColliders.Length == 0)
            CachePlayerSolidColliders();

        var playerBounds = _controller.bounds;
        for (var i = 0; i < _playerSolidColliders.Length; i++)
        {
            var pc = _playerSolidColliders[i];
            if (pc == null || !pc.enabled)
                continue;
            playerBounds.Encapsulate(pc.bounds);
        }

        for (var i = 0; i < _dropDisabledColliders.Count; i++)
        {
            var col = _dropDisabledColliders[i];
            if (col == null)
                continue;
            if (playerBounds.Intersects(col.bounds))
                return true;
        }

        return false;
    }

    private void EndDropThrough()
    {
        _pendingEndDropThrough = false;
        FinishDropThroughRestore();
    }

    private void FinishDropThroughRestore()
    {
        if (_playerSolidColliders == null || _playerSolidColliders.Length == 0)
            CachePlayerSolidColliders();

        // 恢復前若仍重疊，再往下擠開，減少偶發卡磚
        for (var i = 0; i < 10 && StillOverlappingDisabledPlatforms(); i++)
            _controller.Move(Vector3.down * 0.1f);

        for (var i = 0; i < _dropDisabledColliders.Count; i++)
        {
            var col = _dropDisabledColliders[i];
            if (col == null)
                continue;

            for (var j = 0; j < _playerSolidColliders.Length; j++)
            {
                var pc = _playerSolidColliders[j];
                if (pc == null || pc == col)
                    continue;
                Physics.IgnoreCollision(pc, col, false);
            }

            if (!col.enabled)
                col.enabled = true;
        }

        _dropDisabledColliders.Clear();
        _dropDisabledSet.Clear();
        _dropThroughActive = false;
        IsDropThroughActiveStatic = false;
        _dropStartFeetY = 0f;
        _dropPlatformMaxTopY = float.NegativeInfinity;
        _dropPlatformMinBottomY = float.PositiveInfinity;
        _dropThroughExpireAt = -999f;
    }

    private bool IsDropThroughActive() => _dropThroughActive || _pendingEndDropThrough;
}
