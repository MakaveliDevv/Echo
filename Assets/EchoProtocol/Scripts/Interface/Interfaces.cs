namespace Assets.EchoProtocol.Scripts.Interface
{

    public interface IInteractable
    {
        string InteractionPrompt { get; }

        void Interact();
    }
}
