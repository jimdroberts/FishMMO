namespace FishMMO.Shared
{
	public interface IStackable<T>
	{
		bool CanAddToStack(T other);
		bool AddToStack(T other);
		bool TryUnstack(uint amount, out T stack);
	}
}