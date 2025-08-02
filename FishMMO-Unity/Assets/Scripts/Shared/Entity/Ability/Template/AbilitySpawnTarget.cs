namespace FishMMO.Shared
{
	/// <summary>
	/// Specifies the target location or entity for spawning an ability.
	/// Used to determine where an ability effect or object should appear in the game world.
	/// </summary>
	public enum AbilitySpawnTarget : byte
	{
		/// <summary>
		/// Spawn at the caster (self).
		/// </summary>
		Self = 0,

		/// <summary>
		/// Spawn at the caster's position (point blank).
		/// </summary>
		PointBlank,

		/// <summary>
		/// Spawn at the selected target.
		/// </summary>
		Target,

		/// <summary>
		/// Spawn forward from the caster.
		/// </summary>
		Forward,

		/// <summary>
		/// Spawn at the camera's position or direction.
		/// </summary>
		Camera,

		/// <summary>
		/// Spawn at the spawner's position.
		/// </summary>
		Spawner,

		/// <summary>
		/// Spawn at the spawner's position with the camera's rotation.
		/// </summary>
		SpawnerWithCameraRotation,
	}
}