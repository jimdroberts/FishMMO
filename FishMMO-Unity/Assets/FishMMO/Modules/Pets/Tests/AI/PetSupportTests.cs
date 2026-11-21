using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishMMO.Server.Implementation.World.SceneServer.AI;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// Proofs for pets that heal and buff: who counts as their side, the spells a summon can teach
	/// them, and the fixed owner-first order they serve in.
	/// </summary>
	[TestFixture]
	public class PetSupportTests
	{
		private const string Scripts = "Assets/FishMMO";
		private const string SummonFireElemental = "Assets/FishMMO/Modules/Abilities/Templates/Types/Summons/Summon Fire Elemental.asset";
		private const string SummonLesserFireElemental = "Assets/FishMMO/Modules/Abilities/Templates/Types/Summons/Summon Lesser Fire Elemental.asset";
		private const string TeachLesserHeal = "Assets/FishMMO/Modules/Abilities/Templates/Events/Summon/Teach Pet Lesser Heal.asset";
		private const string TeachLesserFireball = "Assets/FishMMO/Modules/Abilities/Templates/Events/Summon/Teach Pet Lesser Fireball.asset";
		private const string LesserHeal = "Assets/FishMMO/Modules/Abilities/Templates/Types/Heal/Lesser Heal.asset";
		private const string LesserFireball = "Assets/FishMMO/Modules/Abilities/Templates/Types/Lesser Fireball.asset";
		private const string GeneralGoods = "Assets/FishMMO/Modules/Commerce/Templates/NPCs/Human/Merchants/GeneralGoods/GeneralGoods.asset";

		private readonly List<GameObject> temporaries = new List<GameObject>();

		[TearDown]
		public void TearDown()
		{
			foreach (GameObject temporary in temporaries)
			{
				if (temporary != null)
				{
					Object.DestroyImmediate(temporary);
				}
			}
			temporaries.Clear();
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
		}

		private static T Load<T>(string path) where T : Object
		{
			T asset = AssetDatabase.LoadAssetAtPath<T>(ProjectFiles.Path(path));
			Assert.IsNotNull(asset, $"{path} did not load.");
			return asset;
		}

		/// <summary>
		/// Builds a networked character by hand. Its RequireComponent chain brings a duplicate
		/// NetworkObject that FishNet logs an error about; that is an artefact of standing a prefab
		/// up in edit mode, not of anything under test.
		/// </summary>
		private T NewCharacter<T>(string name, long id) where T : BaseCharacter
		{
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
			GameObject host = new GameObject(name);
			host.SetActive(false);
			temporaries.Add(host);
			T character = host.AddComponent<T>();
			character.ID = id;
			return character;
		}

		[Test]
		public void SummonFireElemental_IsTheLesserSpellWithEffectSlots()
		{
			PetAbilityTemplate lesser = Load<PetAbilityTemplate>(SummonLesserFireElemental);
			PetAbilityTemplate summon = Load<PetAbilityTemplate>(SummonFireElemental);

			/* Its own, larger elemental since 2026-11-16 ("a fire elemental", with its own attributes and icon); the lesser
			 * spell keeps "a lesser fire elemental". */
			Assert.IsNotNull(summon.PetPrefab, "the summon names an elemental");
			Assert.IsNotNull(lesser.PetPrefab, "and so does the lesser spell");
			Assert.AreNotSame(lesser.PetPrefab, summon.PetPrefab, "the greater spell summons the greater elemental");
			Assert.AreEqual(lesser.ActivationTime, summon.ActivationTime);
			Assert.AreEqual(lesser.Cooldown, summon.Cooldown);
			Assert.GreaterOrEqual(summon.AdditionalEventSlots, 2, "room for both summon effects at once");
			Assert.AreEqual(0, lesser.AdditionalEventSlots, "the lesser spell is unchanged");
		}

		[Test]
		public void TheSummonEffects_TeachLesserHealAndLesserFireball()
		{
			PetAbilityTemplate summon = Load<PetAbilityTemplate>(SummonFireElemental);
			AbilityOnSummonEvent heal = Load<AbilityOnSummonEvent>(TeachLesserHeal);
			AbilityOnSummonEvent fireball = Load<AbilityOnSummonEvent>(TeachLesserFireball);

			CollectionAssert.AreEqual(new[] { Load<AbilityTemplate>(LesserHeal) }, heal.PetAbilities);
			CollectionAssert.AreEqual(new[] { Load<AbilityTemplate>(LesserFireball) }, fireball.PetAbilities);
			Assert.IsTrue(heal.IsCompatibleWith(summon) && fireball.IsCompatibleWith(summon));
		}

		[Test]
		public void TheNewSpellAndEffects_AreSoldAndLoadable()
		{
			MerchantTemplate merchant = Load<MerchantTemplate>(GeneralGoods);
			PetAbilityTemplate summon = Load<PetAbilityTemplate>(SummonFireElemental);
			AbilityOnSummonEvent heal = Load<AbilityOnSummonEvent>(TeachLesserHeal);
			AbilityOnSummonEvent fireball = Load<AbilityOnSummonEvent>(TeachLesserFireball);

			CollectionAssert.Contains(merchant.Abilities, summon);
			CollectionAssert.Contains(merchant.AbilityEvents, heal);
			CollectionAssert.Contains(merchant.AbilityEvents, fireball);

			// Templates are found by id at run time, from the addressables loaded at boot.
			foreach (string path in new[] { SummonFireElemental, TeachLesserHeal, TeachLesserFireball })
			{
				Assert.IsNotNull(AddressableAssetSettingsDefaultObject.Settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(path)),
					$"{path} is not addressable, so no client or server would ever load it.");
			}
		}

		[Test]
		public void LesserHealIsASupportSpell_AndLesserFireballIsNot()
		{
			// What PetSupport reads to decide a heal is a heal, and the attack rotation the opposite.
			AIAbilityIntent heal = AIAbilityClassifier.Classify(Load<AbilityTemplate>(LesserHeal));
			AIAbilityIntent fireball = AIAbilityClassifier.Classify(Load<AbilityTemplate>(LesserFireball));

			Assert.IsTrue(heal.HasAny(AIAbilityIntent.Heal) && !heal.IsOffensive(), "Lesser Heal is a heal and harms nobody");
			Assert.AreNotEqual(AbilitySpawnTarget.Self, Load<AbilityTemplate>(LesserHeal).AbilitySpawnTarget,
				"it is thrown at someone, so the pet can aim it at its owner");
			Assert.IsTrue(fireball.IsOffensive(), "Lesser Fireball belongs in the attack rotation");
		}

		[Test]
		public void EveryPetArchetype_SupportsItsOwner()
		{
			foreach (string guid in AssetDatabase.FindAssets("t:AIArchetypeTemplate"))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				if (!Path.GetFileName(path).StartsWith("Pet - "))
				{
					continue;
				}
				AIArchetypeTemplate archetype = AssetDatabase.LoadAssetAtPath<AIArchetypeTemplate>(ProjectFiles.Path(path));
				Assert.IsNotNull(archetype?.PetSupport, path);
				Assert.IsTrue(archetype.PetSupport.Enabled, $"{path}: a pet that learns a heal must use it");
				Assert.Greater(archetype.PetSupport.HealThreshold, 0f, path);
			}
		}

		[Test]
		public void TheOwnerIsServedBeforeAnyoneElse()
		{
			/* The order is the rule: owner heal, owner buff, then the pet and its friends. Pinned on
			 * the source because the decision runs against live controllers. */
			string source = ProjectFiles.ReadAllText(Path.Combine(Scripts, "Server/Implementation/World/SceneServer/AI/PetSupport.cs"));
			int choose = source.IndexOf("public static bool TryChoose(", System.StringComparison.Ordinal);
			Assert.Greater(choose, 0);
			int ownerHeal = source.IndexOf("TryPick(controller, owner, false, Kind.Heal, out choice)", choose, System.StringComparison.Ordinal);
			int ownerBuff = source.IndexOf("TryPick(controller, owner, false, Kind.Buff, out choice)", choose, System.StringComparison.Ordinal);
			int others = source.IndexOf("RefreshAllies(controller, pet, settings, fallbackLayers);", choose, System.StringComparison.Ordinal);
			Assert.IsTrue(ownerHeal > 0 && ownerHeal < ownerBuff && ownerBuff < others,
				"the owner's heal, then the owner's buffs, before anyone else is even looked for");

			string attacking = ProjectFiles.ReadAllText(Path.Combine(Scripts, "Server/Implementation/World/SceneServer/AI/States/BaseAttackingState.cs"));
			int support = attacking.IndexOf("if (TrySupportAsPet(controller, abilityController))", System.StringComparison.Ordinal);
			int pick = attacking.IndexOf("Ability chosenAbility = controller.AttackCooldownTimer > 0f ? null : PickAbility(controller);", System.StringComparison.Ordinal);
			Assert.IsTrue(support > 0 && support < pick, "in a fight the pet looks after its side before it picks an attack");

			string idle = ProjectFiles.ReadAllText(Path.Combine(Scripts, "Server/Implementation/World/SceneServer/AI/States/PetIdleState.cs"));
			StringAssert.Contains("TrySupport(controller, abilityController, pet)", idle, "and out of a fight too");
		}

		[Test]
		public void ASupportCast_IsAimedAtTheFriend_NotTheEnemy()
		{
			/* The aim followed Target alone, so a healing mote — launched along the aim — flew at the
			 * enemy however far the caster had turned to its friend. */
			string controller = ProjectFiles.ReadAllText(Path.Combine(Scripts, "Server/Implementation/World/SceneServer/AI/AIController.cs"));
			int update = controller.IndexOf("private void UpdateAim(float deltaTime)", System.StringComparison.Ordinal);
			int support = controller.IndexOf("if (SupportTarget != null && TryAimAtSupportTarget())", update, System.StringComparison.Ordinal);
			int enemy = controller.IndexOf("Vector3 targetPosition = Target.position;", update, System.StringComparison.Ordinal);
			Assert.IsTrue(update > 0 && support > update && support < enemy, "the support aim must win over the enemy aim");

			string healer = ProjectFiles.ReadAllText(Path.Combine(Scripts, "Server/Implementation/World/SceneServer/AI/States/HealerAttackingState.cs"));
			StringAssert.Contains("controller.SupportTarget = ally;", healer, "a healer's heal is aimed at the ally too");
		}
	}
}
