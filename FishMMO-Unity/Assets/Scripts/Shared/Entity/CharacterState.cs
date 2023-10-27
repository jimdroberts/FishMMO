namespace FishMMO.Shared
{
	public enum CharacterState : byte
	{
		Idle = 0,
		IsMoving,
		IsRunning,
		IsCrouching,
		IsSwimming,
		IsTeleporting,
		IsFrozen,
		IsStunned,
		IsMesmerized,
		IsGameMaster,
	}
}