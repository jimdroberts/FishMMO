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

		public CooldownInstance(float cooldown)
		{
			totalTime = cooldown;
			remainingTime = cooldown;
		}

		public void SubtractTime(float time)
		{
			remainingTime -= time;
		}

		public void AddTime(float time)
		{
			remainingTime += time;
		}
	}
}