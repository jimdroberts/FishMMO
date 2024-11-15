using UnityEngine;
#if !UNITY_SERVER
using TMPro;
#endif

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

#if !UNITY_SERVER
		Transform MeshRoot { get;}
		TextMeshPro CharacterNameLabel { get; set; }
		TextMeshPro CharacterGuildLabel { get; set; }
#endif

		void RegisterCharacterBehaviour(ICharacterBehaviour characterBehaviour);
		void UnregisterCharacterBehaviour(ICharacterBehaviour characterBehaviour);
		bool TryGet<T>(out T control) where T : class, ICharacterBehaviour;
	}
}