using System;
using FishNet.Serializing;

namespace FishMMO.Shared
{
	public interface ICooldownController : ICharacterBehaviour
	{
		static Action<long, CooldownInstance> OnAddCooldown;
		static Action<long, CooldownInstance> OnUpdateCooldown;
		static Action<long> OnRemoveCooldown;

		void Read(Reader reader);
		void Write(Writer writer);
		void OnTick(float deltaTime);
		bool IsOnCooldown(long id);
		bool TryGetCooldown(long id, out float cooldown);
		void AddCooldown(long id, CooldownInstance cooldown);
		void RemoveCooldown(long id);
		void Clear();
	}
}