using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
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
    [Tooltip("按 S + 空白時，暫時忽略平台碰撞的秒數。")]
    public float dropThroughDuration = 0.22f;
    [Tooltip("判定為按下方向下（S／搖桿下）的輸入閾值。")]
    public float downInputThreshold = -0.5f;
    [Tooltip("手把搖桿死區（GetAxisRaw 不會套用 InputManager deadzone）。")]
    public float stickDeadzone = 0.25f;
    [Tooltip("下穿時是否額外施加向下速度。")]
    public bool addDropThroughDownwardBoost = false;
    [Tooltip("僅在 addDropThroughDownwardBoost 開啟時使用。")]
    public float dropThroughDownwardBoost = 8f;

    [Header("加減速")]
    public float acceleration = 25f;
    public float deceleration = 35f;

    [Header("方向參考（通常是 CameraPivot）")]
    public Transform movementPlane; // 這個的 Y 旋轉會跟著你現在的視角旋轉

    [Header("FezPlatform 本體碰撞")]
    [Tooltip("開啟後與 FezPlatform 圖層有實體碰撞；S+空白下穿期間暫時忽略該圖層。")]
    [SerializeField] private bool collideWithFezPlatformBodies = true;
    [SerializeField] private string fezPlatformLayerName = "FezPlatform";

    [Header("背景牆碰撞")]
    [Tooltip("背景牆僅參與遮擋與深度繞行，不應以實體碰撞擋住玩家（否則無法沿深度軸繞過）。")]
    [SerializeField] private bool ignoreBackgroundWallPhysics = true;
    [SerializeField] private string backgroundWallLayerName = "BackGroundWall";

    private CharacterController _controller;
    private Vector3 _velocity;
    private bool _isGrounded;

    private float _coyoteCounter;
    private float _jumpBufferCounter;
    private float _currentSpeed;
    private float _ignoreCollisionUntil = -999f;
    private readonly System.Collections.Generic.HashSet<Collider> _ignoredPlatformColliders = new System.Collections.Generic.HashSet<Collider>();
    private int _fezPlatformLayer = -1;
    private bool _fezPlatformLayerIgnored;
    private int _backgroundWallLayer = -1;

    private void Awake()
    {
        _controller = GetComponent<CharacterController>();
        if (movementPlane == null)
        {
            movementPlane = transform; // 沒設定就用自己，先避免報錯
        }

        _fezPlatformLayer = LayerMask.NameToLayer(fezPlatformLayerName);
        _backgroundWallLayer = LayerMask.NameToLayer(backgroundWallLayerName);
        if (collideWithFezPlatformBodies)
            EnableFezPlatformLayerCollisions();
        if (ignoreBackgroundWallPhysics)
            DisableBackgroundWallPhysicsCollisions();
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
        _fezPlatformLayerIgnored = false;
    }

    private void Update()
    {
        if (collideWithFezPlatformBodies)
            SetFezPlatformLayerCollisionIgnored(IsDropThroughActive());

        if (Time.time <= _ignoreCollisionUntil)
            RefreshDropThroughIgnores();
        else if (_ignoredPlatformColliders.Count > 0)
            RestoreIgnoredPlatformCollisions();

        // --- 地面偵測 + 土狼跳計時 ---
        _isGrounded = _controller.isGrounded;
        if (_isGrounded)
        {
            _coyoteCounter = coyoteTime;
            // 下穿期間不要把 y 速度重設成 -1，避免角色被「黏」在原平台上。
            if (_velocity.y < 0f && !IsDropThroughActive())
            {
                _velocity.y = -1f;
            }
        }
        else
        {
            _coyoteCounter -= Time.deltaTime;
        }

        // --- 讀取輸入（鍵盤 A/D、空白；PS5：左搖桿、✕、L1/R1）---
        // GetAxisRaw 不吃 InputManager deadzone，手把需自行過濾漂移。
        float inputX = ApplyAxisDeadzone(Input.GetAxisRaw("Horizontal"), stickDeadzone);
        float inputZ = ApplyAxisDeadzone(Input.GetAxisRaw("Vertical"), stickDeadzone); // 僅給下穿判定

        // 移動只吃左右，不吃前後（W/S／搖桿上下）
        Vector3 input = new Vector3(inputX, 0f, 0f);
        float inputMagnitude = Mathf.Clamp01(Mathf.Abs(input.x));
        if (inputMagnitude > 0.01f)
            input.x = Mathf.Sign(input.x);

        // --- 把輸入轉成「當前平面」方向 ---
        Vector3 moveDir =
            movementPlane.right * input.x +
            movementPlane.forward * input.z; // input.z 固定 0，W/S 不會造成移動

        moveDir.y = 0f;
        if (moveDir.sqrMagnitude > 0.0001f)
            moveDir.Normalize();
        else
            moveDir = Vector3.zero;

        // --- 加減速 ---
        float targetSpeed = inputMagnitude * moveSpeed;
        float accel = inputMagnitude > 0.01f ? acceleration : deceleration;
        _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSpeed, accel * Time.deltaTime);

        Vector3 horizontalVelocity = moveDir * _currentSpeed;

        // --- 快取跳：記錄最近一次按跳 ---
        if (Input.GetButtonDown("Jump"))
        {
            var wantsDropThrough = inputZ <= downInputThreshold && _coyoteCounter > 0f;
            if (wantsDropThrough)
            {
                _ignoreCollisionUntil = Time.time + Mathf.Max(0.02f, dropThroughDuration);
                RefreshDropThroughIgnores();
                _jumpBufferCounter = 0f; // 下穿不觸發一般跳躍
                if (addDropThroughDownwardBoost)
                    _velocity.y = Mathf.Min(_velocity.y, -Mathf.Max(0.1f, dropThroughDownwardBoost));
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

        // --- 判斷是否要跳 ---
        if (_jumpBufferCounter > 0f && _coyoteCounter > 0f)
        {
            _jumpBufferCounter = 0f;
            _velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        // --- 重力 ---
        _velocity.y += gravity * Time.deltaTime;
        if (maxFallSpeed > 0f)
            _velocity.y = Mathf.Max(_velocity.y, -maxFallSpeed);

        // --- 合併水平與垂直速度並移動 ---
        Vector3 finalVelocity = horizontalVelocity;
        finalVelocity.y = _velocity.y;

        // 階段 1：DepthProxy 對齊目前玩家深度，再 Move（Follow Speed=0 仍為瞬間）
        FezBackgroundWallMoveTriggerFollower.SyncAllBeforeCharacterControllerMove(Time.deltaTime);
        FezDepthProxyFollower.SyncAllBeforeCharacterControllerMove(Time.deltaTime);

        FezOneWayPlatform.BeginPlayerMove(_controller, finalVelocity.y);
        try
        {
            CollisionFlags flags = _controller.Move(finalVelocity * Time.deltaTime);

            // 撞到天花板時，取消向上速度，避免繼續往上推
            if ((flags & CollisionFlags.Above) != 0 && _velocity.y > 0f)
            {
                _velocity.y = 0f;
            }
        }
        finally
        {
            FezOneWayPlatform.EndPlayerMove();
        }
    }

    private void OnDisable()
    {
        RestoreIgnoredPlatformCollisions();
        if (_fezPlatformLayerIgnored)
            SetFezPlatformLayerCollisionIgnored(false);
    }

    static float ApplyAxisDeadzone(float value, float deadzone)
    {
        var dz = Mathf.Clamp01(deadzone);
        if (Mathf.Abs(value) < dz)
            return 0f;
        return value;
    }

    private void SetFezPlatformLayerCollisionIgnored(bool ignore)
    {
        if (_fezPlatformLayer < 0 || _controller == null)
            return;
        if (_fezPlatformLayerIgnored == ignore)
            return;

        Physics.IgnoreLayerCollision(_controller.gameObject.layer, _fezPlatformLayer, ignore);
        _fezPlatformLayerIgnored = ignore;
    }

    private void RefreshDropThroughIgnores()
    {
        if (_controller == null)
            return;

        var b = _controller.bounds;
        var center = new Vector3(b.center.x, b.min.y + 0.05f, b.center.z);
        var radius = Mathf.Max(0.05f, b.extents.x * 0.9f);
        var distance = Mathf.Max(0.2f, b.extents.y + 1f);

        var hits = Physics.SphereCastAll(
            center,
            radius,
            Vector3.down,
            distance,
            ~0,
            QueryTriggerInteraction.Collide
        );

        for (var i = 0; i < hits.Length; i++)
        {
            var col = hits[i].collider;
            if (col == null || col == _controller)
                continue;

            if (!FezOneWayPlatform.TryResolveByCollider(col, out var platform))
                continue;

            var platformCols = platform.GetAllPlatformColliders();
            if (platformCols == null)
                continue;
            for (var j = 0; j < platformCols.Length; j++)
            {
                var pc = platformCols[j];
                if (pc == null || pc == _controller || _ignoredPlatformColliders.Contains(pc))
                    continue;

                Physics.IgnoreCollision(_controller, pc, true);
                _ignoredPlatformColliders.Add(pc);
            }
        }
    }

    private void RestoreIgnoredPlatformCollisions()
    {
        if (_controller == null)
            return;

        foreach (var col in _ignoredPlatformColliders)
        {
            if (col == null)
                continue;
            Physics.IgnoreCollision(_controller, col, false);
        }
        _ignoredPlatformColliders.Clear();
    }
    private bool IsDropThroughActive()
    {
        return Time.time <= _ignoreCollisionUntil;
    }
}
