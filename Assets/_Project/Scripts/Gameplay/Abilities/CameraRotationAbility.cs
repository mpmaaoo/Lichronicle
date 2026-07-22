using UnityEngine;
using Cinemachine;

namespace Lichronicle.Gameplay.Abilities
{
    /// <summary>
    /// 技能A：視角切換（類 FEZ）
    /// 目前做法：
    /// - 以玩家為中心，Y 軸每次旋轉 90 度。
    /// - 可以接一個 CinemachineVirtualCamera，讓鏡頭跟著世界方向更新。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class CameraRotationAbility : MonoBehaviour, IAbilityHandler
    {
        [SerializeField] private Transform targetPivot; // 通常是玩家或玩家的空物件
        [SerializeField] private float stepDegrees = 90f;
        [SerializeField] private float rotateDuration = 0.35f;
        [SerializeField] private CinemachineVirtualCamera virtualCamera;

        private bool _isRotating;
        private float _elapsed;
        private Quaternion _fromRot;
        private Quaternion _toRot;

        private void Update()
        {
            if (!_isRotating || targetPivot == null)
                return;

            _elapsed += Time.deltaTime;
            var t = Mathf.Clamp01(_elapsed / rotateDuration);
            targetPivot.rotation = Quaternion.Slerp(_fromRot, _toRot, t);

        if (t >= 1f)
        {
            _isRotating = false;
            NotifyWorldViewState();
            NotifyOcclusionGate();
            NotifyOcclusionGateRotationState(false);
            if (!IsPlayerDepthMovementBlocked())
                FezBackgroundWallMoveTriggerFollower.ForceSyncAllPhases(0f);
        }
        }

        public void OnAbilityTriggered(AbilityDefinition definition, bool active)
        {
            if (definition == null || definition.kind != AbilityKind.CameraRotate)
                return;

            if (!active || targetPivot == null)
                return;

            if (_isRotating)
                return;

            var dir = Mathf.Sign(Mathf.Approximately(definition.floatParam, 0f) ? 1f : definition.floatParam);

            _isRotating = true;
            NotifyOcclusionGateRotationState(true);
            _elapsed = 0f;
            _fromRot = targetPivot.rotation;
            var targetY = _fromRot.eulerAngles.y + stepDegrees * dir;
            targetY = Mathf.Round(targetY / 90f) * 90f;
            _toRot = Quaternion.Euler(0f, targetY, 0f);
        }

        private void NotifyWorldViewState()
        {
            if (targetPivot == null)
                return;
            var state = targetPivot.GetComponent<FezWorldViewState>();
            if (state == null)
                state = targetPivot.GetComponentInParent<FezWorldViewState>();
            if (state == null)
                state = targetPivot.GetComponentInChildren<FezWorldViewState>();
            state?.RefreshFromPivot();
        }

        private void NotifyOcclusionGate()
        {
            if (targetPivot == null)
                return;

            var gate = FindOcclusionGate();
            gate?.OnViewRotationCompleted();
        }

        private bool IsPlayerDepthMovementBlocked()
        {
            var gate = FindOcclusionGate();
            return gate != null && gate.IsPlayerDepthMovementBlocked;
        }

        private void NotifyOcclusionGateRotationState(bool rotating)
        {
            var gate = FindOcclusionGate();
            gate?.SetRotationInProgress(rotating);
        }

        private FezRotationOcclusionGate FindOcclusionGate()
        {
            if (targetPivot == null)
                return null;

            var gate = targetPivot.GetComponent<FezRotationOcclusionGate>();
            if (gate == null)
                gate = targetPivot.GetComponentInParent<FezRotationOcclusionGate>();
            if (gate == null)
                gate = targetPivot.GetComponentInChildren<FezRotationOcclusionGate>();
            return gate;
        }
    }
}

