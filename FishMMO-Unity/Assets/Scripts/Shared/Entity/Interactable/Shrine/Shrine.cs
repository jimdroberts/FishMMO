using FishMMO.Server.Core.World.SceneServer;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Shrine interactable that applies buffs or heals health, mana, or both when a player interacts with it.
	/// Configured via a <see cref="ShrineTemplate"/> ScriptableObject asset.
	/// </summary>
	[RequireComponent(typeof(SceneObjectNamer))]
	public class Shrine : Interactable, IShrine
	{
		/// <summary>
		/// Template defining the shrine's healing and buff effects.
		/// </summary>
		public ShrineTemplate Template;

		/// <summary>
		/// Achievement to increment when a player uses this shrine.
		/// </summary>
		public AchievementTemplate AchievementTemplate;

		/// <inheritdoc />
		ShrineTemplate IShrine.Template => Template;

		/// <inheritdoc />
		AchievementTemplate IShrine.AchievementTemplate => AchievementTemplate;

		private string title = "Shrine";

		/// <summary>
		/// Display title shown above the shrine.
		/// </summary>
		public override string Title { get { return title; } }

		/// <summary>
		/// Title color for the shrine UI label.
		/// </summary>
		public override Color TitleColor { get { return TinyColor.ToUnityColor(TinyColor.teal); } }

		public override void OnAwake()
		{
			base.OnAwake();

			if (Template != null)
			{
				title = Template.Name;
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