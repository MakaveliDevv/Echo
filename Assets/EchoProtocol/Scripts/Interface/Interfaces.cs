namespace Assets.EchoProtocol.Scripts.Interface
{
    /// <summary>
    /// Any object that the player can use with the interaction key should implement this interface.
    /// PlayerInteraction does not need to know if the object is a scanner, cell, terminal, etc.;
    /// it only asks for a prompt text and calls Interact().
    /// </summary>
    public interface IInteractable
    {
        // Text shown by the UI when the player looks at this object.
        string InteractionPrompt { get; }

        // Called by PlayerInteraction when the player presses E while looking at the object.
        void Interact();
    }
}
