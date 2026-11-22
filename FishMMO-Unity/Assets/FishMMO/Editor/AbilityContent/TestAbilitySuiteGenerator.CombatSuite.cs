using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.AbilityContent
{
	/// <summary>
	/// The crowd-control, over-time, projectile-behaviour and defence half of the test ability suite (2026-11-22): Stunning
	/// Blow, Freezing Bolt, Mesmerize, Ignite, Rejuvenate (and Self), Ricochet Shot, Forked Bolt, Chain Lightning, Shield
	/// Block and Deflect, with their buffs, effects and premades.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Balance</b>, against the same baseline pools as the rest of the suite (100 health, mana and stamina; health does not
	/// regenerate):
	/// </para>
	/// <list type="bullet">
	///   <item>Control is short and cools down long. Stunning Blow: 3 physical and a 2 s stun, 15 stamina, 12 s. Freezing Bolt:
	///   3 water and a 3 s freeze, 12 mana, 20 s. A stun and a freeze both stop moving, turning, casting and item use
	///   (<see cref="CharacterIncapacitation"/>); the freeze also holds the body's pose in ice.</item>
	///   <item>Mesmerize turns a charmable NPC (the orcs) to fight for you for 12 s: 20 mana, 45 s, so it is up a quarter of
	///   the time at most. Players, pets and wildlife are never charmed.</item>
	///   <item>Over time beats up front: Ignite deals 2 fire on impact and 2 a second for 6 s (14 in all, stacking to three)
	///   for 8 mana; Rejuvenate restores 3 a second for 8 s (24) for 10 mana, twice Lesser Heal's 12 for a slower return.</item>
	///   <item>Ricochet Shot: 4 physical, strikes up to three times, turning off each body and bouncing off walls (10 stamina,
	///   6 s). Forked Bolt: 4 wind, then splits into two bolts of 4 each fanned 60° (8 mana, 6 s). Chain Lightning: 5 wind to
	///   the body struck and up to three more, each within 6 m of the last (15 mana, 10 s).</item>
	///   <item>Shield Block is HELD (up to 6 s, 1.5 s cooldown after): 75% off every blow from the front 150°, each blocked
	///   blow costing 5 stamina plus half a point per point blocked; when stamina cannot pay the guard breaks and the blow
	///   lands in full. Deflect: 15 stamina, 4 s, a 0.4 s window turning back any projectile from the front 150°.</item>
	/// </list>
	/// <para>
	/// Part of <see cref="GenerateAll"/>, and runnable alone (<see cref="GenerateCombatSuiteOnly"/>) so this content can be
	/// rebuilt without rewriting the rest of the suite. Idempotent, like the rest.
	/// </para>
	/// </remarks>
	public static partial class TestAbilitySuiteGenerator
	{
		public const string OverTimeAbilityFolder = "Assets/FishMMO/Modules/Abilities/Templates/Types/Over Time";
		public const string ProjectileAbilityFolder = "Assets/FishMMO/Modules/Abilities/Templates/Types/Projectile";
		public const string DefenseAbilityFolder = "Assets/FishMMO/Modules/Abilities/Templates/Types/Defense";
		public const string OverTimeEventFolder = "Assets/FishMMO/Modules/Abilities/Templates/Events/Hit/Over Time";
		public const string ProjectileEventFolder = "Assets/FishMMO/Modules/Abilities/Templates/Events/Hit/Projectile";
		public const string DefenseEventFolder = "Assets/FishMMO/Modules/Abilities/Templates/Events/Defense";
		public const string OverTimePrefabFolder = "Assets/Prefabs/Shared/Entity/Abilities/Types/Over Time";
		public const string ProjectilePrefabFolder = "Assets/Prefabs/Shared/Entity/Abilities/Types/Projectile";

		private const string PathWaterDamage = "Assets/FishMMO/Templates/Entity/CharacterAttributes/Damage/Water Damage.asset";
		private const string PathWindDamage = "Assets/FishMMO/Templates/Entity/CharacterAttributes/Damage/Wind Damage.asset";
		private const string PathHealth = "Assets/FishMMO/Templates/Entity/CharacterAttributes/Resource/Health.asset";
		private const string PathPunchPrefab = "Assets/Prefabs/Shared/Entity/Abilities/Types/Punch/Punch.prefab";
		private const string PathHealProjectile = HealPrefabFolder + "/Lesser Heal Projectile.prefab";
		private const string PathHealBurst = FXFolder + "/Heal Burst.prefab";

		private static readonly Palette FrostPalette = new Palette(new Color(0.35f, 0.70f, 1.0f), new Color(0.90f, 0.97f, 1.0f));
		private static readonly Palette StunPalette = new Palette(new Color(1.0f, 0.80f, 0.20f), new Color(1.0f, 1.0f, 0.85f));
		private static readonly Palette CharmPalette = new Palette(new Color(0.85f, 0.25f, 0.95f), new Color(1.0f, 0.75f, 0.95f));
		private static readonly Palette StormPalette = new Palette(new Color(0.30f, 0.55f, 1.0f), new Color(0.85f, 0.95f, 1.0f));
		private static readonly Palette ForkPalette = new Palette(new Color(0.10f, 0.85f, 0.75f), new Color(0.80f, 1.0f, 0.95f));
		private static readonly Palette SteelPalette = new Palette(new Color(0.55f, 0.60f, 0.70f), new Color(0.95f, 0.95f, 1.0f));

		// Tuning, named once: descriptions, buffs and events read the same numbers.
		private const float StunSeconds = 2f, FreezeSeconds = 3f, CharmSeconds = 12f;
		private const int BurnPerSecond = 2, BurnSeconds = 6, RejuvenatePerSecond = 3, RejuvenateSeconds = 8;
		private const int ChainLength = 4;
		private const float ChainRadius = 6f;
		private const float DeflectWindow = 0.4f, GuardArc = 150f;

		[DashboardTool(DashboardToolAttribute.Maintenance, "Generate Combat Abilities", Section = "Ability Content", Order = 3,
			Tooltip = "Generates Stunning Blow, Freezing Bolt, Mesmerize, Ignite, Rejuvenate, Ricochet Shot, Forked Bolt, Chain Lightning, Shield Block and Deflect with their buffs, effects and premades, registers them and adds them to the General Goods merchant. Touches nothing else.")]
		public static void GenerateCombatSuiteOnly()
		{
			written.Clear();
			problems.Clear();
			try
			{
				EnsureFolder(FXFolder);
				EnsureFolder(PremadeFolder);
				LoadDependencies();
				if (problems.Count > 0)
				{
					throw new InvalidOperationException("Missing dependencies:\n" + string.Join("\n", problems));
				}

				List<AbilityTemplate> abilities = new List<AbilityTemplate>();
				List<AbilityEvent> events = new List<AbilityEvent>();
				GenerateCombatSuite(abilities, events);
				List<PremadeAbilityTemplate> premades = GeneratePremades(abilities);
				WireMerchant(abilities, events, premades);

				AssetDatabase.SaveAssets();
				RegisterAddressables();
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();

				foreach (string path in written)
				{
					Debug.Log($"[TestAbilitySuiteGenerator] asset: {path}");
				}
				foreach (string problem in problems)
				{
					Debug.LogWarning($"[TestAbilitySuiteGenerator] PROBLEM: {problem}");
				}
				Debug.Log($"[TestAbilitySuiteGenerator] DONE assets={written.Count} problems={problems.Count}");
			}
			catch (Exception ex)
			{
				Debug.LogError($"[TestAbilitySuiteGenerator] FAILED: {ex}");
				throw;
			}
		}

		private static void GenerateCombatSuite(List<AbilityTemplate> abilities, List<AbilityEvent> events)
		{
			foreach (string folder in new[] { ControlAbilityFolder, ControlEventFolder, ControlPrefabFolder, OverTimeAbilityFolder,
				ProjectileAbilityFolder, DefenseAbilityFolder, OverTimeEventFolder, ProjectileEventFolder, DefenseEventFolder,
				OverTimePrefabFolder, ProjectilePrefabFolder, DestroyEventFolder, BuffFolder })
			{
				EnsureFolder(folder);
			}

			DamageAttributeTemplate waterDamage = Require<DamageAttributeTemplate>(PathWaterDamage);
			DamageAttributeTemplate windDamage = Require<DamageAttributeTemplate>(PathWindDamage);
			CharacterAttributeTemplate health = Require<CharacterAttributeTemplate>(PathHealth);
			GameObject punchPrefab = Require<GameObject>(PathPunchPrefab);
			GameObject healProjectile = Require<GameObject>(PathHealProjectile);
			GameObject healBurst = Require<GameObject>(PathHealBurst);
			if (problems.Count > 0)
			{
				throw new InvalidOperationException("Missing dependencies:\n" + string.Join("\n", problems));
			}

			// ── Effects ─────────────────────────────────────────────────────────────────────────
			GameObject frostBolt = BuildEffect(fireFX, "Frost Bolt", go => Recolour(go, FrostPalette));
			GameObject frostBurst = BuildEffect(explosionFX, "Frost Burst", go => Recolour(go, FrostPalette));
			GameObject charmBolt = BuildEffect(fireFX, "Mesmerize Bolt", go => Recolour(go, CharmPalette));
			GameObject charmBurst = BuildEffect(explosionFX, "Mesmerize Burst", go => Recolour(go, CharmPalette));
			GameObject stunBurst = BuildEffect(explosionFX, "Stun Burst", go => Recolour(go, StunPalette));
			GameObject igniteBurst = BuildEffect(explosionFX, "Ignite Burst", go => { });
			GameObject lightningBolt = BuildEffect(fireFX, "Lightning Bolt", go => Recolour(go, StormPalette));
			GameObject lightningBurst = BuildEffect(explosionFX, "Lightning Burst", go => Recolour(go, StormPalette));
			GameObject forkBolt = BuildEffect(fireFX, "Forked Bolt", go => Recolour(go, ForkPalette));
			GameObject forkBurst = BuildEffect(explosionFX, "Forked Burst", go => Recolour(go, ForkPalette));
			GameObject ricochetShot = BuildEffect(fireFX, "Ricochet Shot", go => Recolour(go, SteelPalette));
			GameObject ricochetSparks = BuildEffect(explosionFX, "Ricochet Sparks", go => Recolour(go, SteelPalette));
			GameObject deflectFlash = BuildEffect(explosionFX, "Deflect Flash", go => Recolour(go, SteelPalette));
			GameObject lightningArc = BuildLightningArc();

			// Worn for as long as the buff lasts (BuffController seats them under the model, on every client).
			GameObject stunStars = BuildWornEffect("Stun Stars", fireFX, StunPalette, new[] { HumanBodyBones.Head }, 0.7f,
				new Vector3(0f, 0.28f, 0f), keepUpright: true, worldSpace: false, ring: true, rate: 24f, rise: 0f);
			GameObject charmSwirl = BuildWornEffect("Mesmerize Swirl", fireFX, CharmPalette, new[] { HumanBodyBones.Head }, 0.8f,
				new Vector3(0f, 0.22f, 0f), keepUpright: true, worldSpace: false, ring: true, rate: 30f, rise: 0.1f);
			GameObject burning = BuildWornEffect("Burning", fireFX, null, new[] { HumanBodyBones.Chest }, 0.7f,
				Vector3.zero, keepUpright: false, worldSpace: true, ring: false, rate: 26f, rise: 1.5f);
			GameObject rejuvenation = BuildWornEffect("Rejuvenation", fireFX, HealPalette,
				new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot }, 0.4f,
				Vector3.zero, keepUpright: false, worldSpace: true, ring: false, rate: 22f, rise: 1.0f);
			GameObject frozenIce = BuildFrozenIce();

			// ── Ability objects ─────────────────────────────────────────────────────────────────
			GameObject frostProjectile = BuildFireballLikeProjectile(frostBolt, "Frost Bolt", $"{ControlPrefabFolder}/Frost Bolt Projectile.prefab");
			GameObject charmProjectile = BuildFireballLikeProjectile(charmBolt, "Mesmerize Bolt", $"{ControlPrefabFolder}/Mesmerize Bolt Projectile.prefab");
			GameObject igniteProjectile = BuildFireballLikeProjectile(fireFX, "Ignite", $"{OverTimePrefabFolder}/Ignite Projectile.prefab");
			GameObject lightningProjectile = BuildFireballLikeProjectile(lightningBolt, "Lightning Bolt", $"{ProjectilePrefabFolder}/Chain Lightning Projectile.prefab");
			GameObject forkProjectile = BuildFireballLikeProjectile(forkBolt, "Forked Bolt", $"{ProjectilePrefabFolder}/Forked Bolt Projectile.prefab");
			GameObject ricochetProjectile = BuildFireballLikeProjectile(ricochetShot, "Ricochet Shot", $"{ProjectilePrefabFolder}/Ricochet Shot Projectile.prefab");

			// ── Buffs ───────────────────────────────────────────────────────────────────────────
			StateBuffTemplate stunned = ScriptableObject.CreateInstance<StateBuffTemplate>();
			stunned.Description = "Stunned: cannot move, turn, cast or use items.";
			stunned.Duration = StunSeconds;
			stunned.IsDebuff = true;
			stunned.MaxStacks = 1u;
			stunned.Flag = CharacterFlags.IsStunned;
			stunned.FXPrefabReference = Reference(stunStars);
			stunned = Save(stunned, BuffFolder, "Stunned");

			StateBuffTemplate frozen = ScriptableObject.CreateInstance<StateBuffTemplate>();
			frozen.Description = "Frozen solid: cannot move, turn, cast or use items.";
			frozen.Duration = FreezeSeconds;
			frozen.IsDebuff = true;
			frozen.MaxStacks = 1u;
			frozen.Flag = CharacterFlags.IsFrozen;
			frozen.FXPrefabReference = Reference(frozenIce);
			frozen = Save(frozen, BuffFolder, "Frozen");

			CharmBuffTemplate mesmerized = ScriptableObject.CreateInstance<CharmBuffTemplate>();
			mesmerized.Description = "Mesmerized: fights for its charmer.";
			mesmerized.Duration = CharmSeconds;
			mesmerized.IsDebuff = true;
			mesmerized.MaxStacks = 1u;
			mesmerized.FXPrefabReference = Reference(charmSwirl);
			mesmerized = Save(mesmerized, BuffFolder, "Mesmerized");

			ResourceTickBuffTemplate burn = ScriptableObject.CreateInstance<ResourceTickBuffTemplate>();
			burn.Description = $"Burning: {BurnPerSecond} fire damage a second.";
			burn.Duration = BurnSeconds;
			burn.TickRate = 1f;
			burn.IsDebuff = true;
			burn.MaxStacks = 3u;
			burn.DamageAttribute = fireDamage;
			burn.TickAttributes = new List<BuffAttributeTemplate> { new BuffAttributeTemplate { Template = health, Value = -BurnPerSecond } };
			burn.FXPrefabReference = Reference(burning);
			burn = Save(burn, BuffFolder, "Burning");

			ResourceTickBuffTemplate rejuvenate = ScriptableObject.CreateInstance<ResourceTickBuffTemplate>();
			rejuvenate.Description = $"Rejuvenation: {RejuvenatePerSecond} health a second.";
			rejuvenate.Duration = RejuvenateSeconds;
			rejuvenate.TickRate = 1f;
			rejuvenate.MaxStacks = 1u;
			rejuvenate.TickAttributes = new List<BuffAttributeTemplate> { new BuffAttributeTemplate { Template = health, Value = RejuvenatePerSecond } };
			rejuvenate.FXPrefabReference = Reference(rejuvenation);
			rejuvenate = Save(rejuvenate, BuffFolder, "Rejuvenation");

			DeflectBuffTemplate deflecting = ScriptableObject.CreateInstance<DeflectBuffTemplate>();
			deflecting.Description = "Deflecting: projectiles from the front are turned back.";
			deflecting.Duration = DeflectWindow;
			deflecting.MaxStacks = 1u;
			deflecting.DeflectAngleDegrees = GuardArc;
			deflecting.MaxDeflections = 0; // everything that arrives inside the window
			deflecting = Save(deflecting, BuffFolder, "Deflecting");

			// ── Stunning Blow ───────────────────────────────────────────────────────────────────
			AbilityOnHitEvent stunImpact = SaveEvent<AbilityOnHitEvent>(ControlEventFolder, "Stunning Blow Impact", ev =>
			{
				ev.TargetSelector = Hostiles(new EventTargetSelector { FallbackToInitiator = false });
				ev.OnConditionsMetActions.Add(Damage(3, physicalDamage));
			});
			// The control as an effect (bases and effects, Jim 2026-11-22): bundled here, sold alone, craftable onto any base.
			AbilityOnHitEvent stunEffect = SaveEvent<AbilityOnHitEvent>(ControlEventFolder, "Stun", ev =>
			{
				ev.TargetSelector = Hostiles(new EventTargetSelector { FallbackToInitiator = false });
				ev.OnConditionsMetActions.Add(Buff(stunned));
				ev.OnConditionsMetActions.Add(new PlayFXAction { FXPrefab = stunBurst, Size = 1.0f, HeightOffset = 1.5f });
			});
			abilities.Add(SaveAbility(ControlAbilityFolder, "Stunning Blow", a =>
			{
				a.Description = $"A heavy blow to the head: 3 physical damage, and the foe is stunned for {StunSeconds:0} seconds. It cannot move, turn, cast or use items.";
				a.AbilityObjectPrefab = punchPrefab;
				a.AbilitySpawnTarget = AbilitySpawnTarget.Forward;
				a.Type = AbilityType.GroundedPhysical;
				a.ActivationTime = 0.3f;
				a.Speed = 0f;
				a.LifeTime = 0.05f;
				a.Cooldown = 12f;
				a.HitCount = 1;
				a.AdditionalEventSlots = 1;
				a.ActivationConditions.Add(Cost(stamina, 15));
				a.OnHitEvents.Add(stunImpact);
				a.OnHitEvents.Add(stunEffect);
			}));
			events.Add(stunImpact);
			events.Add(stunEffect);

			// ── Freezing Bolt ───────────────────────────────────────────────────────────────────
			AbilityOnHitEvent freezeImpact = SaveEvent<AbilityOnHitEvent>(ControlEventFolder, "Freezing Bolt Impact", ev =>
			{
				ev.TargetSelector = Hostiles(new EventTargetSelector { FallbackToInitiator = false });
				ev.OnConditionsMetActions.Add(Damage(3, waterDamage));
			});
			AbilityOnHitEvent freezeEffect = SaveEvent<AbilityOnHitEvent>(ControlEventFolder, "Freeze", ev =>
			{
				ev.TargetSelector = Hostiles(new EventTargetSelector { FallbackToInitiator = false });
				ev.OnConditionsMetActions.Add(Buff(frozen));
			});
			AbilityOnDestroyEvent frostFX = SaveEvent<AbilityOnDestroyEvent>(DestroyEventFolder, "Frost Impact FX Event", ev =>
			{
				ev.OnConditionsMetActions.Add(new PlayFXAction { FXPrefab = frostBurst, Size = 1.5f });
			});
			abilities.Add(SaveAbility(ControlAbilityFolder, "Freezing Bolt", a =>
			{
				a.Description = $"A shard of winter: 3 water damage, and the first foe it strikes is frozen solid for {FreezeSeconds:0} seconds. It cannot move, turn, cast or use items.";
				a.AbilityObjectPrefab = frostProjectile;
				a.AbilitySpawnTarget = AbilitySpawnTarget.SpawnerWithCameraRotation;
				a.Type = AbilityType.GroundedMagic;
				a.ActivationTime = 1.0f;
				a.Speed = 16f;
				a.LifeTime = 1.5f; // 24 m
				a.Cooldown = 20f;
				a.HitCount = 1;
				a.ActivationConditions.Add(Cost(mana, 12));
				a.OnTickEvents.Add(projectileMove);
				a.AdditionalEventSlots = 1;
				a.OnHitEvents.Add(freezeImpact);
				a.OnHitEvents.Add(freezeEffect);
				a.OnDestroyEvents.Add(frostFX);
			}));
			events.Add(freezeImpact);
			events.Add(freezeEffect);
			events.Add(frostFX);

			// ── Mesmerize ───────────────────────────────────────────────────────────────────────
			AbilityOnHitEvent charmImpact = SaveEvent<AbilityOnHitEvent>(ControlEventFolder, "Mesmerize Impact", ev =>
			{
				EventTargetSelector selector = Hostiles(new EventTargetSelector { FallbackToInitiator = false });
				selector.Conditions.Add(new CanBeCharmedCondition());
				ev.TargetSelector = selector;
				ev.OnConditionsMetActions.Add(Buff(mesmerized));
			});
			AbilityOnDestroyEvent charmFX = SaveEvent<AbilityOnDestroyEvent>(DestroyEventFolder, "Mesmerize Impact FX Event", ev =>
			{
				ev.OnConditionsMetActions.Add(new PlayFXAction { FXPrefab = charmBurst, Size = 1.5f });
			});
			abilities.Add(SaveAbility(ControlAbilityFolder, "Mesmerize", a =>
			{
				a.Description = $"Bends a creature's will to yours: a charmable foe it strikes fights for you for {CharmSeconds:0} seconds, following you and attacking what you attack. Players, pets and wild animals cannot be charmed.";
				a.AbilityObjectPrefab = charmProjectile;
				a.AbilitySpawnTarget = AbilitySpawnTarget.SpawnerWithCameraRotation;
				a.Type = AbilityType.GroundedMagic;
				a.ActivationTime = 1.5f;
				a.Speed = 14f;
				a.LifeTime = 2f; // 28 m
				a.Cooldown = 45f;
				a.HitCount = 1;
				a.ActivationConditions.Add(Cost(mana, 20));
				a.OnTickEvents.Add(projectileMove);
				a.OnHitEvents.Add(charmImpact);
				a.OnDestroyEvents.Add(charmFX);
			}));
			events.Add(charmImpact);
			events.Add(charmFX);

			// ── Ignite (damage over time) ───────────────────────────────────────────────────────
			AbilityOnHitEvent igniteImpact = SaveEvent<AbilityOnHitEvent>(OverTimeEventFolder, "Ignite Impact", ev =>
			{
				ev.TargetSelector = Hostiles(new EventTargetSelector { FallbackToInitiator = false });
				ev.OnConditionsMetActions.Add(Damage(2, fireDamage));
			});
			AbilityOnHitEvent burnEffect = SaveEvent<AbilityOnHitEvent>(OverTimeEventFolder, "Burn", ev =>
			{
				ev.TargetSelector = Hostiles(new EventTargetSelector { FallbackToInitiator = false });
				ev.OnConditionsMetActions.Add(Buff(burn));
			});
			AbilityOnDestroyEvent igniteFX = SaveEvent<AbilityOnDestroyEvent>(DestroyEventFolder, "Ignite Impact FX Event", ev =>
			{
				ev.OnConditionsMetActions.Add(new PlayFXAction { FXPrefab = igniteBurst, Size = 1.2f });
			});
			abilities.Add(SaveAbility(OverTimeAbilityFolder, "Ignite", a =>
			{
				a.Description = $"Sets the first foe it strikes alight: 2 fire damage, then {BurnPerSecond} fire damage a second for {BurnSeconds} seconds. Stacks up to three times.";
				a.AbilityObjectPrefab = igniteProjectile;
				a.AbilitySpawnTarget = AbilitySpawnTarget.SpawnerWithCameraRotation;
				a.Type = AbilityType.GroundedMagic;
				a.ActivationTime = 0.8f;
				a.Speed = 18f;
				a.LifeTime = 1.5f; // 27 m
				a.Cooldown = 8f;
				a.HitCount = 1;
				a.ActivationConditions.Add(Cost(mana, 8));
				a.OnTickEvents.Add(projectileMove);
				a.AdditionalEventSlots = 1;
				a.OnHitEvents.Add(igniteImpact);
				a.OnHitEvents.Add(burnEffect);
				a.OnDestroyEvents.Add(igniteFX);
			}));
			events.Add(igniteImpact);
			events.Add(burnEffect);
			events.Add(igniteFX);

			// ── Rejuvenate (heal over time) ─────────────────────────────────────────────────────
			AbilityOnHitEvent rejuvenateImpact = SaveEvent<AbilityOnHitEvent>(OverTimeEventFolder, "Rejuvenate Impact", ev =>
			{
				ev.TargetSelector = Healable(new EventTargetSelector { FallbackToInitiator = false });
				ev.OnConditionsMetActions.Add(Buff(rejuvenate));
			});
			AbilityOnDestroyEvent rejuvenateFX = SaveEvent<AbilityOnDestroyEvent>(DestroyEventFolder, "Rejuvenate Impact FX Event", ev =>
			{
				ev.OnConditionsMetActions.Add(new PlayFXAction { FXPrefab = healBurst, Size = 1.5f });
			});
			AbilityOnHitEvent rejuvenateSelf = SaveEvent<AbilityOnHitEvent>(OverTimeEventFolder, "Rejuvenate Self Event", ev =>
			{
				ev.TargetSelector = new InitiatorTargetSelector();
				ev.OnConditionsMetActions.Add(Buff(rejuvenate));
				ev.OnConditionsMetActions.Add(new PlayFXAction { FXPrefab = healBurst, Size = 1.5f, HeightOffset = 1f });
			});
			abilities.Add(SaveAbility(OverTimeAbilityFolder, "Rejuvenate", a =>
			{
				a.Description = $"A mote of slow healing: the ally or friendly character it strikes regains {RejuvenatePerSecond} health a second for {RejuvenateSeconds} seconds. An enemy it strikes is spared.";
				a.AbilityObjectPrefab = healProjectile;
				a.AbilitySpawnTarget = AbilitySpawnTarget.SpawnerWithCameraRotation;
				a.Type = AbilityType.GroundedMagic;
				a.ActivationTime = 1.0f;
				a.Speed = 14f;
				a.LifeTime = 2.5f; // 35 m
				a.Cooldown = 6f;
				a.HitCount = 1;
				a.AdditionalEventSlots = 1;
				a.ActivationConditions.Add(Cost(mana, 10));
				a.OnTickEvents.Add(projectileMove);
				a.OnHitEvents.Add(rejuvenateImpact);
				a.OnDestroyEvents.Add(rejuvenateFX);
			}));
			abilities.Add(SaveAbility(OverTimeAbilityFolder, "Rejuvenate Self", a =>
			{
				a.Description = $"You regain {RejuvenatePerSecond} health a second for {RejuvenateSeconds} seconds.";
				a.AbilitySpawnTarget = AbilitySpawnTarget.Self;
				a.Type = AbilityType.GroundedMagic;
				a.ActivationTime = 1.0f;
				a.Cooldown = 6f;
				a.HitCount = 1;
				a.ActivationConditions.Add(Cost(mana, 10));
				a.OnHitEvents.Add(rejuvenateSelf);
			}));
			events.Add(rejuvenateImpact);
			events.Add(rejuvenateFX);
			events.Add(rejuvenateSelf);

			// ── Ricochet Shot ───────────────────────────────────────────────────────────────────
			AbilityOnHitEvent ricochetImpact = SaveEvent<AbilityOnHitEvent>(ProjectileEventFolder, "Ricochet Shot Impact", ev =>
			{
				ev.TargetSelector = Hostiles(new EventTargetSelector { FallbackToInitiator = false });
				ev.OnConditionsMetActions.Add(Damage(4, physicalDamage));
			});
			// No selector: it acts on the shot, once per hit, body or wall.
			AbilityOnHitEvent ricochetBounce = SaveEvent<AbilityOnHitEvent>(ProjectileEventFolder, "Ricochet Bounce Event", ev =>
			{
				ev.OnConditionsMetActions.Add(new AbilityRicochetHitAction
				{
					ArcValue = new ConstantFloatValue { Amount = 90f },
					ReflectOffScenery = true,
				});
				ev.OnConditionsMetActions.Add(new PlayFXAction { FXPrefab = ricochetSparks, Size = 0.8f });
			});
			abilities.Add(SaveAbility(ProjectileAbilityFolder, "Ricochet Shot", a =>
			{
				a.Description = "A hard shot that glances off what it strikes: 4 physical damage to each of up to three foes, turning off bodies and bouncing off walls.";
				a.AbilityObjectPrefab = ricochetProjectile;
				a.AbilitySpawnTarget = AbilitySpawnTarget.SpawnerWithCameraRotation;
				a.Type = AbilityType.GroundedPhysical;
				a.ActivationTime = 0.4f;
				a.Speed = 20f;
				a.LifeTime = 1.5f; // 30 m in all, however it turns
				a.Cooldown = 6f;
				a.HitCount = 3;
				a.AdditionalEventSlots = 1;
				a.ActivationConditions.Add(Cost(stamina, 10));
				a.OnTickEvents.Add(projectileMove);
				a.OnHitEvents.Add(ricochetImpact);
				a.OnHitEvents.Add(ricochetBounce);
			}));
			events.Add(ricochetImpact);
			events.Add(ricochetBounce);

			// ── Forked Bolt ─────────────────────────────────────────────────────────────────────
			AbilityOnHitEvent forkImpact = SaveEvent<AbilityOnHitEvent>(ProjectileEventFolder, "Forked Bolt Impact", ev =>
			{
				ev.TargetSelector = Hostiles(new EventTargetSelector { FallbackToInitiator = false });
				ev.OnConditionsMetActions.Add(Damage(4, windDamage));
			});
			AbilityOnHitEvent forkSplit = SaveEvent<AbilityOnHitEvent>(ProjectileEventFolder, "Fork Split Event", ev =>
			{
				ev.OnConditionsMetActions.Add(new AbilityForkSplitAction
				{
					CountValue = new ConstantValue { Amount = 2 },
					SpreadValue = new ConstantFloatValue { Amount = 60f },
					OnlyOnCharacters = true,
				});
			});
			AbilityOnDestroyEvent forkFX = SaveEvent<AbilityOnDestroyEvent>(DestroyEventFolder, "Forked Bolt FX Event", ev =>
			{
				ev.OnConditionsMetActions.Add(new PlayFXAction { FXPrefab = forkBurst, Size = 1.0f });
			});
			abilities.Add(SaveAbility(ProjectileAbilityFolder, "Forked Bolt", a =>
			{
				a.Description = "A bolt of storm that splits where it strikes: 4 wind damage to the first foe, then two bolts fan out from it, 4 wind damage each.";
				a.AbilityObjectPrefab = forkProjectile;
				a.AbilitySpawnTarget = AbilitySpawnTarget.SpawnerWithCameraRotation;
				a.Type = AbilityType.GroundedMagic;
				a.ActivationTime = 0.8f;
				a.Speed = 18f;
				a.LifeTime = 1.5f; // 27 m; the forks live out what is left of it
				a.Cooldown = 6f;
				a.HitCount = 1;
				a.AdditionalEventSlots = 1;
				a.ActivationConditions.Add(Cost(mana, 8));
				a.OnTickEvents.Add(projectileMove);
				a.OnHitEvents.Add(forkImpact);
				a.OnHitEvents.Add(forkSplit);
				a.OnDestroyEvents.Add(forkFX);
			}));
			events.Add(forkImpact);
			events.Add(forkSplit);
			events.Add(forkFX);

			// ── Chain Lightning ─────────────────────────────────────────────────────────────────
			AbilityOnHitEvent chainStrike = SaveEvent<AbilityOnHitEvent>(ProjectileEventFolder, "Chain Lightning Strike", ev =>
			{
				ev.TargetSelector = Hostiles(new ChainTargetSelector
				{
					ChainLength = ChainLength,
					ChainRadius = ChainRadius,
					TargetLayer = ~0,
					QueryBufferHint = 16,
				});
				ev.OnConditionsMetActions.Add(Damage(5, windDamage));
				ev.OnConditionsMetActions.Add(new PlayFXAction { FXPrefab = lightningBurst, Size = 1.0f, HeightOffset = 1f });
			});
			AbilityOnHitEvent chainArcs = SaveEvent<AbilityOnHitEvent>(ProjectileEventFolder, "Chain Lightning Arcs", ev =>
			{
				ev.OnConditionsMetActions.Add(new PlayChainArcFXAction
				{
					ArcPrefab = lightningArc,
					ChainLength = ChainLength,
					ChainRadius = ChainRadius,
				});
			});
			abilities.Add(SaveAbility(ProjectileAbilityFolder, "Chain Lightning", a =>
			{
				a.Description = $"A bolt of lightning that leaps from foe to foe: 5 wind damage to the first it strikes and to up to {ChainLength - 1} more, each within {ChainRadius:0} m of the last.";
				a.AbilityObjectPrefab = lightningProjectile;
				a.AbilitySpawnTarget = AbilitySpawnTarget.SpawnerWithCameraRotation;
				a.Type = AbilityType.GroundedMagic;
				a.ActivationTime = 1.2f;
				a.Speed = 24f;
				a.LifeTime = 1.25f; // 30 m
				a.Cooldown = 10f;
				a.HitCount = 1;
				a.AdditionalEventSlots = 1;
				a.ActivationConditions.Add(Cost(mana, 15));
				a.OnTickEvents.Add(projectileMove);
				a.OnHitEvents.Add(chainStrike);
				a.OnHitEvents.Add(chainArcs);
			}));
			events.Add(chainStrike);
			events.Add(chainArcs);

			// ── Shield Block (held) ─────────────────────────────────────────────────────────────
			abilities.Add(SaveAbility(DefenseAbilityFolder, "Shield Block", a =>
			{
				a.Description = $"Hold to raise your guard (up to 6 seconds). Blows from the front take 75% less damage; each blow you block costs stamina (5, and 1 for every 2 damage blocked). Out of stamina, the guard breaks and the blow lands in full.";
				a.AbilitySpawnTarget = AbilitySpawnTarget.Self;
				a.Type = AbilityType.Block;
				a.ActivationTime = 6f; // the longest hold
				a.Cooldown = 1.5f;
				a.HitCount = 0;
				a.ActivationConditions.Add(Cost(stamina, 10));
				a.Guard = new AbilityGuard
				{
					ReducePercent = 75,
					ArcDegrees = GuardArc,
					CostResource = stamina,
					CostPerHit = 5,
					CostPerDamage = 0.5f,
				};
			}));

			// ── Deflect ─────────────────────────────────────────────────────────────────────────
			AbilityOnHitEvent deflect = SaveEvent<AbilityOnHitEvent>(DefenseEventFolder, "Deflect Event", ev =>
			{
				ev.TargetSelector = new InitiatorTargetSelector();
				ev.OnConditionsMetActions.Add(Buff(deflecting));
				ev.OnConditionsMetActions.Add(new PlayFXAction { FXPrefab = deflectFlash, Size = 1.2f, HeightOffset = 1.2f });
			});
			abilities.Add(SaveAbility(DefenseAbilityFolder, "Deflect", a =>
			{
				a.Description = $"A quick sweep of the arm: for {DeflectWindow:0.0} seconds, projectiles arriving from the front are turned back the way they came.";
				a.AbilitySpawnTarget = AbilitySpawnTarget.Self;
				a.Type = AbilityType.Deflect;
				a.ActivationTime = 0f;
				a.Cooldown = 4f;
				a.HitCount = 1;
				a.ActivationConditions.Add(Cost(stamina, 15));
				a.OnHitEvents.Add(deflect);
			}));
			events.Add(deflect);
		}

		private static AssetReferenceGameObject Reference(GameObject prefab)
		{
			return new AssetReferenceGameObject(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab)));
		}

		/// <summary>
		/// An effect worn on bones while a buff lasts (<see cref="FXBoneAttachment"/>): a looping copy of
		/// <paramref name="source"/> in <paramref name="palette"/> (null keeps its colours) seated on each of
		/// <paramref name="bones"/>. A <paramref name="ring"/> circles the bone (stars round a head); otherwise it emits from
		/// the bone and rises by <paramref name="rise"/> (flames, wisps).
		/// </summary>
		private static GameObject BuildWornEffect(string name, GameObject source, Palette? palette, HumanBodyBones[] bones,
			float worldSize, Vector3 offset, bool keepUpright, bool worldSpace, bool ring, float rate, float rise)
		{
			GameObject root = new GameObject(name);
			try
			{
				GameObject emitter = UnityEngine.Object.Instantiate(source);
				emitter.name = "Emitter";
				RemoveUnusedChildren(emitter);
				emitter.transform.SetParent(root.transform, false);
				emitter.transform.localPosition = Vector3.zero;
				emitter.transform.localRotation = Quaternion.identity;
				emitter.transform.localScale = Vector3.one;
				if (palette.HasValue)
				{
					Recolour(emitter, palette.Value);
				}
				foreach (ParticleSystem ps in emitter.GetComponentsInChildren<ParticleSystem>(true))
				{
					var main = ps.main;
					main.loop = true;
					main.prewarm = ring;
					main.playOnAwake = true;
					main.simulationSpace = worldSpace ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
					main.scalingMode = ParticleSystemScalingMode.Hierarchy;
					main.stopAction = ParticleSystemStopAction.None;
					var emission = ps.emission;
					emission.rateOverTime = rate;

					if (ring)
					{
						main.startSpeed = 0f;
						main.startSize = 0.16f;
						main.gravityModifier = 0f;
						var shape = ps.shape;
						shape.enabled = true;
						shape.shapeType = ParticleSystemShapeType.Circle;
						shape.radius = 0.45f;
						shape.radiusThickness = 0f;
						shape.arc = 360f;
						shape.rotation = RingRotation;
						// Round and round the bone.
						var velocity = ps.velocityOverLifetime;
						velocity.enabled = true;
						velocity.space = ParticleSystemSimulationSpace.Local;
						velocity.orbitalX = new ParticleSystem.MinMaxCurve(0f);
						velocity.orbitalY = new ParticleSystem.MinMaxCurve(3.5f);
						velocity.orbitalZ = new ParticleSystem.MinMaxCurve(0f);
						velocity.x = new ParticleSystem.MinMaxCurve(0f);
						velocity.y = new ParticleSystem.MinMaxCurve(rise);
						velocity.z = new ParticleSystem.MinMaxCurve(0f);
						var force = ps.forceOverLifetime;
						force.enabled = false;
					}
					else
					{
						var force = ps.forceOverLifetime;
						force.enabled = true;
						force.space = ParticleSystemSimulationSpace.World;
						force.x = new ParticleSystem.MinMaxCurve(0f);
						force.y = new ParticleSystem.MinMaxCurve(rise);
						force.z = new ParticleSystem.MinMaxCurve(0f);
					}
				}

				FXBoneAttachment attachment = root.AddComponent<FXBoneAttachment>();
				attachment.Bones = bones;
				attachment.Emitter = emitter.transform;
				attachment.WorldSize = worldSize;
				attachment.WorldOffset = offset;
				attachment.KeepUpright = keepUpright;
				return SavePrefab(root, $"{FXFolder}/{name}.prefab");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(root);
			}
		}

		/// <summary>
		/// What a frozen character wears: a slow cloud of ice motes about the body and, on the root,
		/// <see cref="FXFreezePose"/>, which holds the body's pose for as long as the ice lasts.
		/// </summary>
		private static GameObject BuildFrozenIce()
		{
			GameObject root = new GameObject("Frozen Ice");
			try
			{
				GameObject emitter = UnityEngine.Object.Instantiate(explosionFX);
				emitter.name = "Emitter";
				RemoveUnusedChildren(emitter);
				emitter.transform.SetParent(root.transform, false);
				emitter.transform.localPosition = Vector3.zero;
				emitter.transform.localRotation = Quaternion.identity;
				emitter.transform.localScale = Vector3.one;
				Recolour(emitter, FrostPalette);
				foreach (ParticleSystem ps in emitter.GetComponentsInChildren<ParticleSystem>(true))
				{
					var main = ps.main;
					main.loop = true;
					main.prewarm = true;
					main.playOnAwake = true;
					main.duration = 1f;
					main.startLifetime = 1.2f;
					main.startSpeed = 0.03f;
					main.startSize = 0.22f;
					main.gravityModifier = 0f;
					main.simulationSpeed = 1f;
					main.simulationSpace = ParticleSystemSimulationSpace.Local;
					main.scalingMode = ParticleSystemScalingMode.Hierarchy;
					main.stopAction = ParticleSystemStopAction.None;
					main.maxParticles = Mathf.Max(main.maxParticles, 80);
					var emission = ps.emission;
					emission.rateOverTime = 45f;
					emission.SetBursts(new ParticleSystem.Burst[0]);
					var shape = ps.shape;
					shape.enabled = true;
					shape.shapeType = ParticleSystemShapeType.Sphere;
					shape.radius = 0.5f;
					shape.radiusThickness = 0.15f; // a shell about the body
					var velocity = ps.velocityOverLifetime;
					velocity.enabled = false;
					var force = ps.forceOverLifetime;
					force.enabled = false;
				}

				FXBoneAttachment attachment = root.AddComponent<FXBoneAttachment>();
				attachment.Bones = new[] { HumanBodyBones.Chest };
				attachment.Emitter = emitter.transform;
				attachment.WorldSize = 1.3f;
				attachment.WorldOffset = new Vector3(0f, -0.2f, 0f);
				attachment.KeepUpright = true;
				root.AddComponent<FXFreezePose>();
				return SavePrefab(root, $"{FXFolder}/Frozen Ice.prefab");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(root);
			}
		}

		/// <summary>The arc a chain draws for each leap: a thin, bright LineRenderer flickered by <see cref="LightningArc"/>.</summary>
		private static GameObject BuildLightningArc()
		{
			return BuildArcLine("Lightning Arc", new Color(0.85f, 0.93f, 1f, 1f), new Color(0.45f, 0.65f, 1f, 0.9f), 0.09f, 0.04f, 0.12f, 0.35f);
		}

		/// <summary>
		/// A line effect drawn between two points by <see cref="LightningArc"/>: a lightning leap, a beam, a tracer or a drain
		/// stream, by its colours, its width from start to end, how far it strays (<paramref name="jitter"/>, a share of its
		/// length; 0 is straight) and how long it lasts.
		/// </summary>
		private static GameObject BuildArcLine(string name, Color start, Color end, float startWidth, float endWidth, float jitter, float seconds)
		{
			GameObject root = new GameObject(name);
			try
			{
				root.layer = VisualLayer;
				LineRenderer line = root.AddComponent<LineRenderer>();
				line.useWorldSpace = true;
				line.positionCount = 12;
				line.widthCurve = new AnimationCurve(new Keyframe(0f, startWidth), new Keyframe(0.5f, (startWidth + endWidth) * 0.5f), new Keyframe(1f, endWidth));
				line.startColor = start;
				line.endColor = end;
				line.numCapVertices = 2;
				line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
				line.receiveShadows = false;
				// The explosion's own particle material: additive and vertex-coloured, so the line takes its colours.
				ParticleSystemRenderer source = explosionFX.GetComponentInChildren<ParticleSystemRenderer>(true);
				if (source != null && source.sharedMaterial != null)
				{
					line.sharedMaterial = source.sharedMaterial;
				}
				else
				{
					problems.Add($"{name}: the Explosion effect has no particle material to borrow.");
				}
				LightningArc arc = root.AddComponent<LightningArc>();
				arc.Seconds = seconds;
				arc.Segments = 12;
				arc.Jitter = jitter;
				return SavePrefab(root, $"{FXFolder}/{name}.prefab");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(root);
			}
		}
	}
}
