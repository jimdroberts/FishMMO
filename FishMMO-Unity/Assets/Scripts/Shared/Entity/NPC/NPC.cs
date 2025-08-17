using FishNet.Object;
using UnityEngine;
using FishNet.Component.Transforming;
using FishNet.Observing;
using FishNet.Connection;
using FishNet.Serializing;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents a non-player character (NPC) in the game. Handles attribute generation, network payloads, and spawning logic.
	/// </summary>
	[RequireComponent(typeof(AIController))]
	[RequireComponent(typeof(BuffController))]
	[RequireComponent(typeof(CharacterAttributeController))]
	[RequireComponent(typeof(CharacterDamageController))]
	[RequireComponent(typeof(FactionController))]
	[RequireComponent(typeof(NetworkObject))]
	[RequireComponent(typeof(NetworkTransform))]
	[RequireComponent(typeof(NetworkObserver))]
	public class NPC : BaseCharacter, ISceneObject, ISpawnable
	{
		/// <summary>
		/// Static random number generator for NPC attribute seed generation.
		/// </summary>
		private static System.Random npcSeedGenerator = new System.Random();

		/// <summary>
		/// Random number generator for this NPC, seeded for deterministic results.
		/// </summary>
		private System.Random npcRNG;

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

#if !UNITY_SERVER
			// Remove (Clone) from the GameObject name for clarity in the editor.
			GameObject.name = GameObject.name.Replace("(Clone)", "");
			if (CharacterNameLabel != null)
			{
				CharacterNameLabel.text = GameObject.name;
			}
		}
#else
			// Register this NPC in the scene object registry on the server.
			SceneObject.Register(this);

			// If the RNG hasn't been instantiated, create it and generate a seed on the server.
			if (npcRNG == null)
			{
				npcSeed = npcSeedGenerator.Next();
				npcRNG = new System.Random(npcSeed);

				// Generate NPC attributes based on the seed on the server.
				// Clients will receive the current NPC Attributes when CharacterAttributeController is synchronized.
				AddNPCAttributes();
			}
		}
#endif

		/// <summary>
		/// Called when the NPC is destroyed. Unregisters from the scene object registry.
		/// </summary>
		void OnDestroy()
		{
			SceneObject.Unregister(this);
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
			npcRNG = new System.Random(npcSeed);

			//Log.Debug($"Received NPC RNG Seed {npcSeed}");

#if !UNITY_SERVER
			// FactionController stores a reference to the RaceTemplate.
			if (this.TryGet(out IFactionController factionController))
			{
				RaceTemplate raceTemplate = factionController.RaceTemplate;
				int modelIndex = -1;
				if (raceTemplate.Models == null || raceTemplate.Models.Count < 1)
				{
					// Pick a random model for this NPC using the RNG.
					modelIndex = npcRNG.Next(0, raceTemplate.Models.Count);
				}
				InstantiateRaceModelFromIndex(raceTemplate, modelIndex);
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
			ObjectSpawner?.Despawn(this);
		}

#if UNITY_SERVER
		/// <summary>
		/// Applies attribute bonuses to this NPC using the attribute database and random generator.
		/// </summary>
		private void AddNPCAttributes()
		{
			if (AttributeBonuses != null &&
				AttributeBonuses.Attributes != null &&
				this.TryGet(out ICharacterAttributeController attributeController))
			{
				foreach (NPCAttribute attribute in AttributeBonuses.Attributes)
				{
					int value;
					// Determine the attribute value, randomizing if specified.
					if (attribute.IsRandom)
					{
						value = npcRNG.Next(attribute.Min, attribute.Max);
					}
					else
					{
						value = attribute.Max;
					}

					// Apply the value to the appropriate attribute type.
					if (attributeController.TryGetAttribute(attribute.Template, out CharacterAttribute characterAttribute))
					{
						int old = characterAttribute.Value;

						if (attribute.IsScalar)
						{
							// Scale the value as a percent of the current attribute value.
							int newValue = characterAttribute.Value.GetPercentOf(value);
							characterAttribute.SetModifier(newValue - old);

							//Log.Debug($"{characterAttribute.Template.Name} Old: {old} | New: {characterAttribute.FinalValue}");
						}
						else
						{
							characterAttribute.SetModifier(value - old);

							//Log.Debug($"{characterAttribute.Template.Name} Old: {old} | New: {characterAttribute.FinalValue}");
						}
					}
					else if (attributeController.TryGetResourceAttribute(attribute.Template, out CharacterResourceAttribute characterResourceAttribute))
					{
						int old = characterResourceAttribute.Value;

						if (attribute.IsScalar)
						{
							// Scale the value as a percent of the current resource value.
							int newValue = characterResourceAttribute.Value.GetPercentOf(value);
							int modifier = newValue - old;

							characterResourceAttribute.SetModifier(modifier);
							characterResourceAttribute.SetCurrentValue(newValue);

							//Log.Debug($"{characterResourceAttribute.Template.Name} Old: {old} | New: {characterResourceAttribute.CurrentValue}/{characterResourceAttribute.FinalValue}");
						}
						else
						{
							int modifier = value - old;

							characterResourceAttribute.SetModifier(modifier);
							characterResourceAttribute.SetCurrentValue(value);

							//Log.Debug($"{characterResourceAttribute.Template.Name} Old: {old} | New: {characterResourceAttribute.CurrentValue}/{characterResourceAttribute.FinalValue}");
						}
					}
				}
			}
		}
#endif
	}
}