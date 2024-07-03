using System;

namespace FishMMO.Shared
{
	public interface ICooldownController : ICharacterBehaviour
	{
		static Action<long, CooldownInstance> OnAddCooldown;
		static Action<long, CooldownInstance> OnUpdateCooldown;
		static Action<long> OnRemoveCooldown;

		void OnTick(float deltaTime);
		bool IsOnCooldown(long id);
		void AddCooldown(long id, CooldownInstance cooldown);
		void RemoveCooldown(long id);
	}
}