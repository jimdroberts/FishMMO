using System.Collections.Generic;

namespace FishMMO.Shared
{
	public interface ICharacterAttributeController : ICharacterBehaviour
	{
		Dictionary<int, CharacterAttribute> Attributes { get; }
		Dictionary<int, CharacterResourceAttribute> ResourceAttributes { get; }

		void SetAttribute(int id, int value);
		void SetResourceAttribute(int id, int value, float currentValue);
		bool TryGetAttribute(CharacterAttributeTemplate template, out CharacterAttribute attribute);
		bool TryGetAttribute(int id, out CharacterAttribute attribute);
		bool TryGetResourceAttribute(CharacterAttributeTemplate template, out CharacterResourceAttribute attribute);
		float GetResourceAttributeCurrentPercentage(CharacterAttributeTemplate template);
		bool TryGetResourceAttribute(int id, out CharacterResourceAttribute attribute);
		void AddAttribute(CharacterAttribute instance);
		void Regenerate(float deltaTime);
		void ApplyResourceState(CharacterAttributeResourceState resourceState);
		CharacterAttributeResourceState GetResourceState();
	}
}