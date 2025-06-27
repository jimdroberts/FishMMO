using FishNet.Object;
using UnityEngine;
using FishNet.Component.Transforming;
using FishNet.Observing;
using FishNet.Connection;
using FishNet.Serializing;

namespace FishMMO.Shared
{
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
		private static System.Random npcSeedGenerator = new System.Random();

		private System.Random npcAttributeGenerator;
		private int npcAttributeSeed = 0;

		public bool IsCharmable;
		public NPCAttributeDatabase AttributeBonuses;

		[ShowReadonly]
		public ObjectSpawner ObjectSpawner { get; set; }
		[ShowReadonly]
		public SpawnableSettings SpawnableSettings { get; set; }

		public override void OnAwake()
		{
			base.OnAwake();

#if !UNITY_SERVER
			GameObject.name = GameObject.name.Replace("(Clone)", "");
			if (CharacterNameLabel != null)
			{
				CharacterNameLabel.text = GameObject.name;
			}
		}
#else
			SceneObject.Register(this);
		}
#endif

		void OnDestroy()
		{
			SceneObject.Unregister(this);
		}

		public override void ReadPayload(NetworkConnection connection, Reader reader)
		{
			ID = reader.ReadInt64();
			SceneObject.Register(this, true);

			// Read the AbilitySeedGenerator seed
			npcAttributeSeed = reader.ReadInt32();

			// Instantiate the AbilitySeedGenerator
			npcAttributeGenerator = new System.Random(npcAttributeSeed);

			//Log.Debug($"Received NPCAttributeGenerator Seed {npcAttributeSeed}");

			AddNPCAttributes();

#if !UNITY_SERVER
			// FactionController stores a reference to the RaceTemplate.
			if (this.TryGet(out IFactionController factionController))
			{
				RaceTemplate raceTemplate = factionController.RaceTemplate;
				int modelIndex = -1;
				if (raceTemplate.Models == null || raceTemplate.Models.Count < 1)
				{
					// Pick a random model for this NPC
					modelIndex = npcAttributeGenerator.Next(0, raceTemplate.Models.Count);
				}
				InstantiateRaceModelFromIndex(raceTemplate, modelIndex);
			}
#endif
		}

		public override void WritePayload(NetworkConnection connection, Writer writer)
		{
			writer.WriteInt64(ID);

			if (base.IsServerStarted)
			{
				// Check if we already instantiated an RNG for this npc
				if (npcAttributeGenerator == null)
				{
					// Generate an NPCSeedGenerator Seed
					npcAttributeSeed = npcSeedGenerator.Next();

					// Instantiate the NPCAttributeSeedGenerator on the server
					npcAttributeGenerator = new System.Random(npcAttributeSeed);

					AddNPCAttributes();
				}
			}

			// Write the npc RNG seed for the clients
			writer.WriteInt32(npcAttributeSeed);

			//Log.Debug($"Writing NPCAttributeGenerator Seed {npcAttributeSeed}");
		}

		public void Despawn()
		{
			ObjectSpawner?.Despawn(this);
		}

		private void AddNPCAttributes()
		{
			if (AttributeBonuses != null &&
				AttributeBonuses.Attributes != null &&
				this.TryGet(out ICharacterAttributeController attributeController))
			{
				foreach (NPCAttribute attribute in AttributeBonuses.Attributes)
				{
					int value;
					if (attribute.IsRandom)
					{
						value = npcAttributeGenerator.Next(attribute.Min, attribute.Max);
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
	}
}