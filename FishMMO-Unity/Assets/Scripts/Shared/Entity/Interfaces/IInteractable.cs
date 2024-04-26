using UnityEngine;

namespace FishMMO.Shared
{
	public interface IInteractable
	{
		Transform Transform { get; }
		int ID { get; }
		string Title { get; }
		bool InRange(Transform transform);
		bool CanInteract(IPlayerCharacter character);
	}
}