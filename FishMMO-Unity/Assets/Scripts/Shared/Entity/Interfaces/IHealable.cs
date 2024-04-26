namespace FishMMO.Shared
{
	public interface IHealable
	{
		public void Heal(ICharacter healer, int amount);
	}
}