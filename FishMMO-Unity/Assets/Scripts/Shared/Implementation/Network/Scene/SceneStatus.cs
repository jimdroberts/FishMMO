namespace FishMMO.Shared
{
	/// <summary>
	/// Represents the status of a scene in the FishMMO server lifecycle.
	/// </summary>
	public enum SceneStatus : int
	{
		/// <summary>
		/// Scene is pending and has not started loading yet.
		/// </summary>
		Pending = 0,
		/// <summary>
		/// Scene is currently loading.
		/// </summary>
		Loading,
		/// <summary>
		/// Scene is fully loaded and ready.
		/// </summary>
		Ready,
		/// <summary>
		/// Scene failed to load.
		/// </summary>
		Failed,
	}
}