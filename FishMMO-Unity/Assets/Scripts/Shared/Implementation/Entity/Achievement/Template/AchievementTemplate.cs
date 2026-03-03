using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// ScriptableObject template representing an achievement, including icon, category, description, and tiers.
	/// </summary>
	[CreateAssetMenu(fileName = "New Achievement", menuName = "FishMMO/Character/Achievement/Achievement", order = 1)]
	public class AchievementTemplate : CachedScriptableObject<AchievementTemplate>, ICachedObject
	{
		/// <summary>
		/// The icon representing this achievement in the UI.
		/// </summary>
		public Sprite Icon;

		/// <summary>
		/// The category this achievement belongs to (e.g., Combat, Exploration).
		/// </summary>
		public AchievementCategory Category;

		/// <summary>
		/// The description of the achievement, shown to the player.
		/// </summary>
		public string Description;

		/// <summary>
		/// The list of tiers for this achievement, each representing a milestone or level.
		/// </summary>
		public List<AchievementTier> Tiers;

		/// <summary>
		/// The name of this achievement template (from the ScriptableObject's name).
		/// </summary>
		public string Name { get { return this.name; } }
	}
}