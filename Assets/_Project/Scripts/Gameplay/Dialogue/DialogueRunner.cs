using System.Collections.Generic;
using UnityEngine;

namespace Lichronicle.Gameplay.Dialogue
{
    /// <summary>
    /// 最小可用對話 Runner：先只處理「顯示文字 + 選項跳轉」。
    /// UI 先用 Debug.Log 讓系統跑起來，後續再接正式 UI。
    /// </summary>
    public sealed class DialogueRunner : MonoBehaviour
    {
        [SerializeField] private DialogueDefinition dialogue;

        private readonly Dictionary<string, DialogueDefinition.Node> _index = new();
        private DialogueDefinition.Node _current;

        private void Awake()
        {
            RebuildIndex();
        }

        public void StartDialogue(DialogueDefinition def)
        {
            if (def == null)
            {
                Debug.LogWarning("DialogueRunner: StartDialogue called with null.");
                return;
            }

            dialogue = def;
            RebuildIndex();
            GoTo(def.startNodeId);
        }

        public void Choose(int choiceIndex)
        {
            if (_current == null)
                return;
            if (choiceIndex < 0 || choiceIndex >= _current.choices.Count)
                return;

            var next = _current.choices[choiceIndex].nextNodeId;
            GoTo(next);
        }

        private void RebuildIndex()
        {
            _index.Clear();
            if (dialogue == null)
                return;

            for (var i = 0; i < dialogue.nodes.Count; i++)
            {
                var n = dialogue.nodes[i];
                if (n == null || string.IsNullOrWhiteSpace(n.id))
                    continue;
                _index[n.id] = n;
            }
        }

        private void GoTo(string nodeId)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                Debug.Log("DialogueRunner: End (empty nodeId).");
                _current = null;
                return;
            }

            if (!_index.TryGetValue(nodeId, out var node) || node == null)
            {
                Debug.LogWarning($"DialogueRunner: Node not found: {nodeId}");
                _current = null;
                return;
            }

            _current = node;
            RenderCurrentToConsole();
        }

        private void RenderCurrentToConsole()
        {
            if (_current == null)
                return;

            Debug.Log($"[Dialogue] {_current.text}");
            for (var i = 0; i < _current.choices.Count; i++)
            {
                Debug.Log($"  ({i}) {_current.choices[i].label} -> {_current.choices[i].nextNodeId}");
            }
        }
    }
}

