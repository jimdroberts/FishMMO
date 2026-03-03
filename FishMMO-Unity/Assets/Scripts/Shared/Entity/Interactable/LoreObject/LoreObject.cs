using FishMMO.Server.Core.World.SceneServer;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Lore object interactable that displays a UILore window on interaction.
	/// Optionally provides immediate unlocks of known base abilities, ability events, and/or items.
	/// Configured via a <see cref="LoreObjectTemplate"/> ScriptableObject asset.
	/// </summary>
	[RequireComponent(typeof(SceneObjectNamer))]
	public class LoreObject : Interactable, ILoreObject
	{
		/// <summary>
		/// Template defining the lore text and optional ability/item grants.
		/// </summary>
		public LoreObjectTemplate Template;

		/// <summary>
		/// Achievement to increment when a player discovers this lore object.
		/// </summary>
		public AchievementTemplate AchievementTemplate;

		/// <inheritdoc />
		LoreObjectTemplate ILoreObject.Template => Template;

		/// <inheritdoc />
		AchievementTemplate ILoreObject.AchievementTemplate => AchievementTemplate;

		private string title = "Lore";

		/// <summary>
		/// Display title shown above the lore object.
		/// </summary>
		public override string Title { get { return title; } }

		/// <summary>
		/// Title color for the lore object UI label.
		/// </summary>
		public override Color TitleColor { get { return TinyColor.ToUnityColor(TinyColor.plum); } }

		public override void OnAwake()
		{
			base.OnAwake();

			if (Template != null &&
				!string.IsNullOrWhiteSpace(Template.Title))
			{
				title = Template.Title;
			}
		}

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