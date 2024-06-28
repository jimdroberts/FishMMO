using System;

namespace FishMMO.Shared
{
	public interface ICooldownController : ICharacterBehaviour
	{
		static Action<int, CooldownInstance> OnAddCooldown;
		static Action<int> OnRemoveCooldown;

		void OnTick(float deltaTime);
		bool IsOnCooldown(int id);
		void AddCooldown(int id, CooldownInstance cooldown);
		void RemoveCooldown(int id);
	}
}