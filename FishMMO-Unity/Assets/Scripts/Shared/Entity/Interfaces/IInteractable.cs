using UnityEngine;

namespace FishMMO.Shared
{
	public interface IInteractable : ISceneObject
	{
		Transform Transform { get; }
		string Title { get; }
		Color TitleColor { get; }
		bool InRange(Transform transform);
		bool CanInteract(IPlayerCharacter character);
	}
}