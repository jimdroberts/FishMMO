namespace FishMMO.Shared
{
	/// <summary>
	/// Thread-safe wrapper that allows a value to be set only once.
	/// Optimized for lock-free reads after the initial assignment.
	/// Uses both a volatile isSet guard and the lock's implicit memory
	/// barrier to ensure all threads see the written value on weak
	/// memory-model architectures (ARM, ARM64).
	/// </summary>
	public class SetOnce<T>
	{
		private readonly object lockObj = new object();

		// Volatile ensures that all threads see the most up-to-date state of isSet.
		// The lock in the setter provides a full fence (release on exit), ensuring
		// the write to `value` happens-before the write to `isSet = true`.
		// On the read side, we use Volatile.Read on `value` to pair with that release,
		// guaranteeing visibility without taking the lock on the hot path.
		private volatile bool isSet = false;
		private T value = default!;

		/// <summary>
		/// Gets or sets the value. Can only be set once; subsequent sets are ignored.
		/// Reading before a value has been set throws <see cref="InvalidOperationException"/>.
		/// </summary>
		public T Value
		{
			get
			{
				// Guard: reading before SetOnce has been assigned is a programmer error.
				if (!isSet)
					throw new System.InvalidOperationException("SetOnce value has not been set.");

				// Full memory barrier pairs with the lock's implicit release-fence
				// in the setter. This guarantees that on ARM/ARM64 we see the write
				// to `value`, not stale cache. Uses Interlocked.MemoryBarrier instead
				// of Volatile.Read<T> because the latter requires T : class in .NET 10+,
				// and SetOnce<T> must support unconstrained T (value and reference types).
				System.Threading.Interlocked.MemoryBarrier();
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
							// The lock's implicit release-fence on exit ensures
							// the write to `this.value` is visible before any
							// subsequent read of `isSet == true` on another thread.
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
		/// Returns <c>default(T)</c> if the wrapper is null.
		/// Throws <see cref="InvalidOperationException"/> if the value is not yet set.
		/// </summary>
		public static implicit operator T?(SetOnce<T>? convert)
		{
			return convert != null ? convert.Value : default;
		}
	}
}
