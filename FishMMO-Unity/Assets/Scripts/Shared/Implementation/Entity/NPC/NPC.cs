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
		[SerializeField, ShowReadonly]
		private int npcSeed = 0;

		/// <summary>
		/// Gender selected for this NPC's generated name and model set.
		/// </summary>
		[SerializeField, ShowReadonly]
		private CharacterGender npcGender = CharacterGender.Unspecified;

		/// <summary>
		/// If true, this NPC can be charmed by players.
		/// </summary>
		public bool IsCharmable;

		[Header("Corpse Decay")]
		[Tooltip("Seconds the corpse remains visible after death before returning to the object pool.")]
		public float CorpseDecayDuration = 30f;

		/// <summary>
		/// Whether the NPC is currently in corpse state (dead but still visible).
		/// </summary>
		private bool isCorpse;

		/// <summary>
		/// Remaining seconds before the corpse returns to the object pool.
		/// </summary>
		private float corpseDecayTimer;

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
		[SerializeField, ShowReadonly]
		private ObjectSpawner objectSpawner;

		/// <summary>
		/// Reference to the spawner that created this NPC.
		/// </summary>
		public ObjectSpawner ObjectSpawner
		{
			get { return objectSpawner; }
			set { objectSpawner = value; }
		}

		/// <summary>
		/// Settings used when spawning this NPC.
		/// </summary>
		[SerializeReference, ShowReadonly]
		private SpawnableSettings spawnableSettings;

		/// <summary>
		/// Settings used when spawning this NPC.
		/// </summary>
		public SpawnableSettings SpawnableSettings
		{
			get { return spawnableSettings; }
			set { spawnableSettings = value; }
		}

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
			// Register this NPC in the scene object registry on the server.
			// SceneObject registration only needs to happen once per object lifetime.
			SceneObject.Register(this);
		}

		/// <summary>
		/// Called when the server starts for this NPC. Runs on every spawn including pool reuse.
		/// Re-rolls the seed, RNG, gender, and name. Then applies attribute bonuses and learns abilities.
		/// Spawner overrides (AttributeBonuses, CorpseDecayDuration) are injected before this runs.
		/// </summary>
		public override void OnStartServer()
		{
			base.OnStartServer();

			// Re-roll seed and RNG on every spawn (pool reuse).
			// ResetState clears these when the object returns to the pool.
			npcSeed = npcSeedGenerator.Next();
			npcRNG = new DeterministicRNG(npcSeed);

			// Regenerate gender and name for model selection and display.
			SceneObjectNamer sceneObjectNamer = GetComponent<SceneObjectNamer>();
			if (sceneObjectNamer != null)
			{
				npcGender = sceneObjectNamer.EnsureGeneratedGender();
			}

			AddNPCAttributes(true);
			LearnNPCAbilities();

			// Subscribe to the server tick for corpse decay timer.
			base.TimeManager.OnTick += CorpseDecayTick;
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
			// Unsubscribe from tick to prevent stale timer during pool idle.
			if (base.TimeManager != null)
				base.TimeManager.OnTick -= CorpseDecayTick;
			isCorpse = false;
			corpseDecayTimer = 0f;

			base.ResetState(asServer);

#if !UNITY_SERVER
			ClientCharacters.Remove(ID);
#endif

			npcRNG = null;
			npcSeed = 0;
			npcGender = CharacterGender.Unspecified;
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
			npcGender = (CharacterGender)reader.ReadUInt8Unpacked();

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
				int modelCount = raceTemplate == null ? 0 : raceTemplate.GetModelCount(npcGender);
				if (modelCount > 0)
				{
					// Pick a random model for this NPC using the RNG.
					modelIndex = npcRNG.Next(0, modelCount);

					InstantiateRaceModelFromIndex(raceTemplate, modelIndex, npcGender);
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
			SceneObjectNamer sceneObjectNamer = GetComponent<SceneObjectNamer>();
			npcGender = sceneObjectNamer == null ? CharacterGender.Unspecified : sceneObjectNamer.EnsureGeneratedGender();
			writer.WriteUInt8Unpacked((byte)npcGender);

			//Log.Debug($"Writing NPC RNG Seed {npcSeed}");
		}

		/// <summary>
		/// Enters corpse state or returns to pool. On first call after death, the NPC
		/// becomes a corpse (visible, immobile, immortal) for CorpseDecayDuration seconds.
		/// After the timer expires, the object is returned to FishNet's pool for reuse.
		/// </summary>
		public void Despawn()
		{
			if (isCorpse) return;

			DisableFlags(CharacterFlags.IsLoaded);

			// Enter corpse state -- stay spawned so clients see the death animation.
			isCorpse = true;
			corpseDecayTimer = CorpseDecayDuration;

			// Disable AI so the corpse does not move or fight.
			AIController ai = GetComponent<AIController>();
			if (ai != null) ai.enabled = false;

			// Prevent the corpse from being killed again.
			if (TryGet(out ICharacterDamageController dc))
				dc.Immortal = true;
		}

		/// <summary>
		/// Returns the NPC to the object pool immediately. Called when the corpse
		/// decay timer expires or on server shutdown.
		/// </summary>
		public void ReturnToPool()
		{
			isCorpse = false;
			corpseDecayTimer = 0f;
			ObjectSpawner?.Despawn(this);
		}

#if UNITY_SERVER
		/// <summary>
		/// Called each server tick to advance the corpse decay timer.
		/// </summary>
		private void CorpseDecayTick()
		{
			if (!isCorpse) return;
			corpseDecayTimer -= (float)base.TimeManager.TickDelta;
			if (corpseDecayTimer <= 0f)
				ReturnToPool();
		}
#endif

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