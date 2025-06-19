namespace FishMMO.Shared
{
	// A simple wrapper class to hold a reference-like value
	public class RefWrapper<T>
	{
		public T Value;
		public RefWrapper(T value) { Value = value; }
	}
}