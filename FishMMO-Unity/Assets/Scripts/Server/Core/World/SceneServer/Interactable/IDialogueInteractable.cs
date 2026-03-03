using System;
using FishMMO.Shared;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Server-side interface for dialogue interactables.
	/// Exposes the dialogue template needed by the interaction handler and dialogue session management.
	/// </summary>
	public interface IDialogueInteractable : IInteractable
	{
		public static event Action<ICharacter, DialogueTemplate> OnServerDialogueRequested;

		/// <summary>
		/// The dialogue template defining conversation nodes and choices.
		/// </summary>
		DialogueTemplate Template { get; }
	}
}