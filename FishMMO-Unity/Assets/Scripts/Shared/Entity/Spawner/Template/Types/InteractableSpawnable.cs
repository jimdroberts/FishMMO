using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Interactable Spawnable", menuName = "Spawnables/Interactable Spawnable", order = 0)]
	public class InteractableSpawnable : BaseSpawnable
	{
		public Interactable Interactable;
	}
}