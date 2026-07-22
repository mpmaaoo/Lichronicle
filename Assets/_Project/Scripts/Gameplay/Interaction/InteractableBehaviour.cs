using UnityEngine;

namespace Lichronicle.Gameplay.Interaction
{
    /// <summary>
    /// 讓設計可以用 Inspector 先快速接線的互動物件基底：
    /// - Prompt：顯示給玩家的文字
    /// - OnInteract：互動時要做的事（可先用 UnityEvent / 之後替換成資料驅動）
    /// </summary>
    public sealed class InteractableBehaviour : MonoBehaviour, IInteractable
    {
        [SerializeField] private string prompt = "互動";
        [SerializeField] private bool oneShot = false;
        [SerializeField] private UnityEngine.Events.UnityEvent onInteract;

        private bool _used;

        public bool CanInteract(Interactor interactor) => !_used;

        public void Interact(Interactor interactor)
        {
            if (_used)
                return;

            onInteract?.Invoke();

            if (oneShot)
                _used = true;
        }

        public string GetPrompt(Interactor interactor) => prompt;
    }
}

