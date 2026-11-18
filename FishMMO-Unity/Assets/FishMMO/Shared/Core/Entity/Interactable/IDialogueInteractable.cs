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
		/// <remarks>
		/// The third argument is the interactable the conversation is anchored to, or null when the
		/// dialogue was raised by something with no world object behind it. It is what gives the
		/// session a scene object ID, and the session's ID is the only thing the choice handler can
		/// range-check against — with none, a player could open a conversation and then take the
		/// rest of it, rewards included, from anywhere in the zone.
		/// </remarks>
		static Action<ICharacter, DialogueTemplate, IInteractable> OnServerDialogueRequested;

		/// <summary>
		/// The dialogue template defining conversation nodes and choices.
		/// </summary>
		DialogueTemplate Template { get; }
	}
}