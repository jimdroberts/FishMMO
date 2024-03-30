namespace FishMMO.Shared
{
	public interface ICooldownController : ICharacterBehaviour
	{
		bool IsOnCooldown(string name);
		void AddCooldown(string name, CooldownInstance cooldown);
		void RemoveCooldown(string name);
	}
}