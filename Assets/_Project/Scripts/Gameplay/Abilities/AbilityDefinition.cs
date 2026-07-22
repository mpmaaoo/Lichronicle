using UnityEngine;

namespace Lichronicle.Gameplay.Abilities
{
    [CreateAssetMenu(menuName = "Lichronicle/Ability/Ability Definition")]
    public sealed class AbilityDefinition : ScriptableObject
    {
        [Header("基本資料")]
        public string abilityId;
        public string displayName;
        [TextArea(2, 4)] public string description;
        public AbilityKind kind;

        [Header("操作設定")]
        public KeyCode key = KeyCode.None;
        [Tooltip("手把等替代鍵（例如 L1/R1）。留 None 則只用主鍵。")]
        public KeyCode alternateKey = KeyCode.None;
        public bool toggle; // 按一次開啟、再按一次關閉（例如幻影紀錄）

        [Header("額外參數")]
        [Tooltip("例如：視角切換可用 +1 / -1 代表順時針或逆時針。")]
        public float floatParam;

        [Header("冷卻與限制")]
        public float cooldownSeconds;
        public bool canUseWhileMoving = true;
    }
}

