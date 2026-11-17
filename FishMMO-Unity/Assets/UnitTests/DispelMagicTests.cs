using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs for Dispel Magic: one targeted spell that strips an enemy's buffs or lifts a friend's
	/// debuffs, sold by General Goods.
	/// </summary>
	[TestFixture]
	public class DispelMagicTests
	{
		private const string DispelMagic = "Assets/Templates/Entity/Abilities/Types/Dispel/Dispel Magic.asset";
		private const string Purge = "Assets/Templates/Entity/Abilities/Events/Hit/Dispel/Dispel Magic Purge.asset";
		private const string Cleanse = "Assets/Templates/Entity/Abilities/Events/Hit/Dispel/Dispel Magic Cleanse.asset";
		private const string SelfDispelMagic = "Assets/Templates/Entity/Abilities/Types/Dispel/Self Dispel Magic.asset";
		private const string SelfDispelEvent = "Assets/Templates/Entity/Abilities/Events/Hit/Dispel/Self Dispel Magic Event.asset";
		private const string DispelBurst = "Assets/Prefabs/Client/FX/Abilities/Dispel Burst.prefab";
		private const string GeneralGoods = "Assets/Templates/Entity/Interactables/NPCs/Human/Merchants/GeneralGoods/GeneralGoods.asset";

		private static T Load<T>(string path) where T : Object
		{
			T asset = AssetDatabase.LoadAssetAtPath<T>(ProjectFiles.Path(path));
			Assert.IsNotNull(asset, $"{path} did not load.");
			return asset;
		}

		private static ApplyDispelAction OnlyDispel(AbilityOnHitEvent hit)
		{
			ApplyDispelAction found = null;
			foreach (BaseAction action in hit.OnConditionsMetActions)
			{
				if (action is ApplyDispelAction dispel)
				{
					Assert.IsNull(found, $"{hit.name} dispels twice");
					found = dispel;
				}
			}
			Assert.IsNotNull(found, $"{hit.name} must dispel");
			return found;
		}

		/// <summary>The dispel's own burst, played by the event.</summary>
		private static PlayFXAction BurstOf(AbilityOnHitEvent hit)
		{
			foreach (BaseAction action in hit.OnConditionsMetActions)
			{
				if (action is PlayFXAction fx)
				{
					return fx;
				}
			}
			Assert.Fail($"{hit.name} plays no effect, so a dispel would be invisible");
			return null;
		}

		private static TargetAllianceCondition AllianceGate(AbilityOnHitEvent hit)
		{
			TargetedEntitySelector selector = hit.TargetSelector as TargetedEntitySelector;
			Assert.IsNotNull(selector, $"{hit.name} acts on the caster's current target");
			foreach (BaseCondition condition in selector.Conditions)
			{
				if (condition is TargetAllianceCondition alliance)
				{
					return alliance;
				}
			}
			Assert.Fail($"{hit.name} has no alliance gate, so it would apply to friend and foe alike");
			return null;
		}

		[Test]
		public void OnAnEnemy_ItStripsBuffs_AndOnAFriend_ItLiftsDebuffs()
		{
			AbilityTemplate ability = Load<AbilityTemplate>(DispelMagic);
			AbilityOnHitEvent purge = Load<AbilityOnHitEvent>(Purge);
			AbilityOnHitEvent cleanse = Load<AbilityOnHitEvent>(Cleanse);

			CollectionAssert.AreEquivalent(new[] { purge, cleanse }, ability.OnHitEvents);
			Assert.IsTrue(ability.RequiresTarget, "it is cast at someone");

			ApplyDispelAction strip = OnlyDispel(purge);
			Assert.IsTrue(strip.IncludeBuffs && !strip.IncludeDebuffs, "an enemy loses buffs, never debuffs");
			// Neutral passes the gate on purpose (wildlife reads neutral); the harm rule refuses a neutral player.
			TargetAllianceCondition enemyGate = AllianceGate(purge);
			Assert.IsTrue(enemyGate.ApplyToEnemy && !enemyGate.ApplyToAllies && !enemyGate.ApplyToSelf);

			ApplyDispelAction lift = OnlyDispel(cleanse);
			Assert.IsTrue(lift.IncludeDebuffs && !lift.IncludeBuffs, "a friend loses debuffs, never buffs");
			TargetAllianceCondition friendGate = AllianceGate(cleanse);
			// Not the heals' rule any neutral character is cleansed, NPCs, pets and wildlife included; never an enemy.
			Assert.IsTrue(friendGate.ApplyToAllies && friendGate.ApplyToSelf && !friendGate.ApplyToEnemy && friendGate.ApplyToNeutral
				&& !friendGate.NeutralPlayersOnly);
		}

		[Test]
		public void SelfDispelMagic_LiftsYourOwnDebuffs_WithNoTarget()
		{
			AbilityTemplate ability = Load<AbilityTemplate>(SelfDispelMagic);
			AbilityOnHitEvent hit = Load<AbilityOnHitEvent>(SelfDispelEvent);

			CollectionAssert.AreEqual(new[] { hit }, ability.OnHitEvents);
			Assert.AreEqual(AbilitySpawnTarget.Self, ability.AbilitySpawnTarget);
			Assert.IsFalse(ability.RequiresTarget, "it needs nobody targeted");
			Assert.IsInstanceOf<InitiatorTargetSelector>(hit.TargetSelector, "it lands on the caster");

			ApplyDispelAction lift = OnlyDispel(hit);
			Assert.IsTrue(lift.IncludeDebuffs && !lift.IncludeBuffs, "your own buffs are never stripped");
		}

		[Test]
		public void EveryDispel_PlaysTheDispelBurst_AndNothingElseDoes()
		{
			GameObject burst = Load<GameObject>(DispelBurst);
			foreach (string path in new[] { Purge, Cleanse, SelfDispelEvent })
			{
				Assert.AreSame(burst, BurstOf(Load<AbilityOnHitEvent>(path)).FXPrefab, path);
			}

			// Unique: no other ability effect in the project plays it.
			string guid = AssetDatabase.AssetPathToGUID(DispelBurst);
			foreach (string eventGuid in AssetDatabase.FindAssets("t:AbilityEvent"))
			{
				string path = AssetDatabase.GUIDToAssetPath(eventGuid);
				if (path.Contains("/Events/Hit/Dispel/"))
				{
					continue;
				}
				StringAssert.DoesNotContain(guid, System.IO.File.ReadAllText(ProjectFiles.Path(path)), path);
			}

			Assert.IsNotNull(AddressableAssetSettingsDefaultObject.Settings.FindAssetEntry(guid),
				"the burst is loaded by the client from addressables");
		}

		[Test]
		public void ItsRange_MatchesTheTargetSelector()
		{
			/* The client refuses a targeted cast beyond Speed x LifeTime before it is sent, and the
			 * selector refuses beyond its own range on arrival: two ranges that disagree make the
			 * nearer one the real one, silently. */
			AbilityTemplate ability = Load<AbilityTemplate>(DispelMagic);
			float range = ability.Speed * ability.LifeTime;
			foreach (string path in new[] { Purge, Cleanse })
			{
				TargetedEntitySelector selector = (TargetedEntitySelector)Load<AbilityOnHitEvent>(path).TargetSelector;
				Assert.AreEqual(selector.MaximumRange, range, 0.001f, path);
			}
		}

		[Test]
		public void ItsTooltip_SaysWhatItRemoves()
		{
			AbilitySummary summary = AbilitySummary.Compose(Load<AbilityTemplate>(DispelMagic));
			CollectionAssert.Contains(summary.Effects, "Removes up to 2 buffs");
			CollectionAssert.Contains(summary.Effects, "Removes up to 2 debuffs");
		}

		[Test]
		public void ItIsSoldByGeneralGoods_AndLoadable()
		{
			MerchantTemplate merchant = Load<MerchantTemplate>(GeneralGoods);
			CollectionAssert.Contains(merchant.Abilities, Load<AbilityTemplate>(DispelMagic));
			CollectionAssert.Contains(merchant.Abilities, Load<AbilityTemplate>(SelfDispelMagic));

			foreach (string path in new[] { DispelMagic, Purge, Cleanse, SelfDispelMagic, SelfDispelEvent })
			{
				Assert.IsNotNull(AddressableAssetSettingsDefaultObject.Settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(path)),
					$"{path} is not addressable, so it would never be loaded and the ability could not be resolved by id.");
			}
		}
	}
}
