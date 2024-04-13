namespace FishMMO.Shared
{
	public interface ICooldownController : ICharacterBehaviour
	{
		void OnTick(float deltaTime);
		bool IsOnCooldown(string name);
		void AddCooldown(string name, CooldownInstance cooldown);
		void RemoveCooldown(string name);
	}
}