using UnityEngine;

namespace FishMMO.Shared
{
	public interface IInteractable
	{
		Transform Transform { get; }
		bool InRange(Transform transform);
		bool OnInteract(Character character);
	}
}