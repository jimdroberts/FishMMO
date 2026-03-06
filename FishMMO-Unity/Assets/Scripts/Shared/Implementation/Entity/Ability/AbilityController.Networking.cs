using FishNet.Connection;
using FishNet.Serializing;
using FishNet.Transporting;
using System.Collections.Generic;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Partial class for AbilityController handling network payload serialization
	/// and client-side broadcast registration for abilities and knowledge.
	/// </summary>
	public partial class AbilityController
	{
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
			List<BaseAbilityTemplate> templates = new List<BaseAbilityTemplate>();
			foreach (KnownAbilityAddBroadcast knownAbility in msg.Abilities)
			{
				BaseAbilityTemplate baseAbilityTemplate = BaseAbilityTemplate.Get<BaseAbilityTemplate>(knownAbility.TemplateID);
				if (baseAbilityTemplate != null)
				{
					templates.Add(baseAbilityTemplate);

					OnAddKnownAbility?.Invoke(baseAbilityTemplate);
				}
			}
			LearnBaseAbilities(templates);
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
			List<AbilityEvent> events = new List<AbilityEvent>();
			foreach (KnownAbilityEventAddBroadcast knownAbilityEvent in msg.AbilityEvents)
			{
				AbilityEvent abilityEvent = AbilityEvent.Get<AbilityEvent>(knownAbilityEvent.TemplateID);
				if (abilityEvent != null)
				{
					events.Add(abilityEvent);

					OnAddKnownAbilityEvent?.Invoke(abilityEvent);
				}
			}
			LearnAbilityEvents(events);
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
			// Read the AbilitySeedGenerator seed
			abilitySeed = reader.ReadInt32();

			// Instantiate the AbilitySeedGenerator
			abilitySeedGenerator = new System.Random(abilitySeed);

			// Set the initial seed
			currentSeed = abilitySeedGenerator.Next();

			//Log.Debug($"Received AbilitySeedGenerator Seed {abilitySeed}\r\nCurrent Seed {currentSeed}");

			int abilityCount = reader.ReadInt32();
			if (abilityCount < 1)
			{
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

			for (int i = 0; i < abilityCount; ++i)
			{
				long abilityID = reader.ReadInt64();
				int abilityTemplateID = reader.ReadInt32();

				List<int> abilityEvents = new List<int>();
				int abilityEventsCount = reader.ReadInt32();
				for (int j = 0; j < abilityEventsCount; ++j)
				{
					abilityEvents.Add(reader.ReadInt32());
				}
				Ability ability = new Ability(abilityID, abilityTemplateID, abilityEvents);

				LearnAbility(ability);
			}

			if (Character.TryGet(out ICooldownController cooldownController))
			{
				cooldownController.Read(reader);
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
				abilitySeedGenerator = new System.Random(abilitySeed);

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

				writer.WriteInt32(ability.AbilityEvents.Count);
				foreach (int abilityEvent in ability.AbilityEvents.Keys)
				{
					writer.WriteInt32(abilityEvent);
				}
			}

			if (Character.TryGet(out ICooldownController cooldownController))
			{
				cooldownController.Write(writer);
			}
		}
	}
}