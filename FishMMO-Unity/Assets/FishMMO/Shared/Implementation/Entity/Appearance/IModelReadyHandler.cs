namespace FishMMO.Shared
{
	/// <summary>
	/// Implemented by character behaviours that need to re-initialize
	/// when the character's visual model finishes loading asynchronously.
	/// Called from <see cref="BaseCharacter.InstantiateRaceModelFromIndex"/>
	/// after the model is instantiated and the Animator is wired.
	/// </summary>
	public interface IModelReadyHandler
	{
		/// <summary>
		/// Called when the character model has finished loading.
		/// At this point, the skeleton, Animator, and body region renderers are available.
		/// </summary>
		void OnModelReady();
	}
}
