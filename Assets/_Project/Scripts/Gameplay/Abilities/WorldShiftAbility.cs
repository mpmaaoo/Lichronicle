using UnityEngine;

namespace Lichronicle.Gameplay.Abilities
{
    /// <summary>
    /// 技能C：世界切換（類 Titanfall 關卡）
    /// 簡化版本：
    /// - 場景中有兩個結構相似的世界 Root（例如 WorldA / WorldB）。
    /// - 技能啟用時在兩個世界之間切換 active 狀態。
    /// - 之後可以擴充為同時處理敵人/互動物件/特效等的同步。
    /// </summary>
    public sealed class WorldShiftAbility : MonoBehaviour, IAbilityHandler
    {
        [SerializeField] private GameObject worldA;
        [SerializeField] private GameObject worldB;
        [SerializeField] private bool startInWorldA = true;

        private bool _inWorldA;

        private void Awake()
        {
            _inWorldA = startInWorldA;
            ApplyWorldState();
        }

        public void OnAbilityTriggered(AbilityDefinition definition, bool active)
        {
            if (definition == null || definition.kind != AbilityKind.WorldShift)
                return;

            if (!active)
                return;

            _inWorldA = !_inWorldA;
            ApplyWorldState();
        }

        private void ApplyWorldState()
        {
            if (worldA != null)
                worldA.SetActive(_inWorldA);
            if (worldB != null)
                worldB.SetActive(!_inWorldA);
        }
    }
}

