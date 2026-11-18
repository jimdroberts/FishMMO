using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// ScriptableObject database for storing and retrieving achievement templates by name.
	/// </summary>
	[CreateAssetMenu(fileName = "New Achievement Database", menuName = "FishMMO/Character/Achievement/Database", order = 0)]
	public class AchievementTemplateDatabase : ScriptableObject
	{
		/// <summary>
		/// Serializable dictionary mapping achievement names to their templates.
		/// </summary>
		[Serializable]
		public class AchievementDictionary : SerializableDictionary<string, AchievementTemplate> { }

		/// <summary>
		/// The backing field for the achievements dictionary.
		/// </summary>
		[SerializeField]
		private AchievementDictionary achievements = new AchievementDictionary();

		/// <summary>
		/// Public accessor for the achievements dictionary.
		/// </summary>
		public AchievementDictionary Achievements { get { return this.achievements; } }

		/// <summary>
		/// Retrieves an achievement template by name, or null if not found.
		/// </summary>
		/// <param name="name">The name of the achievement to retrieve.</param>
		/// <returns>The <see cref="AchievementTemplate"/> if found, otherwise null.</returns>
		public AchievementTemplate GetAchievement(string name)
		{
			this.achievements.TryGetValue(name, out AchievementTemplate achievement);
			return achievement;
		}
	}
}