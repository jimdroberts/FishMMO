namespace FishMMO.Shared
{
	public interface IEquippable<T>
	{
		void Equip(T owner);
		void Unequip();
	}
}