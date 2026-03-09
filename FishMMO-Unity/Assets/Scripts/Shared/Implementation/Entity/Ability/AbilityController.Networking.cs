using FishNet.Connection;
using FishNet.Serializing;
using FishNet.Transporting;
using System.Collections.Generic;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Partial class for AbilityController handling network payload serialization
	/// and client-side broadcast registration for abilities and knowledge.
	/// </summary>
	public partial class AbilityController
	{
		/// <summary>
		/// Reusable buffer for batch known-ability broadcast processing.
		/// </summary>
		private readonly List<BaseAbilityTemplate> knownAbilityBuffer = new List<BaseAbilityTemplate>();

		/// <summary>
		/// Reusable buffer for batch known-ability-event broadcast processing.
		/// </summary>
		private readonly List<AbilityEvent> knownAbilityEventBuffer = new List<AbilityEvent>();

#if !UNITY_SERVER
		public override void OnStartCharacter()
		{
			base.OnStartCharacter();

			if (!base.IsOwner)
			{
				this.enabled = false;
			}
			else
			{
				ClientManager.RegisterBroadcast<KnownAbilityAddBroadcast>(OnClientKnownAbilityAddBroadcastReceived);
				ClientManager.RegisterBroadcast<KnownAbilityAddMultipleBroadcast>(OnClientKnownAbilityAddMultipleBroadcastReceived);
				ClientManager.RegisterBroadcast<KnownAbilityEventAddBroadcast>(OnClientKnownAbilityEventAddBroadcastReceived);
				ClientManager.RegisterBroadcast<KnownAbilityEventAddMultipleBroadcast>(OnClientKnownAbilityEventAddMultipleBroadcastReceived);
				ClientManager.RegisterBroadcast<AbilityAddBroadcast>(OnClientAbilityAddBroadcastReceived);
				ClientManager.RegisterBroadcast<AbilityAddMultipleBroadcast>(OnClientAbilityAddMultipleBroadcastReceived);

				// invoke client reset event
				OnReset?.Invoke();

				foreach (Ability ability in KnownAbilities.Values)
				{
					// update our client with abilities
					OnAddAbility?.Invoke(ability);
				}
			}
		}

		public override void OnStopCharacter()
		{
			base.OnStopCharacter();

			if (base.IsOwner)
			{
				ClientManager.UnregisterBroadcast<KnownAbilityAddBroadcast>(OnClientKnownAbilityAddBroadcastReceived);
				ClientManager.UnregisterBroadcast<KnownAbilityAddMultipleBroadcast>(OnClientKnownAbilityAddMultipleBroadcastReceived);
				ClientManager.UnregisterBroadcast<KnownAbilityEventAddBroadcast>(OnClientKnownAbilityEventAddBroadcastReceived);
				ClientManager.UnregisterBroadcast<KnownAbilityEventAddMultipleBroadcast>(OnClientKnownAbilityEventAddMultipleBroadcastReceived);
				ClientManager.UnregisterBroadcast<AbilityAddBroadcast>(OnClientAbilityAddBroadcastReceived);
				ClientManager.UnregisterBroadcast<AbilityAddMultipleBroadcast>(OnClientAbilityAddMultipleBroadcastReceived);
			}
		}

		/// <summary>
		/// Server sent an add known ability broadcast.
		/// </summary>
		private void OnClientKnownAbilityAddBroadcastReceived(KnownAbilityAddBroadcast msg, Channel channel)
		{
			BaseAbilityTemplate baseAbilityTemplate = BaseAbilityTemplate.Get<BaseAbilityTemplate>(msg.TemplateID);
			if (baseAbilityTemplate != null)
			{
				LearnBaseAbility(baseAbilityTemplate);

				OnAddKnownAbility?.Invoke(baseAbilityTemplate);
			}
		}

		/// <summary>
		/// Server sent an add known ability broadcast.
		/// </summary>
		private void OnClientKnownAbilityAddMultipleBroadcastReceived(KnownAbilityAddMultipleBroadcast msg, Channel channel)
		{
			knownAbilityBuffer.Clear();
			foreach (KnownAbilityAddBroadcast knownAbility in msg.Abilities)
			{
				BaseAbilityTemplate baseAbilityTemplate = BaseAbilityTemplate.Get<BaseAbilityTemplate>(knownAbility.TemplateID);
				if (baseAbilityTemplate != null)
				{
					knownAbilityBuffer.Add(baseAbilityTemplate);
				}
			}

			// Learn all templates before firing events so listeners that query
			// KnownBaseAbilities see a consistent, fully-populated set.
			LearnBaseAbilities(knownAbilityBuffer);

			foreach (BaseAbilityTemplate baseAbilityTemplate in knownAbilityBuffer)
			{
				OnAddKnownAbility?.Invoke(baseAbilityTemplate);
			}
		}

		/// <summary>
		/// Server sent an add known ability event broadcast.
		/// </summary>
		private void OnClientKnownAbilityEventAddBroadcastReceived(KnownAbilityEventAddBroadcast msg, Channel channel)
		{
			AbilityEvent abilityEvent = AbilityEvent.Get<AbilityEvent>(msg.TemplateID);
			if (abilityEvent != null)
			{
				LearnAbilityEvent(abilityEvent);

				OnAddKnownAbilityEvent?.Invoke(abilityEvent);
			}
		}

		/// <summary>
		/// Server sent an add known ability broadcast.
		/// </summary>
		private void OnClientKnownAbilityEventAddMultipleBroadcastReceived(KnownAbilityEventAddMultipleBroadcast msg, Channel channel)
		{
			knownAbilityEventBuffer.Clear();
			foreach (KnownAbilityEventAddBroadcast knownAbilityEvent in msg.AbilityEvents)
			{
				AbilityEvent abilityEvent = AbilityEvent.Get<AbilityEvent>(knownAbilityEvent.TemplateID);
				if (abilityEvent != null)
				{
					knownAbilityEventBuffer.Add(abilityEvent);
				}
			}

			// Learn all events before firing notifications so listeners that query
			// KnownAbilityEvents see a consistent, fully-populated set.
			LearnAbilityEvents(knownAbilityEventBuffer);

			foreach (AbilityEvent abilityEvent in knownAbilityEventBuffer)
			{
				OnAddKnownAbilityEvent?.Invoke(abilityEvent);
			}
		}

		/// <summary>
		/// Server sent an add ability broadcast.
		/// </summary>
		private void OnClientAbilityAddBroadcastReceived(AbilityAddBroadcast msg, Channel channel)
		{
			AbilityTemplate abilityTemplate = AbilityTemplate.Get<AbilityTemplate>(msg.TemplateID);
			if (abilityTemplate != null)
			{
				Ability newAbility = new Ability(msg.ID, abilityTemplate, msg.Events);
				LearnAbility(newAbility);

				OnAddAbility?.Invoke(newAbility);
			}
		}

		/// <summary>
		/// Server sent an add multiple ability broadcast.
		/// </summary>
		private void OnClientAbilityAddMultipleBroadcastReceived(AbilityAddMultipleBroadcast msg, Channel channel)
		{
			foreach (AbilityAddBroadcast ability in msg.Abilities)
			{
				AbilityTemplate abilityTemplate = AbilityTemplate.Get<AbilityTemplate>(ability.TemplateID);
				if (abilityTemplate != null)
				{
					Ability newAbility = new Ability(ability.ID, abilityTemplate, ability.Events);
					LearnAbility(newAbility);

					OnAddAbility?.Invoke(newAbility);
				}
			}
		}
#endif

		/// <summary>
		/// Reads the ability controller's state from the network payload, including ability RNG seed, known abilities, and cooldowns.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <param name="reader">The network reader to read from.</param>
		public override void ReadPayload(NetworkConnection conn, Reader reader)
		{
			const int maxPayloadAbilities = 2048;
			const int maxPayloadAbilityEvents = 512;

			// Read the AbilitySeedGenerator seed
			abilitySeed = reader.ReadInt32();

			// Instantiate the AbilitySeedGenerator
			abilitySeedGenerator = new DeterministicRNG(abilitySeed);

			// Set the initial seed
			currentSeed = abilitySeedGenerator.Next();

			//Log.Debug($"Received AbilitySeedGenerator Seed {abilitySeed}\r\nCurrent Seed {currentSeed}");

			int abilityCount = reader.ReadInt32();
			if (abilityCount < 0)
			{
				Log.Error("AbilityController", $"ReadPayload: invalid ability count {abilityCount}. Treating as empty.");
				abilityCount = 0;
			}
			else if (abilityCount > maxPayloadAbilities)
			{
				Log.Error("AbilityController", $"ReadPayload: ability count {abilityCount} exceeds limit {maxPayloadAbilities}. Aborting payload read.");
				return;
			}

			KnownAbilities.Clear();
			KnownBaseAbilities.Clear();
			KnownAbilityEvents.Clear();
			KnownAbilityOnTickEvents.Clear();
			KnownAbilityOnHitEvents.Clear();
			KnownAbilityOnPreSpawnEvents.Clear();
			KnownAbilityOnSpawnEvents.Clear();
			KnownAbilityOnDestroyEvents.Clear();

			List<int> abilityEvents = readPayloadAbilityEvents;
			for (int i = 0; i < abilityCount; ++i)
			{
				long abilityID = reader.ReadInt64();
				int abilityTemplateID = reader.ReadInt32();

				abilityEvents.Clear();
				int abilityEventsCount = reader.ReadInt32();
				if (abilityEventsCount < 0)
				{
					Log.Error("AbilityController", $"ReadPayload: invalid ability event count {abilityEventsCount} for abilityID {abilityID}. Skipping ability.");
					continue;
				}
				if (abilityEventsCount > maxPayloadAbilityEvents)
				{
					Log.Error("AbilityController", $"ReadPayload: ability event count {abilityEventsCount} exceeds limit {maxPayloadAbilityEvents} for abilityID {abilityID}. Aborting payload read.");
					// The event count is outside the valid range. Attempting to drain an
					// adversarially large count would stall the main thread before the
					// Reader throws. The payload is unrecoverable — abort entirely.
					return;
				}

				for (int j = 0; j < abilityEventsCount; ++j)
				{
					abilityEvents.Add(reader.ReadInt32());
				}

				// Validate the template exists before constructing the Ability.
				// A corrupted or outdated payload could contain an invalid template ID;
				// Ability.Initialize would NullRef on Template.Name if we proceeded.
				AbilityTemplate abilityTemplate = AbilityTemplate.Get<AbilityTemplate>(abilityTemplateID);
				if (abilityTemplate == null)
				{
					Log.Error("AbilityController", $"ReadPayload: invalid ability template ID {abilityTemplateID} for abilityID {abilityID}. Skipping.");
					continue;
				}

				Ability ability = new Ability(abilityID, abilityTemplate, abilityEvents);

				LearnAbility(ability);
			}

			if (Character.TryGet(out ICooldownController cooldownController))
			{
				cooldownController.Read(reader, base.TimeManager.LocalTick);
			}
		}

		/// <summary>
		/// Writes the ability controller's state to the network payload, including ability RNG seed, known abilities, and cooldowns.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <param name="writer">The network writer to write to.</param>
		public override void WritePayload(NetworkConnection conn, Writer writer)
		{
			// Check if we already instantiated an RNG for this ability controller
			if (abilitySeedGenerator == null)
			{
				// Generate an AbilitySeedGenerator Seed
				abilitySeed = playerSeedGenerator.Next();

				// Instantiate the AbilitySeedGenerator on the server
				abilitySeedGenerator = new DeterministicRNG(abilitySeed);

				// Set the initial seed
				currentSeed = abilitySeedGenerator.Next();
			}

			// Write the ability RNG seed for the clients
			writer.WriteInt32(abilitySeed);

			//Log.Debug($"Writing AbilitySeedGenerator Seed {abilitySeed}\r\nCurrent Seed {currentSeed}");

			// Write the abilities for the clients
			writer.WriteInt32(KnownAbilities.Count);
			foreach (Ability ability in KnownAbilities.Values)
			{
				writer.WriteInt64(ability.ID);
				writer.WriteInt32(ability.Template.ID);

				// Count includes the TypeOverride if present (serialized as an extra event ID).
				int eventCount = ability.AbilityEvents.Count;
				bool hasTypeOverride = ability.TypeOverride != null;
				if (hasTypeOverride)
				{
					eventCount++;
				}

				writer.WriteInt32(eventCount);
				foreach (int abilityEvent in ability.AbilityEvents.Keys)
				{
					writer.WriteInt32(abilityEvent);
				}
				if (hasTypeOverride)
				{
					writer.WriteInt32(ability.TypeOverride.ID);
				}
			}

			if (Character.TryGet(out ICooldownController cooldownController))
			{
				cooldownController.Write(writer);
			}
		}
	}
}