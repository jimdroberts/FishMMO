public class CooldownInstance
{
	private float remainingTime;

	public bool IsOnCooldown
	{
		get
		{
			return remainingTime > 0.0f;
		}
	}

	public CooldownInstance(float cooldown)
	{
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