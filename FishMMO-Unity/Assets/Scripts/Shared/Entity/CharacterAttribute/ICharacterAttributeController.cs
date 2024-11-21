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
		bool TryGetHealthAttribute(out CharacterResourceAttribute health);
		bool TryGetManaAttribute(out CharacterResourceAttribute mana);
		bool TryGetStaminaAttribute(out CharacterResourceAttribute stamina);
		float GetHealthResourceAttributeCurrentPercentage();
		float GetManaResourceAttributeCurrentPercentage();
		float GetStaminaResourceAttributeCurrentPercentage();
		bool TryGetResourceAttribute(CharacterAttributeTemplate template, out CharacterResourceAttribute attribute);
		bool TryGetResourceAttribute(int id, out CharacterResourceAttribute attribute);
		void AddAttribute(CharacterAttribute instance);
		void Regenerate(float deltaTime);
		void ApplyResourceState(CharacterAttributeResourceState resourceState);
		CharacterAttributeResourceState GetResourceState();
	}
}