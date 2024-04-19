namespace FishMMO.Shared
{
	public interface ICooldownController : ICharacterBehaviour
	{
		void OnTick(float deltaTime);
		bool IsOnCooldown(int id);
		void AddCooldown(int id, CooldownInstance cooldown);
		void RemoveCooldown(int id);
	}
}