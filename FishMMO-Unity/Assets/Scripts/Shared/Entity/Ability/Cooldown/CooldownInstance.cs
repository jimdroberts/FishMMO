using System.Runtime.CompilerServices;

namespace FishMMO.Shared
{
	public class CooldownInstance
	{
		private float totalTime;
		private float remainingTime;

		public float TotalTime { get { return totalTime; } }
		public float RemainingTime { get { return remainingTime; } }

		public bool IsOnCooldown
		{
			get
			{
				return remainingTime > 0.0f;
			}
		}

		public CooldownInstance(float remainingTime)
		{
			this.totalTime = remainingTime;
			this.remainingTime = remainingTime;
		}

		public CooldownInstance(float totalTime, float remainingTime)
		{
			this.totalTime = totalTime;
			this.remainingTime = remainingTime;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void SubtractTime(float time)
		{
			remainingTime -= time;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void AddTime(float time)
		{
			remainingTime += time;
		}
	}
}