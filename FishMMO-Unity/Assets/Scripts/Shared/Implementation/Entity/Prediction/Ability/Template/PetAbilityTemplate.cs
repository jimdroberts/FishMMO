using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// ScriptableObject template for defining a pet ability, including prefab and spawn parameters.
	/// </summary>
	[CreateAssetMenu(fileName = "New Pet Ability", menuName = "FishMMO/Character/Ability/Pet Ability", order = 1)]
	public class PetAbilityTemplate : AbilityTemplate
	{
		/// <summary>
		/// The prefab for the pet to be spawned.
		/// </summary>
		public NetworkObject PetPrefab;

		/// <summary>
		/// Abilities the summoned pet starts knowing.
		/// </summary>
		/// <remarks>
		/// These are taught to the pet's <see cref="FishMMO.Shared.Core.IAbilityController"/> on
		/// summon and persisted with it, so the pet keeps them across a log out. The pet prefab's
		/// own <see cref="NPC.Abilities"/> list is learned as well; use that for abilities every
		/// pet of this species has, and this list for abilities granted by the summoning spell.
		/// </remarks>
		[Tooltip("Abilities the summoned pet starts with. Learned on summon and persisted.")]
		public List<AbilityTemplate> PetAbilities = new List<AbilityTemplate>();

		/// <summary>
		/// Summon effects this spell ships with, as a crafter would attach them.
		/// </summary>
		/// <remarks>
		/// The summon counterpart of the base template's On Hit / On Spawn lists, which a summon
		/// never runs (<see cref="AbilityEvent.IsCompatibleWith"/>). Bundled effects run on every
		/// summon by this spell, and their <see cref="AbilityOnSummonEvent.PetAbilities"/> join
		/// <see cref="PetAbilities"/>.
		/// </remarks>
		[Header("Summon Events")]
		[Tooltip("Summon effects built into this spell. They run on the server when the pet arrives, and the abilities they teach are learned by the pet.")]
		public List<AbilityOnSummonEvent> OnSummonEvents = new List<AbilityOnSummonEvent>();

		/// <summary>
		/// The bounding box for spawning the pet.
		/// </summary>
		public Vector3 SpawnBoundingBox;

		/// <summary>
		/// The distance from the spawner to spawn the pet.
		/// </summary>
		public float SpawnDistance;

		/// <summary>
		/// Whether a pet can be taught <paramref name="template"/>.
		/// </summary>
		/// <remarks>
		/// A summon spell cannot: only a player's cast reaches the pet system, so a pet casting
		/// one would spend its resources for nothing.
		/// </remarks>
		/// <param name="template">The ability to teach.</param>
		/// <returns>True when the ability can be taught to a pet.</returns>
		public static bool CanBeTaughtToPet(AbilityTemplate template)
		{
			return template != null && !(template is PetAbilityTemplate);
		}

		/// <summary>
		/// The ability template IDs a pet summoned by this spell is taught.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The persisted list first (a pet restored at login keeps everything it was saved with,
		/// including abilities a crafted effect granted), then this spell's own
		/// <see cref="PetAbilities"/>, then what each summon effect teaches. Duplicates and
		/// abilities a pet cannot be taught are skipped.
		/// </para>
		/// <para>
		/// The summon effects come from <paramref name="summoningAbility"/> when there is one,
		/// because that is the ability as the player crafted it: it holds this template's bundled
		/// effects and the crafted ones. A restore has no cast, so it reads the bundled effects
		/// from the template; the crafted grants it needs are already in the persisted list.
		/// </para>
		/// </remarks>
		/// <param name="summoningAbility">The cast that summoned the pet, or null on a restore.</param>
		/// <param name="persisted">Ability template IDs restored from the database, or null on a fresh summon.</param>
		/// <returns>A new list of ability template IDs, never null.</returns>
		public List<int> BuildPetAbilityIDs(Ability summoningAbility, List<int> persisted)
		{
			List<int> result = persisted != null ? new List<int>(persisted) : new List<int>();

			AppendTeachableIDs(PetAbilities, result);

			if (summoningAbility != null)
			{
				foreach (AbilityOnSummonEvent summonEvent in summoningAbility.OnSummonEvents.Values)
				{
					AppendTeachableIDs(summonEvent.PetAbilities, result);
				}
			}
			else if (OnSummonEvents != null)
			{
				for (int i = 0; i < OnSummonEvents.Count; ++i)
				{
					AbilityOnSummonEvent summonEvent = OnSummonEvents[i];
					if (summonEvent != null)
					{
						AppendTeachableIDs(summonEvent.PetAbilities, result);
					}
				}
			}

			return result;
		}

		/// <summary>
		/// Appends one "your pet learns" line per teachable ability, skipping lines already present.
		/// </summary>
		/// <param name="templates">The abilities taught.</param>
		/// <param name="into">The list to append to.</param>
		public static void AppendPetAbilityDescriptions(List<AbilityTemplate> templates, List<string> into)
		{
			if (templates == null || into == null)
			{
				return;
			}
			for (int i = 0; i < templates.Count; ++i)
			{
				AbilityTemplate template = templates[i];
				if (!CanBeTaughtToPet(template))
				{
					continue;
				}
				string line = $"Your pet learns {template.Name}";
				if (!into.Contains(line))
				{
					into.Add(line);
				}
			}
		}

		/// <summary>Appends the IDs of every teachable ability not already in the list.</summary>
		private static void AppendTeachableIDs(List<AbilityTemplate> templates, List<int> into)
		{
			if (templates == null)
			{
				return;
			}
			for (int i = 0; i < templates.Count; ++i)
			{
				AbilityTemplate template = templates[i];
				if (CanBeTaughtToPet(template) && !into.Contains(template.ID))
				{
					into.Add(template.ID);
				}
			}
		}

		/// <summary>
		/// Called when the pet ability is loaded. Calculates spawn parameters based on the prefab's collider.
		/// </summary>
		public override void OnLoad(string typeName, string resourceName, int resourceID)
		{
			base.OnLoad(typeName, resourceName, resourceID);

			if (PetPrefab == null)
			{
				return;
			}

			// get the collider height
			Collider collider = PetPrefab.GetComponent<Collider>();
			switch (collider)
			{
				case CapsuleCollider capsule:
					SpawnDistance = capsule.radius * 2;
					SpawnBoundingBox.y = capsule.height;
					break;
				case BoxCollider box:
					SpawnDistance = box.bounds.extents.magnitude;
					SpawnBoundingBox.y = box.bounds.extents.y;
					break;
				case SphereCollider sphere:
					SpawnDistance = sphere.radius * 2;
					SpawnBoundingBox.y = sphere.radius;
					break;
				default: break;
			}
		}

		/// <summary>
		/// Flags authoring the server would ignore: effects a summon never runs, and grants a pet
		/// cannot be taught.
		/// </summary>
		protected override void OnValidate()
		{
			base.OnValidate();

#if UNITY_EDITOR
			WarnIncompatible(OnTickEvents);
			WarnIncompatible(OnHitEvents);
			WarnIncompatible(OnPreSpawnEvents);
			WarnIncompatible(OnSpawnEvents);
			WarnIncompatible(OnDestroyEvents);

			if (PetAbilities != null)
			{
				for (int i = 0; i < PetAbilities.Count; ++i)
				{
					if (PetAbilities[i] is PetAbilityTemplate summon)
					{
						Debug.LogWarning($"[PetAbilityTemplate] '{name}': '{summon.name}' is a summon spell; pets cannot summon, so it will not be taught.", this);
					}
				}
			}
#endif
		}

#if UNITY_EDITOR
		/// <summary>Warns once per bundled effect a summon spell will never run.</summary>
		private void WarnIncompatible<T>(List<T> events) where T : AbilityEvent
		{
			if (events == null)
			{
				return;
			}
			for (int i = 0; i < events.Count; ++i)
			{
				T abilityEvent = events[i];
				if (abilityEvent != null && !abilityEvent.IsCompatibleWith(this))
				{
					Debug.LogWarning($"[PetAbilityTemplate] '{name}': '{abilityEvent.name}' does nothing on a summon spell and is ignored. Use an On Summon event.", this);
				}
			}
		}
#endif
	}
}