using FishMMO.Server.Core.World.SceneServer;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents an NPC that players can interact with to start a server-authoritative dialogue.
	/// Uses a <see cref="DialogueTemplate"/> to define the conversation tree.
	/// </summary>
	[RequireComponent(typeof(SceneObjectNamer))]
	public class DialogueInteractable : Interactable, IDialogueInteractable
	{
		/// <summary>
		/// The template defining the conversation tree for this dialogue interactable.
		/// </summary>
		public DialogueTemplate Template;

		/// <inheritdoc />
		DialogueTemplate IDialogueInteractable.Template => Template;

		/// <summary>
		/// The display title for this interactable, shown in the UI.
		/// Defaults to "Dialogue" but is set from the template name on awake.
		/// </summary>
		private string title = "Dialogue";

		/// <summary>
		/// Gets the display title for this dialogue interactable.
		/// </summary>
		public override string Title { get { return title; } }

		/// <summary>
		/// Called when the interactable awakes. Sets the title from the template name if available.
		/// </summary>
		public override void OnAwake()
		{
			base.OnAwake();

			if (Template != null)
			{
				title = Template.Name;
			}
		}

		/// <summary>
		/// Determines if the specified player character can interact with this dialogue interactable.
		/// Requires a valid template and that the base interaction checks pass.
		/// </summary>
		/// <param name="character">The player character attempting to interact.</param>
		/// <returns>True if interaction is allowed, false otherwise.</returns>
		public override bool CanInteract(IPlayerCharacter character)
		{
			if (Template == null ||
				!base.CanInteract(character))
			{
				return false;
			}
			return true;
		}
	}
}