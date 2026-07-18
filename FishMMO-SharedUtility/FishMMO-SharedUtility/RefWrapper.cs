namespace FishMMO.Shared
{
	/// <summary>
	/// A reference-type wrapper for value types or generics.
	/// Allows value types to be passed and modified by reference across different systems.
	/// </summary>
	/// <typeparam name="T">The type of the value to wrap.</typeparam>
	public class RefWrapper<T>
	{
		/// <summary>
		/// The wrapped value. Accessible directly for high-performance scenarios.
		/// </summary>
		public T Value { get; set; }

		public RefWrapper(T value)
		{
			this.Value = value;
		}

		/// <summary>
		/// Updates the value and returns the wrapper for method chaining.
		/// </summary>
		public RefWrapper<T> Set(T newValue)
		{
			this.Value = newValue;
			return this;
		}

		/// <summary>
		/// Implicitly converts the wrapper to its underlying value.
		/// </summary>
		public static implicit operator T?(RefWrapper<T>? wrapper)
		{
			return wrapper != null ? wrapper.Value : default;
		}

		public override string ToString() => Value?.ToString() ?? "null";
	}
}