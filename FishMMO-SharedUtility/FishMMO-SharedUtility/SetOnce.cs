namespace FishMMO.Shared
{
	/// <summary>
	/// Thread-safe wrapper that allows a value to be set only once.
	/// Optimized for lock-free reads after the initial assignment.
	/// </summary>
	public class SetOnce<T>
	{
		private readonly object lockObj = new object();

		// Volatile ensures that all threads see the most up-to-date state of isSet
		private volatile bool isSet = false;
		private T value = default!;

		/// <summary>
		/// Gets or sets the value. Can only be set once; subsequent sets are ignored.
		/// </summary>
		public T Value
		{
			get
			{
				// Most of the time, isSet will be true. 
				// We return immediately without a lock for maximum performance.
				return value;
			}
			set
			{
				// Only enter the lock if the value hasn't been set yet
				if (!isSet)
				{
					lock (lockObj)
					{
						// Double-check to prevent race conditions during the set
						if (!isSet)
						{
							this.value = value;
							isSet = true;
						}
					}
				}
			}
		}

		/// <summary>
		/// Returns true if the value has already been assigned.
		/// </summary>
		public bool IsSet => isSet;

		/// <summary>
		/// Implicit conversion operator for easier usage in logic.
		/// </summary>
		public static implicit operator T?(SetOnce<T>? convert)
		{
			return convert != null ? convert.Value : default;
		}
	}
}