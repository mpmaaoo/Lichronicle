using System;
using System.Collections.Generic;

namespace Lichronicle.Gameplay.Quest
{
    [Serializable]
    public sealed class QuestState
    {
        public string questId;
        public HashSet<string> completedObjectives = new();
        public bool completed;
    }
}

