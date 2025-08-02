using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents a merchant NPC that players can interact with to buy or sell items.
	/// Inherits from Interactable and uses a MerchantTemplate for configuration.
	/// </summary>
	[RequireComponent(typeof(SceneObjectNamer))]
	public class Merchant : Interactable
	{
		/// <summary>
		/// The template containing merchant configuration, such as inventory and description.
		/// </summary>
		public MerchantTemplate Template;

		/// <summary>
		/// The display title for this merchant, shown in the UI. Defaults to "Merchant" but can be set from the template.
		/// </summary>
		private string title = "Merchant";

		/// <summary>
		/// Gets the display title for this merchant, used in UI elements.
		/// </summary>
		public override string Title { get { return title; } }

		/// <summary>
		/// Called when the merchant is awakened in the scene. Sets the title from the template description if available.
		/// </summary>
		public override void OnAwake()
		{
			base.OnAwake();

			// If a template is assigned, use its description as the merchant's title.
			if (Template != null)
			{
				title = Template.Description;
			}
		}

		/// <summary>
		/// Determines if the specified player character can interact with this merchant.
		/// Requires a valid template and that the base interaction checks pass.
		/// </summary>
		/// <param name="character">The player character attempting to interact.</param>
		/// <returns>True if interaction is allowed, false otherwise.</returns>
		public override bool CanInteract(IPlayerCharacter character)
		{
			// Merchant must have a template and pass base interaction checks.
			if (Template == null ||
				!base.CanInteract(character))
			{
				return false;
			}
			return true;
		}
	}
}