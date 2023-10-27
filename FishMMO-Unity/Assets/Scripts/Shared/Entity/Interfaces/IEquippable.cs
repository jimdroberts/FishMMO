namespace FishMMO.Shared
{
	public interface IEquippable<T>
	{
		T Owner { get; }
		void Equip(T owner);
		void Unequip();
	}
}