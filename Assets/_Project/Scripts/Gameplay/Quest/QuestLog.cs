using System.Collections.Generic;
using Lichronicle.Core;
using UnityEngine;

namespace Lichronicle.Gameplay.Quest
{
    public sealed class QuestLog : MonoBehaviour
    {
        [SerializeField] private List<QuestDefinition> knownQuests = new();

        private readonly Dictionary<string, QuestState> _stateByQuest = new();

        public void StartQuest(string questId)
        {
            if (string.IsNullOrWhiteSpace(questId))
                return;

            if (_stateByQuest.ContainsKey(questId))
                return;

            _stateByQuest[questId] = new QuestState { questId = questId };
            GameServices.Events.Publish(new QuestStarted(questId));
        }

        public void CompleteObjective(string questId, string objectiveId)
        {
            if (string.IsNullOrWhiteSpace(questId) || string.IsNullOrWhiteSpace(objectiveId))
                return;

            if (!_stateByQuest.TryGetValue(questId, out var state))
                return;

            if (state.completedObjectives.Add(objectiveId))
            {
                GameServices.Events.Publish(new ObjectiveCompleted(questId, objectiveId));
            }
        }

        public QuestState GetState(string questId)
        {
            _stateByQuest.TryGetValue(questId, out var state);
            return state;
        }
    }
}

