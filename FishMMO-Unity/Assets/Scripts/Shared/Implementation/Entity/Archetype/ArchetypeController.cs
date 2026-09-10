using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Serializing;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

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

		[Header("ECA - Archetype")]
		[Tooltip("Triggers invoked when the character's archetype changes.")]
		[SerializeField]
		private List<Trigger> onArchetypeChangeTriggers = new List<Trigger>();

		/// <inheritdoc />
		public List<Trigger> OnArchetypeChangeTriggers => onArchetypeChangeTriggers;

		/// <summary>
		/// The archetype template currently assigned to this character.
		/// </summary>
		public ArchetypeTemplate Template { get; private set; }

		/// <summary>
		/// Persistence version of the character's archetype row. Advanced on every change and by
		/// the save snapshot; compared by <see cref="MarkPersisted"/>. Loaded from the row on login.
		/// </summary>
		public long Version { get; set; }

		/// <summary>Whether the archetype has changed since the database last confirmed it.</summary>
		public bool PersistenceDirty { get; private set; }

		/// <summary>Clears the dirty mark if nothing has changed since the confirmed snapshot was taken.</summary>
		public void MarkPersisted(long persistedVersion)
		{
			if (Version == persistedVersion)
			{
				PersistenceDirty = false;
			}
		}

		/// <summary>
		/// Installs a persisted archetype without events, requirement checks or dirtying — the
		/// load path.
		/// </summary>
		public void Restore(int templateID, long version)
		{
			Template = ArchetypeTemplate.Get<ArchetypeTemplate>(templateID);
			if (Template == null)
			{
				Log.Warning("ArchetypeController", $"Restore: no ArchetypeTemplate is registered for ID {templateID}; the character loads without an archetype.");
			}
			Version = version;
			PersistenceDirty = false;
		}

		/// <summary>
		/// Resets the archetype controller's state, clearing the current archetype template.
		/// </summary>
		/// <param name="asServer">Whether the reset is performed on the server.</param>
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);

			Template = null;
			Version = 0;
			PersistenceDirty = false;
		}

		/// <summary>
		/// Reads the archetype payload from the network. Reads the template ID and assigns
		/// the corresponding archetype template from the cache.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="reader">The network reader containing serialized data.</param>
		public override void ReadPayload(NetworkConnection connection, Reader reader)
		{
			int templateID = reader.ReadInt32Unpacked();
			if (templateID >= 0)
			{
				/* The restore path: install the value, raise nothing.
				 *
				 * This runs on every peer for every character that comes into observer range, and
				 * the default non-skipping path ends in Character.Invoke(onArchetypeChangeTriggers).
				 * BaseCharacter.Invoke has no authority gate and only a minority of action types
				 * gate themselves with EcaAuthority, so a stranger walking past used to run that
				 * stranger's archetype-change triggers on the onlooker's machine. This is exactly
				 * the defect FactionController.ReadPayload documents having fixed for factions. */
				RestoreArchetype(templateID);
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
			/* Unpacked. Template ids are a deterministic 32-bit hash
			 * (CachedScriptableObject.AddToCache), so they span the whole range and the
			 * signed-packed form spends FIVE bytes on one. See ObservedBuffEntry. */
			writer.WriteInt32Unpacked(Template != null ? Template.ID : -1);
		}

		/// <summary>
		/// Sets the character's archetype by template ID. Looks up the template from the
		/// <see cref="CachedScriptableObject{T}"/> cache and assigns it.
		/// </summary>
		/// <param name="templateID">The cached ID of the archetype template to assign.</param>
		public void SetArchetype(int templateID)
		{
			ApplyArchetype(templateID, skipEvent: false);
		}

		/// <summary>
		/// Installs an archetype this peer was TOLD about — the spawn payload and the observer
		/// broadcast — raising nothing.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The counterpart of <c>FactionController.ApplyFactionValue</c>, and it exists for the same
		/// reason: both of those paths run on every peer for every character that comes into
		/// observer range, and <see cref="SetArchetype(int)"/> ends in
		/// <c>Character.Invoke(onArchetypeChangeTriggers)</c>. <c>BaseCharacter.Invoke</c> has no
		/// authority gate and only a minority of action types gate themselves with
		/// <c>EcaAuthority</c>, so a stranger walking past would run that stranger's
		/// archetype-change triggers on the onlooker's machine.
		/// </para>
		/// <para>
		/// A separate method rather than a <c>skipEvent</c> parameter on <see cref="SetArchetype(int)"/>
		/// because <see cref="IArchetypeController"/> declares the two-argument-free signatures, and
		/// an implementation with an extra optional parameter does not satisfy an interface member.
		/// Adding the flag to the interface — matching
		/// <c>IFactionController.SetFaction(int, int, bool)</c> — would be the tidier shape.
		/// </para>
		/// </remarks>
		/// <param name="templateID">The cached ID of the archetype template to install.</param>
		public void RestoreArchetype(int templateID)
		{
			ApplyArchetype(templateID, skipEvent: true);
		}

		/// <summary>Resolves <paramref name="templateID"/> and hands it to the single apply core.</summary>
		private void ApplyArchetype(int templateID, bool skipEvent)
		{
			ArchetypeTemplate template = ArchetypeTemplate.Get<ArchetypeTemplate>(templateID);
			if (template == null)
			{
				Log.Warning("ArchetypeController", $"Failed to find ArchetypeTemplate with ID {templateID}.");
				return;
			}
			ApplyArchetype(template, skipEvent);
		}

		/// <summary>
		/// Sets the character's archetype by template reference. Invokes <see cref="OnArchetypeChanged"/>
		/// if the archetype actually changes.
		/// </summary>
		/// <param name="template">The archetype template to assign.</param>
		public void SetArchetype(ArchetypeTemplate template)
		{
			ApplyArchetype(template, skipEvent: false);
		}

		/// <summary>
		/// The single apply core. Validates, assigns, persists and announces — the last of those
		/// only when <paramref name="skipEvent"/> is false. See <see cref="RestoreArchetype"/> for
		/// who passes true and why.
		/// </summary>
		/// <param name="template">The archetype template to assign.</param>
		/// <param name="skipEvent">True to install without raising anything.</param>
		private void ApplyArchetype(ArchetypeTemplate template, bool skipEvent)
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

			/* Server-authoritative requirements validation, decided at runtime rather than by a
			 * build define. UNITY_SERVER is a build-target define that is absent in the editor
			 * the scene server is developed in, so a define-gated check (and the broadcast below)
			 * silently did nothing there. Not because a client payload reaches this: ReadPayload
			 * only runs server-side for a PREDICTED spawn, and no prefab enables predicted
			 * spawning. It is here because every other caller — archetype selection, admin
			 * tooling, content scripts — reaches SetArchetype with an id the server has not
			 * checked, and the rewards (abilities, items, buffs, titles, attributes) are granted
			 * below. */
			bool isServer = base.NetworkObject != null && base.NetworkObject.IsServerInitialized;
			if (isServer)
			{
				if (!template.MeetsRequirements(PlayerCharacter))
				{
					Log.Warning("ArchetypeController", $"Character {Character?.ID} does not meet requirements for archetype {template.Name} ({template.ID}).");
					return;
				}
			}

			ArchetypeTemplate oldTemplate = Template;
			Template = template;

			if (isServer)
			{
				PersistenceDirty = true;
				++Version;
				SendArchetypeUpdate(template.ID);
			}

			if (skipEvent)
			{
				return;
			}

			// UI-facing, and stays on every peer that actually changed its own archetype.
			OnArchetypeChanged?.Invoke(oldTemplate, Template);

			/* ECA triggers only on the authoritative peer, the same rule BuffController.ApplyResolved
			 * follows. Actions are free to damage, grant items or move a character and only a
			 * minority of the action types gate themselves with EcaAuthority, so dispatching on a
			 * client ran the rest of them a second time, locally. */
			if (isServer)
			{
				Character.Invoke(onArchetypeChangeTriggers, new ArchetypeEventData(Character, Template, oldTemplate));
			}
		}

		/// <summary>
		/// Sends archetype updates to owner and observers.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Why the observer half is kept.</b> No client code reads
		/// <see cref="IArchetypeController"/> today, so both this channel and the non-owner half of
		/// <see cref="WritePayload"/> look write-only on the receiving side. They are not: a shared
		/// ECA condition reads it. <c>IsArchetypeCondition</c> evaluates
		/// <c>eventData.TargetCharacter ?? initiator</c>, so a predicted ability whose condition
		/// names the TARGET's archetype is evaluated on the caster's client against a peer's
		/// controller. Strip the archetype from that peer's copy and the condition answers
		/// differently on the client than on the server — a mispredict, not a blank label. One
		/// packed id per change is cheap, the value is not private progression data the way a
		/// reputation integer is (an archetype is legible from a character's gear and abilities),
		/// and a sentinel in the non-owner payload would buy a few bytes at the cost of that
		/// agreement.
		/// </para>
		/// </remarks>
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

			/* One serialisation for the whole observer set. The private loop this replaced called
			 * NetworkConnection.Broadcast per recipient, which writes the struct again for each one;
			 * ServerManager.Broadcast(HashSet, ...) writes it once and reuses the ArraySegment. The
			 * scope helper also owns the defensive copy that makes FishNet's set-mutating
			 * BroadcastExcept safe to use against NetworkObject.Observers. */
			ObserverBroadcastScope.BroadcastToObserversExceptOwner(base.NetworkObject, new CharacterObserverArchetypeUpdateBroadcast()
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

			/* The restore path for the same reason ReadPayload takes it: this is somebody else's
			 * archetype being installed on this machine, and the announcing path would run that
			 * character's ECA triggers here.
			 *
			 * Cast because ICharacter.TryGet throws on anything but an interface and
			 * IArchetypeController does not declare the restore entry point. */
			if (archetypeController is ArchetypeController concrete)
			{
				concrete.RestoreArchetype(msg.TemplateID);
			}
		}
#endif
	}
}