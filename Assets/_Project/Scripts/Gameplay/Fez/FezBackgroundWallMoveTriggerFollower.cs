using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 沿深度軸跟隨玩家；優先移動 <see cref="FezBackgroundWall.DepthReference"/>（WallCenter），
/// Trigger 淺側邊界不得比牆本體淺面更淺。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(FezBackgroundWallMoveTrigger))]
public sealed class FezBackgroundWallMoveTriggerFollower : MonoBehaviour
{
    private static readonly List<FezBackgroundWallMoveTriggerFollower> Registry =
        new List<FezBackgroundWallMoveTriggerFollower>(32);

    private static int _lastBeforeMoveFrame = -1;
    private static int _lastAfterRouterFrame = -1;

    [SerializeField] private Transform playerRoot;
    [SerializeField] private FezDepthAxisSource depthAxisSource = FezDepthAxisSource.WorldViewState;
    [SerializeField] private Transform explicitAxisTransform;
    [SerializeField] private Camera explicitCamera;
    [SerializeField] private float followSpeed = 0f;

    private Transform _wallRoot;
    private Transform _depthReference;
    private Vector3 _depthRefBaseLocalOnWall;
    private FezBackgroundWall _ownerWall;
    private CharacterController _playerCc;
    private Collider _triggerCollider;
    private FezRotationOcclusionGate _playerOcclusionGate;

    public static void SyncAllBeforeCharacterControllerMove(float deltaTime) =>
        SyncPhase(deltaTime, ref _lastBeforeMoveFrame);

    public static void SyncAllAfterBackgroundWallRouter(float deltaTime) =>
        SyncPhase(deltaTime, ref _lastAfterRouterFrame);

    public static void ForceSyncAllPhases(float deltaTime)
    {
        _lastBeforeMoveFrame = -1;
        _lastAfterRouterFrame = -1;
        SyncAllBeforeCharacterControllerMove(deltaTime);
        SyncAllAfterBackgroundWallRouter(deltaTime);
    }

    public void SetPlayerRoot(Transform root)
    {
        playerRoot = root;
        _playerCc = root != null ? root.GetComponent<CharacterController>() : null;
        _playerOcclusionGate = root != null ? root.GetComponent<FezRotationOcclusionGate>() : null;
    }

    private static void SyncPhase(float deltaTime, ref int lastFrameGate)
    {
        var f = Time.frameCount;
        if (f == lastFrameGate)
            return;
        lastFrameGate = f;

        for (var i = 0; i < Registry.Count; i++)
        {
            var c = Registry[i];
            if (c != null && c.isActiveAndEnabled)
                c.SyncToPlayerDepth(deltaTime);
        }
    }

    private void Awake()
    {
        var trigger = GetComponent<FezBackgroundWallMoveTrigger>();
        _ownerWall = trigger != null ? trigger.OwnerWall : null;
        _wallRoot = _ownerWall != null ? _ownerWall.transform : transform.parent;
        _triggerCollider = GetComponent<Collider>();
        CacheDepthReferenceBasePose();
        if (playerRoot != null)
        {
            _playerCc = playerRoot.GetComponent<CharacterController>();
            _playerOcclusionGate = playerRoot.GetComponent<FezRotationOcclusionGate>();
        }
    }

    private void CacheDepthReferenceBasePose()
    {
        _depthReference = _ownerWall != null ? _ownerWall.DepthReference : null;
        if (_depthReference != null && _wallRoot != null)
        {
            _depthRefBaseLocalOnWall = _wallRoot.InverseTransformPoint(_depthReference.position);
            return;
        }

        _depthReference = transform;
        _depthRefBaseLocalOnWall = _wallRoot != null
            ? _wallRoot.InverseTransformPoint(transform.position)
            : transform.localPosition;
    }

    private void OnEnable()
    {
        if (!Registry.Contains(this))
            Registry.Add(this);
    }

    private void OnDisable()
    {
        Registry.Remove(this);
    }

    private void SyncToPlayerDepth(float dt)
    {
        if (playerRoot == null || _ownerWall == null || _wallRoot == null || _depthReference == null)
            return;
        if (IsPlayerDepthMovementBlocked())
            return;

        var viewForward = FezDepthAxis.GetViewForward(depthAxisSource, explicitAxisTransform, explicitCamera);
        if (viewForward.sqrMagnitude < 0.0001f)
            return;
        viewForward.Normalize();

        var playerDepth = _playerCc != null
            ? FezDepthAxis.DepthOf(_playerCc.bounds.center, viewForward)
            : FezDepthAxis.DepthOf(playerRoot.position, viewForward);

        var bodyShallow = _ownerWall.GetBodyShallowDepth(viewForward);
        var shallowLead = GetTriggerShallowLeadAtBasePose(viewForward);
        var minPivotDepth = bodyShallow + shallowLead;
        var followDepth = Mathf.Max(playerDepth, minPivotDepth);

        var delta = ComputeDepthDeltaFromBasePose(followDepth, viewForward);
        if (!float.IsFinite(delta) || Mathf.Abs(delta) < 0.0001f)
            return;

        var localDepthDir = _wallRoot.InverseTransformDirection(viewForward);
        localDepthDir.y = 0f;
        if (localDepthDir.sqrMagnitude < 0.0001f)
            return;
        localDepthDir.Normalize();

        ApplyDepthReferenceLocalPosition(_depthRefBaseLocalOnWall + localDepthDir * delta, dt);

        if (_depthReference != transform)
            transform.localPosition = Vector3.zero;
    }

    private float GetTriggerShallowLeadAtBasePose(Vector3 viewForward)
    {
        var pivotWorld = GetDepthReferenceBaseWorldPosition();
        var pivotDepth = FezDepthAxis.DepthOf(pivotWorld, viewForward);
        var triggerBounds = GetTriggerBoundsAtBasePose();
        var triggerShallow = FezBackgroundWall.ProjectBoundsShallowDepth(triggerBounds, viewForward);
        return Mathf.Max(0f, pivotDepth - triggerShallow);
    }

    private Vector3 GetDepthReferenceBaseWorldPosition()
    {
        return _wallRoot.TransformPoint(_depthRefBaseLocalOnWall);
    }

    private Bounds GetTriggerBoundsAtBasePose()
    {
        if (_triggerCollider == null)
            return new Bounds(GetDepthReferenceBaseWorldPosition(), Vector3.zero);

        if (_triggerCollider is BoxCollider box)
            return GetBoxWorldBoundsAtBasePose(box);

        var prevRefLocal = _depthReference.localPosition;
        var prevSelfLocal = transform.localPosition;
        _depthReference.localPosition = _depthRefBaseLocalOnWall;
        transform.localPosition = Vector3.zero;
        Physics.SyncTransforms();
        var b = _triggerCollider.bounds;
        _depthReference.localPosition = prevRefLocal;
        transform.localPosition = prevSelfLocal;
        Physics.SyncTransforms();
        return b;
    }

    private Bounds GetBoxWorldBoundsAtBasePose(BoxCollider box)
    {
        var localToWorld = BuildTriggerBaseLocalToWorldMatrix();
        var center = box.center;
        var ext = box.size * 0.5f;
        var first = localToWorld.MultiplyPoint3x4(center + Vector3.Scale(new Vector3(-1f, -1f, -1f), ext));
        var bounds = new Bounds(first, Vector3.zero);
        for (var xi = -1; xi <= 1; xi += 2)
        {
            for (var yi = -1; yi <= 1; yi += 2)
            {
                for (var zi = -1; zi <= 1; zi += 2)
                {
                    var corner = center + Vector3.Scale(new Vector3(xi, yi, zi), ext);
                    bounds.Encapsulate(localToWorld.MultiplyPoint3x4(corner));
                }
            }
        }

        return bounds;
    }

    private Matrix4x4 BuildTriggerBaseLocalToWorldMatrix()
    {
        if (_depthReference == transform)
        {
            var child = Matrix4x4.TRS(_depthRefBaseLocalOnWall, transform.localRotation, transform.localScale);
            return _wallRoot.localToWorldMatrix * child;
        }

        var refMatrix = Matrix4x4.TRS(_depthRefBaseLocalOnWall, _depthReference.localRotation, _depthReference.localScale);
        var triggerMatrix = Matrix4x4.TRS(transform.localPosition, transform.localRotation, transform.localScale);
        return _wallRoot.localToWorldMatrix * refMatrix * triggerMatrix;
    }

    private float ComputeDepthDeltaFromBasePose(float targetDepth, Vector3 viewForward)
    {
        var baseDepth = FezDepthAxis.DepthOf(GetDepthReferenceBaseWorldPosition(), viewForward);
        return targetDepth - baseDepth;
    }

    private void ApplyDepthReferenceLocalPosition(Vector3 desiredLocalOnWall, float dt)
    {
        if (followSpeed <= 0f)
            _depthReference.localPosition = desiredLocalOnWall;
        else
        {
            var t = 1f - Mathf.Exp(-followSpeed * dt);
            _depthReference.localPosition = Vector3.Lerp(_depthReference.localPosition, desiredLocalOnWall, t);
        }
    }

    private bool IsPlayerDepthMovementBlocked()
    {
        return _playerOcclusionGate != null && _playerOcclusionGate.IsPlayerDepthMovementBlocked;
    }
}
