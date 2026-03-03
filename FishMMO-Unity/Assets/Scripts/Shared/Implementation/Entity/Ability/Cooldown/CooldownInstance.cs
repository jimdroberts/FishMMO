using System.Runtime.CompilerServices;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents a single cooldown instance for an ability.
	/// </summary>
	public class CooldownInstance
	{
		private float totalTime;
		private float remainingTime;

		/// <summary>
		/// The total cooldown time.
		/// </summary>
		public float TotalTime { get { return totalTime; } }

		/// <summary>
		/// The remaining cooldown time.
		/// </summary>
		public float RemainingTime { get { return remainingTime; } }

		/// <summary>
		/// Gets whether the cooldown is still active.
		/// </summary>
		public bool IsOnCooldown
		{
			get
			{
				return remainingTime > 0.0f;
			}
		}

		/// <summary>
		/// Initializes a new cooldown instance with the same total and remaining time.
		/// </summary>
		/// <param name="remainingTime">The cooldown time.</param>
		public CooldownInstance(float remainingTime)
		{
			this.totalTime = remainingTime;
			this.remainingTime = remainingTime;
		}

		/// <summary>
		/// Initializes a new cooldown instance with specified total and remaining time.
		/// </summary>
		/// <param name="totalTime">Total cooldown time.</param>
		/// <param name="remainingTime">Remaining cooldown time.</param>
		public CooldownInstance(float totalTime, float remainingTime)
		{
			this.totalTime = totalTime;
			this.remainingTime = remainingTime;
		}

		/// <summary>
		/// Subtracts time from the remaining cooldown.
		/// </summary>
		/// <param name="time">Time to subtract.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void SubtractTime(float time)
		{
			remainingTime -= time;
		}

		/// <summary>
		/// Adds time to the remaining cooldown.
		/// </summary>
		/// <param name="time">Time to add.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void AddTime(float time)
		{
			remainingTime += time;
		}
	}
}