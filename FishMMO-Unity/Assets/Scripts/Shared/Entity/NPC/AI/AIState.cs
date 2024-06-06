namespace FishMMO.Shared
{
	public enum AIState : byte
	{
		Idle = 0,
		Wander,
		Patrol,
		Retreat,
		Aggressive,
		Hunt,
	}
}