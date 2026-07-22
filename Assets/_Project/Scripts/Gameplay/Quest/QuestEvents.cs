namespace Lichronicle.Gameplay.Quest
{
    public readonly struct QuestStarted
    {
        public readonly string QuestId;
        public QuestStarted(string questId) => QuestId = questId;
    }

    public readonly struct ObjectiveCompleted
    {
        public readonly string QuestId;
        public readonly string ObjectiveId;
        public ObjectiveCompleted(string questId, string objectiveId)
        {
            QuestId = questId;
            ObjectiveId = objectiveId;
        }
    }
}

