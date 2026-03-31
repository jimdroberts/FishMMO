using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA event data for archetype change events, carrying the new and previous archetype templates.
	/// </summary>
	public class ArchetypeEventData : EventData
	{
		/// <summary>
		/// The new archetype template applied to the character.
		/// </summary>
		public ArchetypeTemplate NewTemplate { get; }

		/// <summary>
		/// The previous archetype template before the change.
		/// </summary>
		public ArchetypeTemplate PreviousTemplate { get; }

		/// <summary>
		/// Creates a new ArchetypeEventData.
		/// </summary>
		/// <param name="initiator">The character whose archetype changed.</param>
		/// <param name="newTemplate">The new archetype template.</param>
		/// <param name="previousTemplate">The previous archetype template (may be null).</param>
		public ArchetypeEventData(ICharacter initiator, ArchetypeTemplate newTemplate, ArchetypeTemplate previousTemplate)
			: base(initiator)
		{
			NewTemplate = newTemplate;
			PreviousTemplate = previousTemplate;
		}
	}
}