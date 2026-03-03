using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// ScriptableObject template defining configuration for container interactables (chests, wardrobes, etc.).
	/// </summary>
	[CreateAssetMenu(fileName = "New Container", menuName = "FishMMO/Character/Container/Container", order = 1)]
	public class ContainerTemplate : CachedScriptableObject<ContainerTemplate>, ICachedObject
	{
		public Sprite Icon;
		public string Description;

		/// <summary>
		/// Number of item slots this container provides.
		/// </summary>
		public int SlotCount = 10;

		/// <summary>
		/// When true, the container despawns after all items have been taken.
		/// </summary>
		public bool DespawnWhenEmpty;

		public string Name { get { return this.name; } }
	}
}