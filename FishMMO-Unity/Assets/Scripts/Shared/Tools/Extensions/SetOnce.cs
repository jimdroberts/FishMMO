namespace FishMMO.Shared
{
	/// <summary>
	/// Thread-safe wrapper that allows a value to be set only once.
	/// Subsequent sets are ignored. Useful for lazy initialization or single-assignment scenarios.
	/// </summary>
	public class SetOnce<T>
	{
		/// <summary>
		/// Lock object for thread safety.
		/// </summary>
		private readonly object lockObj = new object();

		/// <summary>
		/// Indicates whether the value has been set.
		/// </summary>
		private bool isSet = false;

		/// <summary>
		/// The stored value.
		/// </summary>
		private T value;

		/// <summary>
		/// Gets or sets the value. Can only be set once; subsequent sets are ignored.
		/// Thread-safe.
		/// </summary>
		public T Value
		{
			get
			{
				lock (this.lockObj)
				{
					return this.value;
				}
			}
			set
			{
				lock (this.lockObj)
				{
					if (this.isSet)
					{
						return;
					}
					this.isSet = true;
					this.value = value;
				}
			}
		}

		/// <summary>
		/// Implicit conversion operator to allow SetOnce<T> to be used as T.
		/// </summary>
		/// <param name="convert">The SetOnce instance to convert.</param>
		public static implicit operator T(SetOnce<T> convert)
		{
			return convert.Value;
		}
	}
}