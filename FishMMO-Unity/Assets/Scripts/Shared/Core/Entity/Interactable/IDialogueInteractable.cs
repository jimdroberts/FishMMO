using System;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Server-side interface for dialogue interactables.
	/// Exposes the dialogue template needed by the interaction handler and dialogue session management.
	/// </summary>
	public interface IDialogueInteractable : IInteractable
	{
		/// <summary>
		/// Raised on the server when a dialogue session is requested via an ECA action.
		/// The InteractableSystem subscribes to this delegate to start dialogue sessions.
		/// </summary>
		static Action<ICharacter, DialogueTemplate> OnServerDialogueRequested;

		/// <summary>
		/// The dialogue template defining conversation nodes and choices.
		/// </summary>
		DialogueTemplate Template { get; }
	}
}