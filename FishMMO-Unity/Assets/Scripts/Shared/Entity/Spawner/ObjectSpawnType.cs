namespace FishMMO.Shared
{
	/// <summary>
	/// Defines the method used to select which object to spawn from the spawner's list.
	/// </summary>
	public enum ObjectSpawnType : byte
	{
		/// <summary>
		/// Spawn objects in linear order, cycling through the list.
		/// </summary>
		Linear = 0,

		/// <summary>
		/// Spawn objects at random from the list.
		/// </summary>
		Random,

		/// <summary>
		/// Spawn objects based on weighted random selection using spawn chances.
		/// </summary>
		Weighted,
	}
}