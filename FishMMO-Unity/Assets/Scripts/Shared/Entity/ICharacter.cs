using UnityEngine;

namespace FishMMO.Shared
{
	public interface ICharacter
	{
		long ID { get; set; }
		Transform Transform { get; }
		GameObject GameObject { get; }
		Collider Collider { get; set; }
		bool IsTeleporting { get; }
		bool IsSpawned { get; }

		void RegisterCharacterBehaviour(ICharacterBehaviour characterBehaviour);
		void UnregisterCharacterBehaviour(ICharacterBehaviour characterBehaviour);
		bool TryGet<T>(out T control) where T : class, ICharacterBehaviour;
	}
}