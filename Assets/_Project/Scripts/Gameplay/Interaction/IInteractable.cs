namespace Lichronicle.Gameplay.Interaction
{
    public interface IInteractable
    {
        bool CanInteract(Interactor interactor);
        void Interact(Interactor interactor);
        string GetPrompt(Interactor interactor);
    }
}

