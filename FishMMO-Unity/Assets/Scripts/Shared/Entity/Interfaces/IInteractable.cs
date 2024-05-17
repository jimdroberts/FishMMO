using UnityEngine;

namespace FishMMO.Shared
{
	public interface IInteractable : ISceneObject
	{
		Transform Transform { get; }
		string Title { get; }
		bool InRange(Transform transform);
		bool CanInteract(IPlayerCharacter character);
	}
}