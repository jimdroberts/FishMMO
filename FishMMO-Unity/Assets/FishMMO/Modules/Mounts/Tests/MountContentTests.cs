using System.Collections.Generic;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The built mount content (MountContentBuilder, issue #109) holds to the rules the code relies on. Ignored until the
	/// builder has run.
	/// </summary>
	[TestFixture]
	public class MountContentTests
	{
		private static T Load<T>(string path) where T : Object
		{
			T asset = AssetDatabase.LoadAssetAtPath<T>(path);
			if (asset == null)
			{
				Assert.Ignore($"{path} is not built yet (Dashboard > Mounts > Build mounts).");
			}
			return asset;
		}

		[Test]
		public void EveryMount_LastsUntilRemoved_BreaksOnAction_AndHasABody()
		{
			foreach (MountContentBuilder.MountSpec spec in MountContentBuilder.Roster)
			{
				MountBuffTemplate mount = Load<MountBuffTemplate>($"{MountContentBuilder.MountFolder}/{spec.Name}.asset");
				Assert.That(mount.BreaksOnAction, Is.True, $"{spec.Name}: casting dismounts");
				Assert.That(mount.LastsUntilRemoved, Is.True, spec.Name);
				Assert.That(mount.IsDebuff, Is.False, $"{spec.Name}: a buff the rider can click off");
				Assert.That(mount.Material, Is.Not.Null, $"{spec.Name}: drawn with the wildlife material");
				Assert.That(mount.SaddleHeight, Is.GreaterThan(0.5f), spec.Name);
			}
		}

		[Test]
		public void EverySaddle_NamesItsMount_AndSitsInTheSaddleSocket()
		{
			foreach (MountContentBuilder.MountSpec spec in MountContentBuilder.Roster)
			{
				SaddleTemplate saddle = Load<SaddleTemplate>($"{MountContentBuilder.SaddleFolder}/{spec.Saddle}.asset");
				Assert.That(saddle.Slot, Is.EqualTo(ItemSlot.MountSaddle), spec.Saddle);
				Assert.That(saddle.Mount, Is.Not.Null, spec.Saddle);
				Assert.That(saddle.Mount.name, Is.EqualTo(spec.Name), spec.Saddle);
				Assert.That(saddle.Price, Is.GreaterThan(0), spec.Saddle);
			}
		}

		[Test]
		public void TheDragonFlies_TheFrogLeaps_TheLlamaClimbs()
		{
			Assert.That(Load<MountBuffTemplate>($"{MountContentBuilder.MountFolder}/Dragon.asset").Flies, Is.True);
			MountBuffTemplate frog = Load<MountBuffTemplate>($"{MountContentBuilder.MountFolder}/Giant Frog.asset");
			Assert.That(frog.JumpHeightMultiplier, Is.GreaterThan(2f));
			Assert.That(frog.LeapSpeed, Is.GreaterThan(0f));
			Assert.That(Load<MountBuffTemplate>($"{MountContentBuilder.MountFolder}/Llama.asset").MaxSlopeDegrees, Is.GreaterThan(60f));
			Assert.That(Load<MountBuffTemplate>($"{MountContentBuilder.MountFolder}/Horse.asset").GroundSpeedMultiplier,
				Is.GreaterThan(Load<MountBuffTemplate>($"{MountContentBuilder.MountFolder}/Cow.asset").GroundSpeedMultiplier));
		}

		[Test]
		public void TheMountAbility_MountsItsCaster_AndChecksTheRules()
		{
			AbilityTemplate mount = Load<AbilityTemplate>($"{MountContentBuilder.AbilityFolder}/{MountContentBuilder.MountAbilityName}.asset");
			Assert.That(MountPressOverride.IsMountTemplate(mount), Is.True, "pressing it while mounted gets off");
			Assert.That(mount.ActivationConditions.Exists(c => c is CanMountCondition), Is.True);
			Assert.That(mount.AbilitySpawnTarget, Is.EqualTo(AbilitySpawnTarget.Self));
		}

		[Test]
		public void TheDismountEffect_KnocksAStruckFoeOff()
		{
			AbilityOnHitEvent dismount = Load<AbilityOnHitEvent>($"{MountContentBuilder.EventFolder}/{MountContentBuilder.DismountEffectName}.asset");
			Assert.That(dismount.OnConditionsMetActions.Exists(a => a is DismountAction), Is.True);
			Assert.That(dismount.TargetSelector, Is.InstanceOf<EventTargetSelector>());
		}

		[Test]
		public void TheStablemaster_SellsEverySaddle_TheMountPremade_AndDismount()
		{
			MerchantTemplate merchant = Load<MerchantTemplate>(MountContentBuilder.MerchantPath);
			Assert.That(merchant.Items.FindAll(i => i is SaddleTemplate).Count, Is.EqualTo(MountContentBuilder.Roster.Length));
			Assert.That(merchant.PremadeAbilities.Exists(p => p != null && MountPressOverride.IsMountTemplate(p.Ability)), Is.True);
			Assert.That(merchant.AbilityEvents.Exists(e => e != null && e.name == MountContentBuilder.DismountEffectName), Is.True);
			GameObject prefab = Load<GameObject>(MountContentBuilder.StablemasterPrefabPath);
			Assert.That(prefab.GetComponent<NPC>(), Is.Not.Null);
		}

		[Test]
		public void EveryMountAsset_IsAddressable()
		{
			AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
			Assume.That(settings, Is.Not.Null);
			var paths = new List<string> { MountContentBuilder.MerchantPath,
				$"{MountContentBuilder.AbilityFolder}/{MountContentBuilder.MountAbilityName}.asset",
				$"{MountContentBuilder.EventFolder}/{MountContentBuilder.DismountEffectName}.asset" };
			foreach (MountContentBuilder.MountSpec spec in MountContentBuilder.Roster)
			{
				paths.Add($"{MountContentBuilder.MountFolder}/{spec.Name}.asset");
				paths.Add($"{MountContentBuilder.SaddleFolder}/{spec.Saddle}.asset");
			}
			foreach (string path in paths)
			{
				Load<Object>(path);
				Assert.That(settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(path)), Is.Not.Null, $"{path} is not addressable: it would never load");
			}
		}
	}
}
