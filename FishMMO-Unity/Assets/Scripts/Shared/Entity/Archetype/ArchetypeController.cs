using FishNet.Connection;
using FishNet.Serializing;
using System;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Controls the archetype state for a character. Handles archetype assignment, network payload
	/// serialization, and event invocation when the archetype changes. The archetype determines
	/// which abilities, items, buffs, titles, and attributes a character has access to.
	/// </summary>
	public class ArchetypeController : CharacterBehaviour, IArchetypeController
	{
		/// <summary>
		/// Event triggered when the character's archetype changes. Provides the old and new archetype templates.
		/// The old template may be null if no archetype was previously assigned.
		/// </summary>
		public event Action<ArchetypeTemplate, ArchetypeTemplate> OnArchetypeChanged;

		/// <summary>
		/// The archetype template currently assigned to this character.
		/// </summary>
		public ArchetypeTemplate Template { get; private set; }

		/// <summary>
		/// Resets the archetype controller's state, clearing the current archetype template.
		/// </summary>
		/// <param name="asServer">Whether the reset is performed on the server.</param>
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);

			Template = null;
		}

		/// <summary>
		/// Reads the archetype payload from the network. Reads the template ID and assigns
		/// the corresponding archetype template from the cache.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="reader">The network reader containing serialized data.</param>
		public override void ReadPayload(NetworkConnection connection, Reader reader)
		{
			int templateID = reader.ReadInt32();
			if (templateID >= 0)
			{
				SetArchetype(templateID);
			}
		}

		/// <summary>
		/// Writes the archetype payload to the network. Writes the template ID for the
		/// currently assigned archetype, or -1 if no archetype is assigned.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="writer">The network writer to serialize data into.</param>
		public override void WritePayload(NetworkConnection connection, Writer writer)
		{
			writer.WriteInt32(Template != null ? Template.ID : -1);
		}

		/// <summary>
		/// Sets the character's archetype by template ID. Looks up the template from the
		/// <see cref="CachedScriptableObject{T}"/> cache and assigns it.
		/// </summary>
		/// <param name="templateID">The cached ID of the archetype template to assign.</param>
		public void SetArchetype(int templateID)
		{
			ArchetypeTemplate template = ArchetypeTemplate.Get<ArchetypeTemplate>(templateID);
			if (template == null)
			{
				Log.Warning("ArchetypeController", $"Failed to find ArchetypeTemplate with ID {templateID}.");
				return;
			}
			SetArchetype(template);
		}

		/// <summary>
		/// Sets the character's archetype by template reference. Invokes <see cref="OnArchetypeChanged"/>
		/// if the archetype actually changes.
		/// </summary>
		/// <param name="template">The archetype template to assign.</param>
		public void SetArchetype(ArchetypeTemplate template)
		{
			if (template == null)
			{
				Log.Warning("ArchetypeController", "Attempted to set a null ArchetypeTemplate.");
				return;
			}

			// Skip if the archetype is already assigned.
			if (Template != null && Template.ID == template.ID)
			{
				return;
			}

			ArchetypeTemplate oldTemplate = Template;
			Template = template;

			OnArchetypeChanged?.Invoke(oldTemplate, Template);
		}
	}
}