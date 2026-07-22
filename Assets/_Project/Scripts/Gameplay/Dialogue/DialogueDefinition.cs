using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lichronicle.Gameplay.Dialogue
{
    [CreateAssetMenu(menuName = "Lichronicle/Dialogue/Dialogue Definition")]
    public sealed class DialogueDefinition : ScriptableObject
    {
        public List<Node> nodes = new();
        public string startNodeId;

        [Serializable]
        public sealed class Node
        {
            public string id;
            [TextArea(2, 6)] public string text;
            public List<Choice> choices = new();
        }

        [Serializable]
        public sealed class Choice
        {
            public string label;
            public string nextNodeId;
        }
    }
}

