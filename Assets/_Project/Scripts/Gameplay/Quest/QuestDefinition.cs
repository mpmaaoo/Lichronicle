using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lichronicle.Gameplay.Quest
{
    [CreateAssetMenu(menuName = "Lichronicle/Quest/Quest Definition")]
    public sealed class QuestDefinition : ScriptableObject
    {
        public string questId;
        public string title;
        [TextArea(2, 6)] public string description;
        public List<Objective> objectives = new();

        [Serializable]
        public sealed class Objective
        {
            public string objectiveId;
            public string label;
        }
    }
}

