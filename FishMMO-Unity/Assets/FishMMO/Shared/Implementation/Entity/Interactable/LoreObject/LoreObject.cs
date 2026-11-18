using System.Collections.Generic;
using FishMMO.Shared.Core;
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

		/// <summary>
		/// Characters that have already taken this lore object's item grant. Server-side.
		/// </summary>
		/// <remarks>
		/// Deliberately NOT a gate on interacting. Re-reading lore is free and should stay that
		/// way — the window reopens, the achievement is idempotent, and the ability grants skip
		/// what the character already knows. Only the items are one-time.
		/// </remarks>
		private HashSet<long> claimedItemGrants;

		/// <inheritdoc />
		public bool TryConsumeItemGrant(long characterID)
		{
			if (characterID == 0)
			{
				return false;
			}

			claimedItemGrants ??= new HashSet<long>();

			// Add returns false when the character is already present, which is exactly the
			// "already claimed" answer — so the test and the record are one operation and two
			// requests in the same frame cannot both pass.
			return claimedItemGrants.Add(characterID);
		}

		/// <summary>
		/// Drops the claim record when this instance returns to the pool.
		/// </summary>
		/// <param name="asServer">True when the reset is for the server instance.</param>
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);

			claimedItemGrants?.Clear();
		}
	}
}