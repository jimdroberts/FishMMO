using System;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Interface for a character's archetype controller, providing access to the current archetype template
	/// and events for archetype changes.
	/// </summary>
	public interface IArchetypeController : ICharacterBehaviour
	{
		/// <summary>
		/// Event triggered when the character's archetype changes. Provides the old and new archetype templates.
		/// The old template may be null if no archetype was previously assigned.
		/// </summary>
		event Action<ArchetypeTemplate, ArchetypeTemplate> OnArchetypeChanged;

		/// <summary>
		/// The archetype template currently assigned to this character.
		/// </summary>
		ArchetypeTemplate Template { get; }

		/// <summary>
		/// Sets the character's archetype by template ID. Looks up the template from the cache and assigns it.
		/// </summary>
		/// <param name="templateID">The cached ID of the archetype template to assign.</param>
		void SetArchetype(int templateID);

		/// <summary>
		/// Sets the character's archetype by template reference.
		/// </summary>
		/// <param name="template">The archetype template to assign.</param>
		void SetArchetype(ArchetypeTemplate template);
	}
}