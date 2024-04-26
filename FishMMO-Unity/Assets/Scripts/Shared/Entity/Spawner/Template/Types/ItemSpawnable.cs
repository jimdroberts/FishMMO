using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Item Spawnable", menuName = "Spawnables/Item Spawnable", order = 0)]
	public class ItemSpawnable : BaseSpawnable
	{
		public BaseItemTemplate Item;
	}
}