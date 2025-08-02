using System;
using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Faction", menuName = "FishMMO/Character/Faction/Faction", order = 1)]
	public class FactionTemplate : CachedScriptableObject<FactionTemplate>, ICachedObject
	{
		/// <summary>
		/// Serializable hash set of faction templates. Used for storing allied, neutral, and hostile relationships.
		/// </summary>
		[Serializable]
		public class FactionHashSet : SerializableHashSet<FactionTemplate> { }

		/// <summary>
		/// The icon representing this faction in the UI.
		/// </summary>
		public Sprite Icon;

		/// <summary>
		/// The minimum possible value for faction reputation or standing.
		/// </summary>
		public const int Minimum = -10000;

		/// <summary>
		/// The maximum possible value for faction reputation or standing.
		/// </summary>
		public const int Maximum = 10000;

		/// <summary>
		/// Description of the faction, used for tooltips and UI.
		/// </summary>
		public string Description;

		/// <summary>
		/// Set of factions that are considered allied by default.
		/// </summary>
		public FactionHashSet DefaultAllied;

		/// <summary>
		/// Set of factions that are considered neutral by default.
		/// </summary>
		public FactionHashSet DefaultNeutral;

		/// <summary>
		/// Set of factions that are considered hostile by default.
		/// </summary>
		public FactionHashSet DefaultHostile;

		/// <summary>
		/// The display name of the faction (from the ScriptableObject's name).
		/// </summary>
		public string Name { get { return this.name; } }
	}
}