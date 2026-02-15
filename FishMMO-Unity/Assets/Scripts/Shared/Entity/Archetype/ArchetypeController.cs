using FishNet.Connection;
using FishNet.Serializing;
using FishNet.Transporting;
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

#if UNITY_SERVER
			if (base.IsServerStarted)
			{
				SendArchetypeUpdate(template.ID);
			}
#endif

			OnArchetypeChanged?.Invoke(oldTemplate, Template);
		}

#if UNITY_SERVER
		/// <summary>
		/// Sends archetype updates to owner and observers.
		/// </summary>
		/// <param name="templateID">The archetype template ID to send.</param>
		private void SendArchetypeUpdate(int templateID)
		{
			if (Character == null)
			{
				return;
			}

			BroadcastToOwnerOnly(Character, new ArchetypeUpdateBroadcast()
			{
				TemplateID = templateID,
			}, Channel.Reliable);

			BroadcastToObserversOnly(Character, new CharacterObserverArchetypeUpdateBroadcast()
			{
				CharacterID = Character.ID,
				TemplateID = templateID,
			}, Channel.Reliable);
		}

		/// <summary>
		/// Broadcasts the payload to only the owner of the character.
		/// </summary>
		private static void BroadcastToOwnerOnly<T>(ICharacter character, T broadcast, Channel channel)
			where T : struct, IBroadcast
		{
			if (character?.Owner != null)
			{
				character.Owner.Broadcast(broadcast, true, channel);
			}
		}

		/// <summary>
		/// Broadcasts the payload to all current observers of the character, excluding the owner.
		/// </summary>
		private static void BroadcastToObserversOnly<T>(ICharacter character, T broadcast, Channel channel)
			where T : struct, IBroadcast
		{
			if (character == null || character.Observers == null)
			{
				return;
			}

			NetworkConnection owner = character.Owner;
			foreach (NetworkConnection observer in character.Observers)
			{
				if (observer == null || observer == owner)
				{
					continue;
				}

				observer.Broadcast(broadcast, true, channel);
			}
		}
#endif

#if !UNITY_SERVER
		/// <summary>
		/// Called when the character starts on the client. Registers archetype broadcast listeners.
		/// Non-owner character instances are disabled and updated through owner-routed observer broadcasts.
		/// </summary>
		public override void OnStartCharacter()
		{
			base.OnStartCharacter();

			if (!base.IsOwner)
			{
				enabled = false;
				return;
			}

			ClientManager.RegisterBroadcast<ArchetypeUpdateBroadcast>(OnClientArchetypeUpdateBroadcastReceived);
			ClientManager.RegisterBroadcast<CharacterObserverArchetypeUpdateBroadcast>(OnClientCharacterObserverArchetypeUpdateBroadcastReceived);
		}

		/// <summary>
		/// Called when the character stops on the client. Unregisters archetype broadcast listeners.
		/// </summary>
		public override void OnStopCharacter()
		{
			base.OnStopCharacter();

			if (base.IsOwner)
			{
				ClientManager.UnregisterBroadcast<ArchetypeUpdateBroadcast>(OnClientArchetypeUpdateBroadcastReceived);
				ClientManager.UnregisterBroadcast<CharacterObserverArchetypeUpdateBroadcast>(OnClientCharacterObserverArchetypeUpdateBroadcastReceived);
			}
		}

		/// <summary>
		/// Resolves a target archetype controller from the client character cache.
		/// </summary>
		private static bool TryGetCachedArchetypeController(long characterID, out IArchetypeController archetypeController)
		{
			archetypeController = null;
			if (characterID <= 0)
			{
				return false;
			}

			if (!BaseCharacter.ClientCharacters.TryGetValue(characterID, out ICharacter character) ||
				character == null)
			{
				return false;
			}

			return character.TryGet(out archetypeController);
		}

		/// <summary>
		/// Handles owner-targeted archetype updates from the server.
		/// </summary>
		private void OnClientArchetypeUpdateBroadcastReceived(ArchetypeUpdateBroadcast msg, Channel channel)
		{
			SetArchetype(msg.TemplateID);
		}

		/// <summary>
		/// Handles observer-targeted archetype updates from the server.
		/// </summary>
		private void OnClientCharacterObserverArchetypeUpdateBroadcastReceived(CharacterObserverArchetypeUpdateBroadcast msg, Channel channel)
		{
			if (!TryGetCachedArchetypeController(msg.CharacterID, out IArchetypeController archetypeController))
			{
				return;
			}

			archetypeController.SetArchetype(msg.TemplateID);
		}
#endif
	}
}