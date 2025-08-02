namespace FishMMO.Shared
{
	/// <summary>
	/// Interface for entities that can be killed by a character or other source.
	/// Implement this to allow objects to be killed and trigger death logic.
	/// </summary>
	public interface IKillable
	{
		/// <summary>
		/// Kills the entity, triggering death logic and effects.
		/// </summary>
		/// <param name="killer">The character responsible for the kill.</param>
		void Kill(ICharacter killer);
	}
}