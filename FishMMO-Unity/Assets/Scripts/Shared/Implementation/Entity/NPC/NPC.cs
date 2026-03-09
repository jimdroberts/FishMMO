using System.Collections.Generic;
using UnityEngine;
using FishNet.Component.Transforming;
using FishNet.Observing;
using FishNet.Connection;
using FishNet.Serializing;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents a non-player character (NPC) in the game. Handles attribute generation, network payloads, and spawning logic.
	/// </summary>
	[RequireComponent(typeof(AIController))]
	[RequireComponent(typeof(AbilityController))]
	[RequireComponent(typeof(BuffController))]
	[RequireComponent(typeof(CharacterAttributeController))]
	[RequireComponent(typeof(CharacterDamageController))]
	[RequireComponent(typeof(FactionController))]
	[RequireComponent(typeof(NetworkTransform))]
	[RequireComponent(typeof(NetworkObserver))]
	public class NPC : BaseCharacter, ISceneObject, ISpawnable
	{
		/// <summary>
		/// Static random number generator for NPC attribute seed generation.
		/// </summary>
		private static DeterministicRNG npcSeedGenerator = new DeterministicRNG();

		/// <summary>
		/// Random number generator for this NPC, seeded for deterministic results.
		/// </summary>
		private DeterministicRNG npcRNG;

		/// <summary>
		/// Exposes the seeded RNG for deterministic AI decisions.
		/// All AI subsystems should use this instead of <see cref="DeterministicRNG.Shared"/>
		/// so that behaviour is reproducible given the same seed.
		/// </summary>
		public DeterministicRNG RNG => npcRNG;

		/// <summary>
		/// The seed used for RNG, synchronized over the network.
		/// </summary>
		private int npcSeed = 0;

		/// <summary>
		/// If true, this NPC can be charmed by players.
		/// </summary>
		public bool IsCharmable;

		/// <summary>
		/// Database of attribute bonuses for this NPC.
		/// </summary>
		public NPCAttributeDatabase AttributeBonuses;

		/// <summary>
		/// Ability templates this NPC can use. Populated in the inspector.
		/// Each template is learned as an <see cref="Ability"/> instance during
		/// <see cref="OnStartServer"/>, before clients receive the spawn payload.
		/// </summary>
		[Header("Abilities")]
		[Tooltip("Ability templates this NPC knows. Learned on server start.")]
		public List<AbilityTemplate> Abilities = new List<AbilityTemplate>();

		/// <summary>
		/// Reference to the spawner that created this NPC.
		/// </summary>
		[ShowReadonly]
		public ObjectSpawner ObjectSpawner { get; set; }

		/// <summary>
		/// Settings used when spawning this NPC.
		/// </summary>
		[ShowReadonly]
		public SpawnableSettings SpawnableSettings { get; set; }

		/// <summary>
		/// Called when the NPC is awakened. Handles name cleanup and registration.
		/// </summary>
		public override void OnAwake()
		{
			base.OnAwake();

			// Set the loaded flag to allow controllers to check if the NPC is fully loaded and in the world. 
			// This is important for proper attribute clamping and preventing actions before the NPC is fully initialized.
			EnableFlags(CharacterFlags.IsLoaded);

#if !UNITY_SERVER
			// Remove (Clone) from the GameObject name for clarity in the editor.
			GameObject.name = GameObject.name.Replace("(Clone)", "");
			if (CharacterNameLabel != null)
			{
				CharacterNameLabel.text = GameObject.name;
			}
		}
#else
			// Ensure the NPC is initialized on the server.

			// Register this NPC in the scene object registry on the server.
			SceneObject.Register(this);

			// If the RNG hasn't been instantiated, create it and generate a seed on the server.
			if (npcRNG == null)
			{
				npcSeed = npcSeedGenerator.Next();
				npcRNG = new DeterministicRNG(npcSeed);
			}
		}

		/// <summary>
		/// Called when the server starts for this NPC. Applies attribute bonuses after any spawner overrides have been injected.
		/// </summary>
		public override void OnStartServer()
		{
			base.OnStartServer();

			AddNPCAttributes(true);
			LearnNPCAbilities();
		}
#endif

		/// <summary>
		/// Called when the NPC is destroyed. Unregisters from the scene object registry.
		/// </summary>
		public override void OnDestroying()
		{
			SceneObject.Unregister(this);
		}

		/// <summary>
		/// Resets the NPC's state for object pool reuse. Clears RNG, spawner references, and client-side tracking.
		/// </summary>
		/// <param name="asServer">Whether the reset is performed on the server.</param>
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);

#if !UNITY_SERVER
			ClientCharacters.Remove(ID);
#endif

			npcRNG = null;
			npcSeed = 0;
			ObjectSpawner = null;
			SpawnableSettings = null;
		}

		/// <summary>
		/// Reads the NPC's payload from the network, including ID and attribute seed. Applies attributes and sets up model.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="reader">The network reader.</param>
		public override void ReadPayload(NetworkConnection connection, Reader reader)
		{
			ID = reader.ReadInt64();
			SceneObject.Register(this, true);

			// Read the attribute seed for deterministic attribute generation.
			npcSeed = reader.ReadInt32();

			// Instantiate the client side NPC RNG with the received seed.
			npcRNG = new DeterministicRNG(npcSeed);

			//Log.Debug($"Received NPC RNG Seed {npcSeed}");

			// Clients still need to generate the attribute modifier values locally.
			AddNPCAttributes(false);

#if !UNITY_SERVER
			ClientCharacters[ID] = this;

			// FactionController stores a reference to the RaceTemplate.
			if (this.TryGet(out IFactionController factionController))
			{
				RaceTemplate raceTemplate = factionController.RaceTemplate;
				int modelIndex = -1;
				if (raceTemplate.Models != null && raceTemplate.Models.Count > 0)
				{
					// Pick a random model for this NPC using the RNG.
					modelIndex = npcRNG.Next(0, raceTemplate.Models.Count);

					InstantiateRaceModelFromIndex(raceTemplate, modelIndex);
				}
			}
#endif
		}

		/// <summary>
		/// Writes the NPC's payload to the network, including ID and attribute seed. Ensures deterministic attribute generation on clients.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="writer">The network writer.</param>
		public override void WritePayload(NetworkConnection connection, Writer writer)
		{
			writer.WriteInt64(ID);

			// Write the seed for clients to use for determinism.
			writer.WriteInt32(npcSeed);

			//Log.Debug($"Writing NPC RNG Seed {npcSeed}");
		}

		/// <summary>
		/// Despawns this NPC using the assigned ObjectSpawner.
		/// </summary>
		public void Despawn()
		{
			DisableFlags(CharacterFlags.IsLoaded);

			ObjectSpawner?.Despawn(this);
		}

#if UNITY_SERVER
		/// <summary>
		/// Creates <see cref="Ability"/> instances from the inspector-configured
		/// <see cref="Abilities"/> list and teaches them to the NPC's <see cref="AbilityController"/>.
		/// Uses the template's <see cref="AbilityTemplate.ID"/> as the ability instance ID so that
		/// cooldown tracking, activation, and network serialization all work correctly.
		/// Called during <see cref="OnStartServer"/> before <c>WritePayload</c> broadcasts to clients.
		/// </summary>
		private void LearnNPCAbilities()
		{
			if (Abilities == null || Abilities.Count < 1)
			{
				return;
			}
			if (!this.TryGet(out IAbilityController abilityController))
			{
				return;
			}

			for (int i = 0; i < Abilities.Count; i++)
			{
				AbilityTemplate template = Abilities[i];
				if (template == null)
				{
					continue;
				}

				// Use the template ID as the ability instance ID.
				// NPCs don't craft abilities so there's no DB-assigned ID.
				Ability ability = new Ability((long)template.ID, template);
				abilityController.LearnAbility(ability);
			}
		}
#endif

		/// <summary>
		/// Applies attribute bonuses to this NPC using the attribute database and random generator.
		/// </summary>
		private void AddNPCAttributes(bool asServer)
		{
			if (npcRNG == null ||
				AttributeBonuses == null ||
				AttributeBonuses.Attributes == null ||
				!this.TryGet(out ICharacterAttributeController attributeController))
			{
				return;
			}

			foreach (NPCAttribute attribute in AttributeBonuses.Attributes)
			{
				int value;
				if (attribute.IsRandom)
				{
					value = npcRNG.Next(attribute.Min, attribute.Max);
				}
				else
				{
					value = attribute.Max;
				}

				if (attributeController.TryGetAttribute(attribute.Template, out CharacterAttribute characterAttribute))
				{
					int old = characterAttribute.Value;

					if (attribute.IsScalar)
					{
						int newValue = characterAttribute.Value.GetPercentOf(value);
						characterAttribute.SetModifier(newValue - old);
					}
					else
					{
						characterAttribute.SetModifier(value - old);
					}
				}
				else if (attributeController.TryGetResourceAttribute(attribute.Template, out CharacterResourceAttribute characterResourceAttribute))
				{
					int old = characterResourceAttribute.Value;

					if (attribute.IsScalar)
					{
						int newValue = characterResourceAttribute.Value.GetPercentOf(value);
						int modifier = newValue - old;

						characterResourceAttribute.SetModifier(modifier);
						if (asServer)
						{
							characterResourceAttribute.SetCurrentValue(newValue);
						}
					}
					else
					{
						int modifier = value - old;

						characterResourceAttribute.SetModifier(modifier);
						if (asServer)
						{
							characterResourceAttribute.SetCurrentValue(value);
						}
					}
				}
			}
		}
	}
}