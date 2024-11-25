using System;
using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Faction", menuName = "Character/Faction/Faction", order = 1)]
	public class FactionTemplate : CachedScriptableObject<FactionTemplate>, ICachedObject
	{
		[Serializable]
		public class FactionHashSet : SerializableHashSet<FactionTemplate> { }

		public Sprite Icon;
		
		public const int Minimum = -10000;
		public const int Maximum = 10000;
		
		public string Description;

		public FactionHashSet DefaultAllied;
		public FactionHashSet DefaultNeutral;
		public FactionHashSet DefaultHostile;

		public string Name { get { return this.name; } }
	}
}