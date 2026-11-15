using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishMMO.Server.Implementation.World.SceneServer.AI;
using Origin = FishMMO.Shared.KnockbackHitAction.KnockbackOrigin;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins the healing and area ability suite (TestAbilitySuiteGenerator), the attack-shaped knockback, NPC knockback,
	/// sized effects and the haste effect worn at the ankles.
	/// </summary>
	[TestFixture]
	public class HealingAndAreaAbilityTests
	{
		private const string HealAbilities = "Assets/Templates/Entity/Abilities/Types/Heal";
		private const string AreaAbilities = "Assets/Templates/Entity/Abilities/Types/Area";
		private const string FX = "Assets/Prefabs/Client/FX/Abilities";
		private const string Merchant = "Assets/Templates/Entity/Interactables/NPCs/Human/Merchants/GeneralGoods/GeneralGoods.asset";
		private const string SpeedBuff = "Assets/Templates/Entity/Buffs/Minor Increase Move Speed.asset";
		private const string TestHuman = "Assets/Prefabs/Client/Models/TestHuman/TestHuman.prefab";

		private static readonly string[] AbilityPaths =
		{
			HealAbilities + "/Lesser Heal.asset",
			HealAbilities + "/Lesser Heal Self.asset",
			AreaAbilities + "/Area Lesser Heal.asset",
			AreaAbilities + "/Area Lesser Fire.asset",
			AreaAbilities + "/Area Minor Armor.asset",
			AreaAbilities + "/Area Minor Movement Speed.asset",
			AreaAbilities + "/Area Knockback.asset",
		};

		private static readonly string[] RingPaths =
		{
			FX + "/Heal Nova.prefab", FX + "/Fire Nova.prefab", FX + "/Armor Nova.prefab", FX + "/Haste Nova.prefab", FX + "/Shockwave.prefab",
		};

		private static AbilityTemplate Load(string path)
		{
			AbilityTemplate t = AssetDatabase.LoadAssetAtPath<AbilityTemplate>(ProjectFiles.Path(path));
			if (t == null)
			{
				Assert.Inconclusive($"'{path}' has not been generated (TestAbilitySuiteGenerator.GenerateAll).");
			}
			return t;
		}

		// ── Knockback: direction follows the attack ──────────────────────────────────────────

		[Test]
		public void Knockback_Auto_PushesByTheShapeOfTheAttack()
		{
			// An area around an object is a blast: away from the object.
			Assert.That(KnockbackHitAction.ResolveOrigin(KnockbackDirection.Auto, true, true, false, AbilitySpawnTarget.PointBlank), Is.EqualTo(Origin.AbilityObject));
			// A self-centred area (no object) pushes away from the caster.
			Assert.That(KnockbackHitAction.ResolveOrigin(KnockbackDirection.Auto, false, false, true, AbilitySpawnTarget.Self), Is.EqualTo(Origin.Caster));
			// A projectile's direct hit pushes along its flight.
			Assert.That(KnockbackHitAction.ResolveOrigin(KnockbackDirection.Auto, true, false, false, AbilitySpawnTarget.SpawnerWithCameraRotation), Is.EqualTo(Origin.Heading));
			Assert.That(KnockbackHitAction.ResolveOrigin(KnockbackDirection.Auto, true, false, false, AbilitySpawnTarget.Camera), Is.EqualTo(Origin.Heading));
			// A projectile whose hit event fans out over an area pushes away from where it landed.
			Assert.That(KnockbackHitAction.ResolveOrigin(KnockbackDirection.Auto, true, false, true, AbilitySpawnTarget.SpawnerWithCameraRotation), Is.EqualTo(Origin.Impact));
			// A melee volume pushes away from the swinger.
			Assert.That(KnockbackHitAction.ResolveOrigin(KnockbackDirection.Auto, true, false, false, AbilitySpawnTarget.Forward), Is.EqualTo(Origin.Caster));
			// A volume placed where it acts (a trap) pushes from its own centre.
			Assert.That(KnockbackHitAction.ResolveOrigin(KnockbackDirection.Auto, true, false, false, AbilitySpawnTarget.Target), Is.EqualTo(Origin.AbilityObject));
		}

		[Test]
		public void Knockback_ExplicitModes_OverrideTheShape()
		{
			Assert.That(KnockbackHitAction.ResolveOrigin(KnockbackDirection.AwayFromCaster, true, true, false, AbilitySpawnTarget.PointBlank), Is.EqualTo(Origin.Caster));
			Assert.That(KnockbackHitAction.ResolveOrigin(KnockbackDirection.Heading, true, true, false, AbilitySpawnTarget.PointBlank), Is.EqualTo(Origin.Heading));
			Assert.That(KnockbackHitAction.ResolveOrigin(KnockbackDirection.Heading, false, false, false, AbilitySpawnTarget.Self), Is.EqualTo(Origin.Caster), "no object, no heading");
			Assert.That(KnockbackHitAction.ResolveOrigin(KnockbackDirection.AwayFromCentre, true, false, false, AbilitySpawnTarget.Spawner), Is.EqualTo(Origin.AbilityObject));
		}

		[Test]
		public void Knockback_IsDefaultedToAuto_SoShippedAssetsKeepTheirMeaning()
		{
			Assert.That(new KnockbackHitAction().Direction, Is.EqualTo(KnockbackDirection.Auto));
			Assert.That((int)KnockbackDirection.Auto, Is.Zero, "an asset authored before the field existed reads 0");
		}

		[Test]
		public void Knockback_Blast_PushesEveryVictimOutwardAcrossTheGround()
		{
			Vector3 centre = new Vector3(10f, 0f, 10f);
			Vector3 east = KnockbackHitAction.ResolveDirection(Origin.AbilityObject, Vector3.forward, centre, centre + new Vector3(3f, 1.5f, 0f), Vector3.forward);
			Vector3 north = KnockbackHitAction.ResolveDirection(Origin.AbilityObject, Vector3.forward, centre, centre + new Vector3(0f, -2f, 4f), Vector3.forward);
			Assert.That(Vector3.Distance(east, Vector3.right), Is.LessThan(1e-4f), "radial, flattened");
			Assert.That(Vector3.Distance(north, Vector3.forward), Is.LessThan(1e-4f));
			// Standing on the centre: the fallback, never a zero vector.
			Vector3 onCentre = KnockbackHitAction.ResolveDirection(Origin.AbilityObject, Vector3.forward, centre, centre, new Vector3(0f, 0.5f, -2f));
			Assert.That(Vector3.Distance(onCentre, Vector3.back), Is.LessThan(1e-4f));
		}

		[Test]
		public void Knockback_Projectile_PushesAlongItsFlight()
		{
			Vector3 heading = new Vector3(0f, -0.2f, 1f);
			Vector3 push = KnockbackHitAction.ResolveDirection(Origin.Heading, heading, Vector3.zero, new Vector3(5f, 0f, 5f), Vector3.forward);
			Assert.That(Vector3.Distance(push, heading.normalized), Is.LessThan(1e-4f), "the full heading, as the action always applied it");
		}

		[Test]
		public void NpcKnockback_SlidesAboutAMetreAndAThird_ThenStops()
		{
			const float tick = 1f / 30f;
			Vector3 v = new Vector3(8f, 0f, 0f);
			float travelled = 0f;
			int ticks = 0;
			while (v != Vector3.zero && ticks < 300)
			{
				travelled += v.x * tick;
				v = AIController.DecayKnockback(v, 24f, tick);
				++ticks;
			}
			Assert.That(ticks, Is.LessThan(20), "about a third of a second");
			Assert.That(travelled, Is.InRange(1.2f, 1.6f), "v² / 2a = 1.33 m, plus one tick of discretisation");
			Assert.That(AIController.DecayKnockback(new Vector3(0f, 0f, 3f), 0f, tick), Is.EqualTo(Vector3.zero), "no deceleration never slides forever");
			Vector3 slowed = AIController.DecayKnockback(new Vector3(0f, 0f, 3f), 24f, tick);
			Assert.That(slowed.z, Is.InRange(2.1f, 2.3f), "direction kept, speed down by 24 × tick");
		}

		[Test]
		public void TheBrainSeam_CarriesKnockback()
		{
			Assert.That(typeof(INPCBrain).GetMethod(nameof(INPCBrain.ApplyKnockback)), Is.Not.Null);
			Assert.That(typeof(INPCBrain).IsAssignableFrom(typeof(AIController)), Is.True);
		}

		// ── Sized effects ────────────────────────────────────────────────────────────────────

		[Test]
		public void SpawnFX_ScalesTheEffectToItsSize()
		{
			GameObject prefab = new GameObject("SizedFXProbe");
			GameObject instance = null;
			try
			{
				instance = PlayFXAction.SpawnFX(prefab, Vector3.zero, SceneManager.GetActiveScene(), 12f);
				Assert.That(instance.transform.localScale, Is.EqualTo(Vector3.one * 12f));
				Object.DestroyImmediate(instance);
				instance = PlayFXAction.SpawnFX(prefab, Vector3.zero, SceneManager.GetActiveScene());
				Assert.That(instance.transform.localScale, Is.EqualTo(Vector3.one), "0 keeps the prefab's scale");
			}
			finally
			{
				if (instance != null) Object.DestroyImmediate(instance);
				Object.DestroyImmediate(prefab);
			}
		}

		// ── The generated suite ──────────────────────────────────────────────────────────────

		[Test]
		public void LesserHeal_IsAProjectileThatHealsOnlyFriends()
		{
			AbilityTemplate heal = Load(AbilityPaths[0]);
			Assert.That(heal.AbilitySpawnTarget, Is.EqualTo(AbilitySpawnTarget.SpawnerWithCameraRotation));
			Assert.That(heal.Speed, Is.GreaterThan(0f));
			Assert.That(heal.AbilityObjectPrefab, Is.Not.Null);
			Assert.That(heal.AbilityObjectPrefab.GetComponentInChildren<ParticleSystem>(), Is.Not.Null, "it has a visible body");
			AbilityOnHitEvent hit = heal.OnHitEvents.Single();
			Assert.That(hit.OnConditionsMetActions.OfType<ApplyHealAction>().Count(), Is.EqualTo(1));
			TargetAllianceCondition alliance = hit.TargetSelector.Conditions.OfType<TargetAllianceCondition>().Single();
			Assert.That(alliance.ApplyToAllies, Is.True);
			Assert.That(alliance.ApplyToEnemy || alliance.ApplyToNeutral, Is.False, "an enemy it strikes is spared");
			Assert.That(heal.OnDestroyEvents.SelectMany(e => e.OnConditionsMetActions).OfType<PlayFXAction>().Any(a => a.FXPrefab != null), Is.True, "a burst where it ends");
		}

		[Test]
		public void LesserHealSelf_HealsTheCasterWithABurst()
		{
			AbilityTemplate heal = Load(AbilityPaths[1]);
			Assert.That(heal.AbilitySpawnTarget, Is.EqualTo(AbilitySpawnTarget.Self));
			AbilityOnHitEvent hit = heal.OnHitEvents.Single();
			Assert.That(hit.TargetSelector, Is.InstanceOf<InitiatorTargetSelector>());
			Assert.That(hit.OnConditionsMetActions.OfType<ApplyHealAction>().Count(), Is.EqualTo(1));
			Assert.That(hit.OnConditionsMetActions.OfType<PlayFXAction>().Any(a => a.FXPrefab != null), Is.True);
		}

		[Test]
		public void EveryAreaAbility_IsABlastWhoseRingFillsItsRadius()
		{
			foreach (string path in AbilityPaths.Skip(2))
			{
				AbilityTemplate area = Load(path);
				Assert.That(area.AbilitySpawnTarget, Is.EqualTo(AbilitySpawnTarget.PointBlank), area.name);
				Assert.That(area.AbilityObjectPrefab, Is.Not.Null, area.name);
				AbilityOnSpawnEvent burst = area.OnSpawnEvents.Single();
				AbilityApplyAreaAction query = burst.OnConditionsMetActions.OfType<AbilityApplyAreaAction>().Single();
				PlayFXAction ring = burst.OnConditionsMetActions.OfType<PlayFXAction>().Single();
				float radius = ((ConstantFloatValue)query.RadiusValue).Amount;
				Assert.That(ring.FXPrefab, Is.Not.Null, area.name);
				Assert.That(ring.Size, Is.EqualTo(radius * 2f).Within(1e-4f), $"{area.name}: a 1 m effect at Size 2r reaches r");
				Assert.That(((ConstantValue)query.MaxHitsValue).Amount, Is.InRange(1, 6), area.name);
				Assert.That(area.OnHitEvents.Count, Is.EqualTo(1), $"{area.name}: what the blast does to each body");
				Assert.That(area.ActivationConditions.OfType<HasResourceCondition>().Any(c => c.RequiredAmount > 0), Is.True, $"{area.name} costs something");
			}
		}

		[Test]
		public void HostileAreas_NeverStrikeTheCaster_AndLeaveTheRestToTheHarmRule()
		{
			foreach (string path in new[] { AbilityPaths[3], AbilityPaths[6] })
			{
				AbilityTemplate area = Load(path);
				TargetAllianceCondition alliance = area.OnHitEvents.Single().TargetSelector.Conditions.OfType<TargetAllianceCondition>().Single();
				Assert.That(alliance.ApplyToSelf, Is.False, $"{area.name}: the caster is in its own query");
				Assert.That(alliance.ApplyToAllies, Is.False, area.name);
				Assert.That(alliance.ApplyToEnemy && alliance.ApplyToNeutral, Is.True, $"{area.name}: wildlife reads as neutral");
			}
		}

		[Test]
		public void FriendlyAreas_ReachTheCasterAndAllies_Only()
		{
			foreach (string path in new[] { AbilityPaths[2], AbilityPaths[4], AbilityPaths[5] })
			{
				AbilityTemplate area = Load(path);
				TargetAllianceCondition alliance = area.OnHitEvents.Single().TargetSelector.Conditions.OfType<TargetAllianceCondition>().Single();
				Assert.That(alliance.ApplyToSelf && alliance.ApplyToAllies, Is.True, area.name);
				Assert.That(alliance.ApplyToEnemy || alliance.ApplyToNeutral, Is.False, area.name);
			}
		}

		[Test]
		public void AreaKnockback_PushesFromTheBlast()
		{
			AbilityTemplate knockback = Load(AbilityPaths[6]);
			KnockbackHitAction push = knockback.OnHitEvents.Single().OnConditionsMetActions.OfType<KnockbackHitAction>().Single();
			Assert.That(push.Direction, Is.EqualTo(KnockbackDirection.Auto));
			// What Auto makes of this ability: every victim is a splash of a PointBlank object.
			Assert.That(KnockbackHitAction.ResolveOrigin(push.Direction, true, true, false, knockback.AbilitySpawnTarget), Is.EqualTo(Origin.AbilityObject));
		}

		[Test]
		public void TheRings_AreFlatAndReachTheEdgeOfTheMetreCube()
		{
			foreach (string path in RingPaths)
			{
				GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectFiles.Path(path));
				if (prefab == null) Assert.Inconclusive($"'{path}' has not been generated.");
				GameObject probe = Object.Instantiate(prefab);
				try
				{
					ParticleSystem ps = probe.GetComponentInChildren<ParticleSystem>();
					var main = ps.main;
					main.stopAction = ParticleSystemStopAction.None;
					Assert.That(main.scalingMode, Is.EqualTo(ParticleSystemScalingMode.Hierarchy), $"{prefab.name}: Size must scale it");
					ps.Simulate(main.startLifetime.constantMax * 0.95f / Mathf.Max(0.01f, main.simulationSpeed), true, true, false); // Simulate is scaled by simulationSpeed
					var particles = new ParticleSystem.Particle[main.maxParticles];
					int count = ps.GetParticles(particles);
					Assert.That(count, Is.GreaterThan(0), prefab.name);
					float height = 0f, reach = 0f;
					for (int i = 0; i < count; ++i)
					{
						height = Mathf.Max(height, Mathf.Abs(particles[i].position.y));
						reach = Mathf.Max(reach, new Vector2(particles[i].position.x, particles[i].position.z).magnitude);
					}
					Assert.That(height, Is.LessThan(0.05f), $"{prefab.name} lies on the ground");
					Assert.That(reach, Is.InRange(0.4f, 0.55f), $"{prefab.name} reaches the cube's edge and no further");
				}
				finally
				{
					Object.DestroyImmediate(probe);
				}
			}
		}

		[Test]
		public void TheMovementBuff_WearsTheHasteEffectAtTheAnkles()
		{
			BaseBuffTemplate buff = AssetDatabase.LoadAssetAtPath<BaseBuffTemplate>(ProjectFiles.Path(SpeedBuff));
			Assert.That(buff, Is.Not.Null);
			Assert.That(buff.FXPrefabReference != null && buff.FXPrefabReference.RuntimeKeyIsValid(), Is.True, "the buff names an effect");
			GameObject fx = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(buff.FXPrefabReference.AssetGUID));
			Assert.That(fx, Is.Not.Null);
			FXBoneAttachment attachment = fx.GetComponent<FXBoneAttachment>();
			Assert.That(attachment, Is.Not.Null);
			CollectionAssert.AreEquivalent(new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot }, attachment.Bones);
			ParticleSystem emitter = attachment.Emitter.GetComponent<ParticleSystem>();
			Assert.That(emitter.main.loop, Is.True, "lasts as long as the buff");
			Assert.That(emitter.main.simulationSpace, Is.EqualTo(ParticleSystemSimulationSpace.World), "trails behind a runner");

			AddressableAssetEntry entry = AddressableAssetSettingsDefaultObject.Settings?.FindAssetEntry(buff.FXPrefabReference.AssetGUID);
			Assert.That(entry, Is.Not.Null, "an AssetReference the client can load");
		}

		[Test]
		public void TheHasteEffect_SitsOnTheTestHumansAnkleBones()
		{
			GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectFiles.Path(TestHuman));
			GameObject fx = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectFiles.Path(FX + "/Haste Ankles.prefab"));
			if (model == null || fx == null) Assert.Inconclusive("TestHuman or Haste Ankles is missing.");

			GameObject meshRoot = new GameObject("MeshRoot");
			try
			{
				GameObject body = (GameObject)Object.Instantiate(model, meshRoot.transform);
				GameObject worn = Object.Instantiate(fx, meshRoot.transform); // as BaseBuffTemplate.OnApplyFX does
				FXBoneAttachment attachment = worn.GetComponent<FXBoneAttachment>();
				typeof(FXBoneAttachment).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(attachment, null);

				Animator animator = body.GetComponentInChildren<Animator>();
				Assert.That(animator != null && animator.avatar != null && animator.avatar.isHuman, Is.True, "the TestHuman is a humanoid");
				Transform left = HumanoidBones.Find(animator, animator.transform, HumanBodyBones.LeftFoot);
				Transform right = HumanoidBones.Find(animator, animator.transform, HumanBodyBones.RightFoot);
				Assert.That(left, Is.Not.Null);
				Assert.That(right, Is.Not.Null);

				ParticleSystem[] onLeft = left.GetComponentsInChildren<ParticleSystem>(true).Where(p => p.transform.parent == left).ToArray();
				ParticleSystem[] onRight = right.GetComponentsInChildren<ParticleSystem>(true).Where(p => p.transform.parent == right).ToArray();
				Assert.That(onLeft.Length, Is.EqualTo(1), "one emitter on the left ankle");
				Assert.That(onRight.Length, Is.EqualTo(1), "one emitter on the right ankle");
				Assert.That(onLeft[0].transform.lossyScale.x, Is.EqualTo(attachment.WorldSize).Within(1e-3f), "sized in metres whatever the bone's scale");
				Assert.That(Vector3.Distance(onLeft[0].transform.position, left.position), Is.LessThan(1e-3f));
			}
			finally
			{
				Object.DestroyImmediate(meshRoot);
			}
		}

		[Test]
		public void GeneralGoods_SellsEverything()
		{
			MerchantTemplate merchant = AssetDatabase.LoadAssetAtPath<MerchantTemplate>(ProjectFiles.Path(Merchant));
			Assert.That(merchant, Is.Not.Null);
			foreach (string path in AbilityPaths)
			{
				AbilityTemplate ability = Load(path);
				Assert.That(merchant.Abilities, Does.Contain(ability), $"{ability.name} template");
				Assert.That(merchant.PremadeAbilities.Any(p => p != null && p.Ability == ability), Is.True, $"{ability.name} premade");
				foreach (AbilityEvent e in ability.OnHitEvents.Cast<AbilityEvent>().Concat(ability.OnSpawnEvents).Concat(ability.OnDestroyEvents))
				{
					if (AssetDatabase.GetAssetPath(e).Contains("/Move/"))
					{
						continue; // the shared projectile move, not part of this suite
					}
					Assert.That(merchant.AbilityEvents, Does.Contain(e), $"{ability.name}: {e.name}");
				}
			}
			foreach (PremadeAbilityTemplate premade in merchant.PremadeAbilities.Where(p => p != null))
			{
				Assert.That(premade.Validate(out string reason), Is.True, $"{premade.name}: {reason}");
			}
		}

		[Test]
		public void EverythingGenerated_IsAddressable()
		{
			AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
			Assert.That(settings, Is.Not.Null);
			var expected = new List<(string path, string group)>();
			foreach (string path in AbilityPaths)
			{
				AbilityTemplate a = Load(path);
				expected.Add((path, "Shared_Static_Permanent"));
				if (a.AbilityObjectPrefab != null) expected.Add((AssetDatabase.GetAssetPath(a.AbilityObjectPrefab), "Shared_Static_Permanent"));
				foreach (AbilityEvent e in a.OnHitEvents.Cast<AbilityEvent>().Concat(a.OnSpawnEvents).Concat(a.OnDestroyEvents))
					expected.Add((AssetDatabase.GetAssetPath(e), "Shared_Static_Permanent"));
			}
			foreach (string ring in RingPaths) expected.Add((ring, "Client_Dynamic"));
			expected.Add((FX + "/Heal.prefab", "Client_Dynamic"));
			expected.Add((FX + "/Heal Burst.prefab", "Client_Dynamic"));
			expected.Add((FX + "/Haste Ankles.prefab", "Client_Dynamic"));

			foreach ((string path, string group) in expected)
			{
				AddressableAssetEntry entry = settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(path));
				Assert.That(entry, Is.Not.Null, $"{path} is not addressable");
				Assert.That(entry.parentGroup.Name, Is.EqualTo(group), path);
				Assert.That(entry.labels, Does.Contain(group), path);
			}
		}
	}
}
