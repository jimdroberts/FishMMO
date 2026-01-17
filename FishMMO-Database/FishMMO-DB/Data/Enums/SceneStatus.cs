namespace FishMMO.Database.Data.Enums
{
	/// <summary>
	/// Scene status enumeration for scene loading lifecycle.
	/// </summary>
	public enum SceneStatus
	{
		/// <summary>
		/// Scene is pending load.
		/// </summary>
		Pending = 0,

		/// <summary>
		/// Scene is currently loading.
		/// </summary>
		Loading = 1,

		/// <summary>
		/// Scene is ready for players.
		/// </summary>
		Ready = 2,

		/// <summary>
		/// Scene loading failed.
		/// </summary>
		Failed = 3
	}
}