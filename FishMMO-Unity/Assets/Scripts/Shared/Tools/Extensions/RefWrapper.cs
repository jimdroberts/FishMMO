namespace FishMMO.Shared
{
	/// <summary>
	/// A simple wrapper class to hold a reference-like value for value types or generics.
	/// Useful for passing values by reference in contexts where only objects are allowed.
	/// </summary>
	public class RefWrapper<T>
	{
		/// <summary>
		/// The wrapped value.
		/// </summary>
		public T Value;

		/// <summary>
		/// Initializes a new instance of RefWrapper with the specified value.
		/// </summary>
		/// <param name="value">The value to wrap.</param>
		public RefWrapper(T value) { Value = value; }
	}
}