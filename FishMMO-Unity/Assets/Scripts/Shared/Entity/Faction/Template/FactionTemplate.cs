using System;
using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Faction", menuName = "Character/Faction/Faction", order = 1)]
	public class FactionTemplate : CachedScriptableObject<FactionTemplate>, ICachedObject
	{
		[Serializable]
		public class FactionHashSet : SerializableHashSet<FactionTemplate> { }

		public int AlliedLevel = 1000;
		public int EnemyLevel = -1000;
		public string Description;

		public FactionHashSet Allied;
		public FactionHashSet Neutral;
		public FactionHashSet Enemies;

		public string Name { get { return this.name; } }
	}
}