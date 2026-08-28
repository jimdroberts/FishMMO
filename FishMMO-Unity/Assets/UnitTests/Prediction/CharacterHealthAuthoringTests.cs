using System.Collections.Generic;
using System.IO;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Asserts that every prefab carrying a <see cref="CharacterDamageController"/> also carries a
	/// health resource attribute the controller can actually find — issue #157.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The damage controller resolves health through
	/// <c>ICharacterAttributeController.TryGetHealthAttribute</c>, which looks the attribute up by
	/// <see cref="CharacterAttributeController.HealthResourceTemplateID"/>. That field is authored
	/// per prefab and defaults to zero, and zero is not the ID of anything — so a prefab that was
	/// given a damage controller but never given a health template resolves nothing, reports
	/// <c>IsAlive</c> as false forever, and cannot be damaged, killed or healed.
	/// </para>
	/// <para>
	/// That is a content fault the compiler cannot see, and it is expensive: <c>IsAlive</c> is read
	/// per tick from AI target selection, inventory checks and input handling, so the three
	/// interactable NPCs that shipped this way (banker, general merchant, ability crafter) drove
	/// roughly 28 error lines a second and wrote 153 MB of scene-server log in five and a half
	/// minutes. <see cref="MissingHealthResourceTests"/> covers the logging half — the report is
	/// now made once per object rather than once per access. This covers the other half, so the
	/// condition being reported does not come back.
	/// </para>
	/// <para>
	/// The database is checked rather than a running controller because
	/// <c>InitializeOnce</c> builds the attribute dictionaries straight from
	/// <see cref="CharacterAttributeController.CharacterAttributeDatabase"/>: an entry in that list
	/// whose <c>IsResourceAttribute</c> is set and whose ID matches the field is exactly what
	/// <c>TryGetHealthAttribute</c> will find at runtime.
	/// </para>
	/// <para>
	/// EditMode only — it uses <see cref="AssetDatabase"/> to load the shipped prefabs.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class CharacterHealthAuthoringTests
	{
		/// <summary>Folder holding the shipped prefabs.</summary>
		private const string PREFAB_FOLDER = "Assets/Prefabs";

		/// <summary>
		/// Gives every attribute template the ID production would give it.
		/// </summary>
		/// <remarks>
		/// <c>CharacterAttributeTemplate.ID</c> is assigned by <c>AddToCache</c>, not serialized, so
		/// an asset loaded straight off disk reports ID zero — the same value an unauthored
		/// <c>HealthResourceTemplateID</c> holds, which would make every prefab look correctly
		/// configured. Registering them here reproduces
		/// <c>(typeName + assetName).GetDeterministicHashCode()</c>, the same value a running game
		/// computes.
		/// </remarks>
		[OneTimeSetUp]
		public void RegisterAttributeTemplateIds()
		{
			foreach (string guid in AssetDatabase.FindAssets("t:CharacterAttributeTemplate"))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				CharacterAttributeTemplate attribute = AssetDatabase.LoadAssetAtPath<CharacterAttributeTemplate>(path);
				if (attribute != null)
				{
					attribute.AddToCache(attribute.name);
				}
			}
		}

		/// <summary>
		/// Every prefab under <see cref="PREFAB_FOLDER"/>, loaded once.
		/// </summary>
		private static IEnumerable<GameObject> Prefabs()
		{
			foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PREFAB_FOLDER }))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
				if (prefab != null)
				{
					yield return prefab;
				}
			}
		}

		/// <summary>
		/// Returns true when the database holds a resource attribute whose ID is the one the
		/// controller will ask for.
		/// </summary>
		/// <param name="controller">The attribute controller authored on the prefab.</param>
		private static bool ResolvesHealth(CharacterAttributeController controller)
		{
			if (controller.CharacterAttributeDatabase == null)
			{
				return false;
			}

			foreach (CharacterAttributeTemplate attribute in controller.CharacterAttributeDatabase.Attributes)
			{
				if (attribute != null &&
					attribute.IsResourceAttribute &&
					attribute.ID == controller.HealthResourceTemplateID)
				{
					return true;
				}
			}
			return false;
		}

		[Test]
		public void EveryPrefabWithADamageController_ResolvesAHealthResourceAttribute()
		{
			int damageControllers = 0;
			List<string> offenders = new List<string>();

			foreach (GameObject prefab in Prefabs())
			{
				// Inactive children included: a disabled branch still spawns with the prefab.
				foreach (CharacterDamageController damage in prefab.GetComponentsInChildren<CharacterDamageController>(true))
				{
					damageControllers++;

					/* Same GameObject, not the prefab root: Character.TryGet resolves behaviours
					 * registered by one character, and the pairing the damage controller depends on
					 * is the attribute controller sitting beside it. */
					CharacterAttributeController attributes = damage.GetComponent<CharacterAttributeController>();
					if (attributes == null)
					{
						offenders.Add($"{prefab.name} (no CharacterAttributeController beside the damage controller)");
						continue;
					}
					if (attributes.CharacterAttributeDatabase == null)
					{
						offenders.Add($"{prefab.name} (no attribute database, so it spawns with no attributes at all)");
						continue;
					}
					if (!ResolvesHealth(attributes))
					{
						offenders.Add($"{prefab.name} (HealthResourceTemplateID {attributes.HealthResourceTemplateID} " +
							$"matches no resource attribute in '{attributes.CharacterAttributeDatabase.name}')");
					}
				}
			}

			TestContext.WriteLine($"MEASURE CharacterDamageController components scanned across prefabs: {damageControllers}");
			LogAssert.IsTrue(damageControllers > 0,
				$"No CharacterDamageController was found under {PREFAB_FOLDER}; this guard is checking nothing.");
			LogAssert.AreEqual(0, offenders.Count,
				"These prefabs can take damage but have no health to lose, so IsAlive is false forever and " +
				"the missing-health report fires on every spawn (issue #157): " + string.Join(", ", offenders));
		}

		[Test]
		public void TheInteractableNpcs_FromIssue157_AreConfigured()
		{
			/* Named explicitly because these three are what produced the 153 MB log: a generic
			 * sweep would still pass if someone re-authored them back to zero while adding a new
			 * prefab that happens to be correct. */
			string[] paths =
			{
				"Assets/Prefabs/Shared/Entity/NPCs/Interactables/Human/Banker/HumanBanker.prefab",
				"Assets/Prefabs/Shared/Entity/NPCs/Interactables/Human/Merchants/GeneralGoods/HumanGeneralMerchant.prefab",
				"Assets/Prefabs/Shared/Entity/NPCs/Interactables/Human/AbilityCrafter/HumanAbilityCrafter.prefab",
			};

			foreach (string path in paths)
			{
				LogAssert.IsTrue(File.Exists(path), $"'{path}' is missing; the guard cannot check it.");

				GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
				LogAssert.IsNotNull(prefab, $"'{path}' did not load as a prefab.");

				CharacterAttributeController attributes = prefab.GetComponentInChildren<CharacterAttributeController>(true);
				LogAssert.IsNotNull(attributes, $"'{prefab.name}' has no CharacterAttributeController.");
				LogAssert.IsTrue(ResolvesHealth(attributes),
					$"'{prefab.name}' does not resolve a health resource attribute.");
			}
		}
	}
}
