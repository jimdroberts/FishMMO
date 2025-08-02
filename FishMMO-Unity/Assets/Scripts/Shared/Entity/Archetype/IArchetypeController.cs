namespace FishMMO.Shared
{
	/// <summary>
	/// Interface for a character's archetype controller, providing access to the current archetype template.
	/// </summary>
	public interface IArchetypeController : ICharacterBehaviour
	{
		/// <summary>
		/// The archetype template currently assigned to this character.
		/// </summary>
		ArchetypeTemplate Template { get; }
	}
}