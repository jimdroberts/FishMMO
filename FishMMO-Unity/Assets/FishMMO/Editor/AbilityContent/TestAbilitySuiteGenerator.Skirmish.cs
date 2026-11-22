using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.AbilityContent
{
	/// <summary>
	/// Stealth, skirmish and utility abilities for combat testing (2026-11-22, second round): Invisibility, See Invisible,
	/// Stab and the Backstab effect, Swipe, Disrupting Strike, Hamstring, Provoke, Arcane Ward, Piercing Arrow, Crossbow Shot and Drain Life,
	/// and the Pet Regeneration summon effect.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Each exercises a different combat path: a state flag the AI reads (invisible / sees invisible), a buff that ends when
	/// its carrier acts, a positional gate (behind the target), a frontal arc on a point-blank area, the interrupt, a
	/// movement debuff, NPC taunt, an absorb pool, a projectile that pierces, a hitscan shot, damage and heal in one hit,
	/// and a permanent effect crafted onto a summon.
	/// </para>
	/// <list type="bullet">
	///   <item>Invisibility: 60 s, NPCs do not sweep you up, your model is 80% gone and your nameplate hidden; any ability or
	///   consumable ends it. 15 mana, 30 s. See Invisible: 60 s, invisible characters are drawn faint with their nameplates
	///   (never <c>/gm invis</c>). 10 mana, 30 s.</item>
	///   <item>Stab: 5 physical, 10 stamina, 4 s, one crafting slot. Backstab is an EFFECT (an OnHit event gated by
	///   <see cref="IsBehindTargetCondition"/>): +10 physical when the attacker is behind the struck foe (120° arc), on any
	///   ability it is crafted onto; "Premade Backstab" is Stab with it. Swipe: 5 physical to up to 5 foes in a 120° cone
	///   3.5 m deep. 12 stamina, 5 s.</item>
	///   <item>Disrupting Strike: 2 physical and the target's cast is broken. 10 stamina, 10 s. Hamstring: 2 physical and
	///   -40% move speed for 6 s. 10 stamina, 8 s. Provoke: an NPC within 20 m turns on you. 8 stamina, 12 s.</item>
	///   <item>Arcane Ward: absorbs 30 damage for 20 s. 15 mana, 30 s. Piercing Arrow: 4 physical to each of the first
	///   three bodies in its path. 8 stamina, 4 s. Crossbow Shot: 6 physical, instant (hitscan, 30 m). 8 stamina, 3 s.
	///   Drain Life: 5 death damage, and you regain what it dealt's worth, 5. 12 mana, 8 s.</item>
	///   <item>Pet Regeneration (crafted onto a summon): the pet regenerates 2 health a second for as long as it lives,
	///   restored with it at login.</item>
	/// </list>
	/// </remarks>
	public static partial class TestAbilitySuiteGenerator
	{
		public const string StealthAbilityFolder = "Assets/FishMMO/Modules/Abilities/Templates/Types/Stealth";
		public const string MeleeAbilityFolder = "Assets/FishMMO/Modules/Abilities/Templates/Types/Melee";
		public const string UtilityAbilityFolder = "Assets/FishMMO/Modules/Abilities/Templates/Types/Utility";
		public const string StealthEventFolder = "Assets/FishMMO/Modules/Abilities/Templates/Events/Hit/Stealth";
		public const string MeleeEventFolder = "Assets/FishMMO/Modules/Abilities/Templates/Events/Hit/Melee";
		public const string UtilityEventFolder = "Assets/FishMMO/Modules/Abilities/Templates/Events/Hit/Utility";
		public const string SummonEventFolder = "Assets/FishMMO/Modules/Abilities/Templates/Events/Summon";

		private const string PathDeathDamage = "Assets/FishMMO/Templates/Entity/CharacterAttributes/Damage/Death Damage.asset";
		private const string PathMoveSpeed = "Assets/FishMMO/Templates/Entity/CharacterAttributes/Speed/Move Speed.asset";
		private const string PathHealthRegeneration = "Assets/FishMMO/Templates/Entity/CharacterAttributes/Regeneration/Health Regeneration.asset";
		private const string PathAreaPulse = AreaPrefabFolder + "/Area Pulse.prefab";

		private static readonly Palette ShadowPalette = new Palette(new Color(0.35f, 0.35f, 0.55f), new Color(0.75f, 0.80f, 0.95f));
		private static readonly Palette SightPalette = new Palette(new Color(0.95f, 0.75f, 0.20f), new Color(1.0f, 1.0f, 0.90f));
		private static readonly Palette DrainPalette = new Palette(new Color(0.55f, 0.05f, 0.15f), new Color(0.75f, 0.30f, 0.85f));
		private static readonly Palette WardPalette = new Palette(new Color(0.30f, 0.45f, 1.0f), new Color(0.80f, 0.90f, 1.0f));
		private static readonly Palette ProvokePalette = new Palette(new Color(0.85f, 0.10f, 0.05f), new Color(1.0f, 0.65f, 0.20f));
		private static readonly Palette BeamPalette = new Palette(new Color(1.0f, 0.45f, 0.10f), new Color(1.0f, 0.95f, 0.75f));

		public const string BeamAbilityFolder = "Assets/FishMMO/Modules/Abilities/Templates/Types/Beam";
		public const string BeamEventFolder = "Assets/FishMMO/Modules/Abilities/Templates/Events/Hit/Beam";

		// Searing Beam: held, a pulse every 0.2 s, 2 fire and 2 mana a pulse (10 damage and 10 mana a second), up to 5 s.
		private const float BeamPulse = 0.2f, BeamRange = 25f, BeamLongestHold = 5f;
		private const int BeamDamagePerPulse = 2, BeamManaPerPulse = 2;

		private const float InvisibleSeconds = 60f, SeeInvisibleSeconds = 60f;
		private const float SwipeRadius = 3.5f, SwipeArc = 120f, BackstabArc = 120f;
		private const int StabDamage = 5, BackstabBonus = 10;

		/// <summary>Premades with crafted effects (GeneratePremades makes the plain ones), merged in by the caller.</summary>
		private static readonly List<PremadeAbilityTemplate> extraPremades = new List<PremadeAbilityTemplate>();
		private const int SwipeMaxHits = 5;

		/// <summary>
		/// Health Regeneration is authored per five seconds (CharacterAttributeController's REGEN_AUTHORING_WINDOW_SECONDS),
		/// so 10 is 2 a second.
		/// </summary>
		private const int PetRegenerationPerFiveSeconds = 10;

		[DashboardTool(DashboardToolAttribute.Maintenance, "Generate Skirmish Abilities", Section = "Ability Content", Order = 4,
			Tooltip = "Generates Invisibility, See Invisible, Backstab, Swipe, Disrupting Strike, Hamstring, Provoke, Arcane Ward, Piercing Arrow, Crossbow Shot, Drain Life and the Pet Regeneration summon effect, registers them and adds them to the General Goods merchant. Touches nothing else.")]
		public static void GenerateSkirmishSuiteOnly()
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
				extraPremades.Clear();
				GenerateSkirmishSuite(abilities, events);
				List<PremadeAbilityTemplate> premades = GeneratePremades(abilities);
				premades.AddRange(extraPremades);
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

		private static void GenerateSkirmishSuite(List<AbilityTemplate> abilities, List<AbilityEvent> events)
		{
			foreach (string folder in new[] { StealthAbilityFolder, MeleeAbilityFolder, UtilityAbilityFolder, ProjectileAbilityFolder,
				StealthEventFolder, MeleeEventFolder, UtilityEventFolder, ProjectileEventFolder, SummonEventFolder, ProjectilePrefabFolder,
				BeamAbilityFolder, BeamEventFolder,
				DestroyEventFolder, BuffFolder })
			{
				EnsureFolder(folder);
			}

			DamageAttributeTemplate deathDamage = Require<DamageAttributeTemplate>(PathDeathDamage);
			CharacterAttributeTemplate moveSpeed = Require<CharacterAttributeTemplate>(PathMoveSpeed);
			CharacterAttributeTemplate healthRegeneration = Require<CharacterAttributeTemplate>(PathHealthRegeneration);
			GameObject punchPrefab = Require<GameObject>(PathPunchPrefab);
			GameObject areaPulse = Require<GameObject>(PathAreaPulse);
			if (problems.Count > 0)
			{
				throw new InvalidOperationException("Missing dependencies:\n" + string.Join("\n", problems));
			}

			// ── Effects ─────────────────────────────────────────────────────────────────────────
			GameObject shadowPuff = BuildEffect(explosionFX, "Shadow Puff", go => Recolour(go, ShadowPalette));
			GameObject sightFlash = BuildEffect(explosionFX, "Sight Flash", go => Recolour(go, SightPalette));
			GameObject swipeArc = BuildEffect(explosionFX, "Swipe Arc", go => { Recolour(go, SteelPalette); MakeGroundRing(go); });
			GameObject strikeSparks = BuildEffect(explosionFX, "Strike Sparks", go => Recolour(go, SteelPalette));
			GameObject wardFlash = BuildEffect(explosionFX, "Ward Flash", go => Recolour(go, WardPalette));
			GameObject arrowBody = BuildEffect(fireFX, "Arrow", go => Recolour(go, SteelPalette));
			GameObject drainBolt = BuildEffect(fireFX, "Drain Bolt", go => Recolour(go, DrainPalette));
			GameObject drainBurst = BuildEffect(explosionFX, "Drain Burst", go => Recolour(go, DrainPalette));
			GameObject provokeBurst = BuildEffect(explosionFX, "Provoke Burst", go => Recolour(go, ProvokePalette));
			GameObject beamSpark = BuildEffect(explosionFX, "Searing Beam Spark", go => Recolour(go, BeamPalette));
			// Lines drawn between two points (LightningArc): the beam, the crossbow's tracer, the drain's stream.
			GameObject beamLine = BuildArcLine("Searing Beam Line", new Color(1f, 0.95f, 0.75f, 1f), new Color(1f, 0.45f, 0.1f, 0.85f), 0.2f, 0.12f, 0.025f, 0.26f);
			GameObject tracer = BuildArcLine("Crossbow Tracer", new Color(1f, 1f, 1f, 0.9f), new Color(0.8f, 0.85f, 0.95f, 0.3f), 0.035f, 0.02f, 0f, 0.12f);
			GameObject drainStream = BuildArcLine("Drain Stream", new Color(0.65f, 0.05f, 0.2f, 1f), new Color(0.75f, 0.35f, 0.9f, 0.9f), 0.07f, 0.12f, 0.08f, 0.45f);

			// Worn for as long as the buff lasts.
			GameObject invisibility = BuildWornEffect("Invisibility Shimmer", fireFX, ShadowPalette, new[] { HumanBodyBones.Chest }, 0.6f,
				Vector3.zero, keepUpright: true, worldSpace: false, ring: true, rate: 6f, rise: 0.05f);
			AddToPrefab<FXConcealment>(invisibility);
			GameObject seeInvisible = BuildWornEffect("See Invisible Glow", fireFX, SightPalette, new[] { HumanBodyBones.Head }, 0.35f,
				new Vector3(0f, 0.05f, 0f), keepUpright: true, worldSpace: false, ring: true, rate: 10f, rise: 0f);
			AddToPrefab<FXSeeInvisible>(seeInvisible);
			GameObject hamstrung = BuildWornEffect("Hamstrung", fireFX, DrainPalette, new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot }, 0.3f,
				Vector3.zero, keepUpright: true, worldSpace: false, ring: true, rate: 14f, rise: 0f);
			GameObject wardBubble = BuildWornEffect("Arcane Ward Bubble", explosionFX, WardPalette, new[] { HumanBodyBones.Chest }, 1.6f,
				Vector3.zero, keepUpright: true, worldSpace: false, ring: true, rate: 20f, rise: 0f);
			GameObject petRegen = BuildWornEffect("Pet Regeneration", fireFX, HealPalette, new[] { HumanBodyBones.Chest }, 0.5f,
				Vector3.zero, keepUpright: true, worldSpace: true, ring: false, rate: 5f, rise: 0.8f);

			// ── Ability objects ─────────────────────────────────────────────────────────────────
			GameObject arrowProjectile = BuildFireballLikeProjectile(arrowBody, "Arrow", $"{ProjectilePrefabFolder}/Piercing Arrow Projectile.prefab");
			GameObject drainProjectile = BuildFireballLikeProjectile(drainBolt, "Drain Bolt", $"{ProjectilePrefabFolder}/Drain Life Projectile.prefab");

			// ── Buffs ───────────────────────────────────────────────────────────────────────────
			StateBuffTemplate invisible = ScriptableObject.CreateInstance<StateBuffTemplate>();
			invisible.Description = "Invisible: unseen by creatures that are not looking for you. Ends when you act.";
			invisible.Duration = InvisibleSeconds;
			invisible.MaxStacks = 1u;
			invisible.Flag = CharacterFlags.IsInvisible;
			invisible.BreaksOnAction = true;
			invisible.FXPrefabReference = Reference(invisibility);
			invisible = Save(invisible, BuffFolder, "Invisible");

			StateBuffTemplate seesInvisible = ScriptableObject.CreateInstance<StateBuffTemplate>();
			seesInvisible.Description = "See Invisible: invisible creatures and characters are plain to you.";
			seesInvisible.Duration = SeeInvisibleSeconds;
			seesInvisible.MaxStacks = 1u;
			seesInvisible.Flag = CharacterFlags.SeesInvisible;
			seesInvisible.FXPrefabReference = Reference(seeInvisible);
			seesInvisible = Save(seesInvisible, BuffFolder, "See Invisible");

			AttributeBuffTemplate hamstring = ScriptableObject.CreateInstance<AttributeBuffTemplate>();
			hamstring.Description = "Hamstrung: 40% slower.";
			hamstring.Duration = 6f;
			hamstring.IsDebuff = true;
			hamstring.MaxStacks = 1u;
			hamstring.BonusAttributes = new List<BuffAttributeTemplate> { new BuffAttributeTemplate { Template = moveSpeed, Value = -40 } };
			hamstring.FXPrefabReference = Reference(hamstrung);
			hamstring = Save(hamstring, BuffFolder, "Hamstrung");

			DamageNegationBuffTemplate ward = ScriptableObject.CreateInstance<DamageNegationBuffTemplate>();
			ward.Description = "Arcane Ward: absorbs 30 damage from any direction.";
			ward.Duration = 20f;
			ward.MaxStacks = 1u;
			ward.Mode = DamageNegationMode.Absorb;
			ward.Amount = 30;
			ward.RequiresFacing = false;
			ward.FXPrefabReference = Reference(wardBubble);
			ward = Save(ward, BuffFolder, "Arcane Ward");

			AttributeBuffTemplate regeneration = ScriptableObject.CreateInstance<AttributeBuffTemplate>();
			regeneration.Description = "Pet Regeneration: 2 health a second, for as long as it lives.";
			regeneration.IsPermanent = true;
			regeneration.MaxStacks = 1u;
			regeneration.BonusAttributes = new List<BuffAttributeTemplate>
			{
				new BuffAttributeTemplate { Template = healthRegeneration, Value = PetRegenerationPerFiveSeconds },
			};
			regeneration.FXPrefabReference = Reference(petRegen);
			regeneration = Save(regeneration, BuffFolder, "Pet Regeneration");

			// ── Pet Regeneration (an effect crafted onto a summon) ─────────────────────────────
			AbilityOnSummonEvent petRegeneration = SaveEvent<AbilityOnSummonEvent>(SummonEventFolder, "Pet Regeneration", ev =>
			{
				// No selector: the summon event's target is the pet (PetSystem.InvokeSummonEvents).
				ev.OnConditionsMetActions.Add(Buff(regeneration));
			});
			events.Add(petRegeneration);

			// ── Invisibility and See Invisible ─────────────────────────────────────────────────
			AbilityOnHitEvent vanish = SaveEvent<AbilityOnHitEvent>(StealthEventFolder, "Invisibility Event", ev =>
			{
				ev.TargetSelector = new InitiatorTargetSelector();
				ev.OnConditionsMetActions.Add(Buff(invisible));
				ev.OnConditionsMetActions.Add(new PlayFXAction { FXPrefab = shadowPuff, Size = 1.6f, HeightOffset = 1f });
			});
			abilities.Add(SaveAbility(StealthAbilityFolder, "Invisibility", a =>
			{
				a.Description = $"Fade from sight for {InvisibleSeconds:0} seconds: creatures do not notice you, and others see only a shimmer. Attacking, casting or using an item ends it.";
				a.AbilitySpawnTarget = AbilitySpawnTarget.Self;
				a.Type = AbilityType.Magic;
				a.ActivationTime = 1.5f;
				a.Cooldown = 30f;
				a.HitCount = 1;
				a.ActivationConditions.Add(Cost(mana, 15));
				a.OnHitEvents.Add(vanish);
			}));
			events.Add(vanish);

			AbilityOnHitEvent sight = SaveEvent<AbilityOnHitEvent>(StealthEventFolder, "See Invisible Event", ev =>
			{
				ev.TargetSelector = new InitiatorTargetSelector();
				ev.OnConditionsMetActions.Add(Buff(seesInvisible));
				ev.OnConditionsMetActions.Add(new PlayFXAction { FXPrefab = sightFlash, Size = 1.2f, HeightOffset = 1.6f });
			});
			abilities.Add(SaveAbility(StealthAbilityFolder, "See Invisible", a =>
			{
				a.Description = $"For {SeeInvisibleSeconds:0} seconds you see invisible creatures and characters, faint but plain, with their names.";
				a.AbilitySpawnTarget = AbilitySpawnTarget.Self;
				a.Type = AbilityType.Magic;
				a.ActivationTime = 1.0f;
				a.Cooldown = 30f;
				a.HitCount = 1;
				a.ActivationConditions.Add(Cost(mana, 10));
				a.OnHitEvents.Add(sight);
			}));
			events.Add(sight);

			// ── Stab, and the Backstab effect (Jim, 2026-11-22: the position is an effect, the attack is an ability) ────
			AbilityOnHitEvent stab = SaveEvent<AbilityOnHitEvent>(MeleeEventFolder, "Stab Impact", ev =>
			{
				ev.TargetSelector = Hostiles(new EventTargetSelector { FallbackToInitiator = false });
				ev.OnConditionsMetActions.Add(Damage(StabDamage, physicalDamage));
				ev.OnConditionsMetActions.Add(new PlayFXAction { FXPrefab = strikeSparks, Size = 0.5f, HeightOffset = 1.1f });
			});
			/* The positional half, as a crafting effect: on any ability that strikes, the struck foe takes BackstabBonus more
			 * when the attacker stands behind it. Nothing at all otherwise. */
			AbilityOnHitEvent backstab = SaveEvent<AbilityOnHitEvent>(StealthEventFolder, "Backstab", ev =>
			{
				EventTargetSelector selector = Hostiles(new EventTargetSelector { FallbackToInitiator = false });
				selector.Conditions.Add(new IsBehindTargetCondition { ArcDegrees = BackstabArc });
				ev.TargetSelector = selector;
				ev.OnConditionsMetActions.Add(Damage(BackstabBonus, physicalDamage));
				ev.OnConditionsMetActions.Add(new PlayFXAction { FXPrefab = strikeSparks, Size = 0.8f, HeightOffset = 1.1f });
			});
			AbilityTemplate stabAbility = SaveAbility(MeleeAbilityFolder, "Stab", a =>
			{
				a.Description = $"A quick thrust: {StabDamage} physical damage. Craft Backstab onto it for {BackstabBonus} more from behind.";
				a.AbilityObjectPrefab = punchPrefab;
				a.AbilitySpawnTarget = AbilitySpawnTarget.Forward;
				a.Type = AbilityType.GroundedPhysical;
				a.ActivationTime = 0f;
				a.Speed = 0f;
				a.LifeTime = 0.05f;
				a.Cooldown = 4f;
				a.HitCount = 1;
				a.AdditionalEventSlots = 1; // room for Backstab (or any other effect)
				a.ActivationConditions.Add(Cost(stamina, 10));
				a.OnHitEvents.Add(stab);
			});
			abilities.Add(stabAbility);
			events.Add(stab);
			events.Add(backstab);
			// "Backstab", ready made: Stab with the effect crafted on (the merchant's one-step route).
			PremadeAbilityTemplate premadeBackstab = ScriptableObject.CreateInstance<PremadeAbilityTemplate>();
			premadeBackstab.Ability = stabAbility;
			premadeBackstab.Events = new List<AbilityEvent> { backstab };
			premadeBackstab.Price = PremadePrice;
			if (!premadeBackstab.Validate(out string backstabReason))
			{
				problems.Add($"Premade Backstab: {backstabReason}");
			}
			extraPremades.Add(Save(premadeBackstab, PremadeFolder, "Premade Backstab"));

			// ── Swipe (a frontal cone) ──────────────────────────────────────────────────────────
			AbilityOnSpawnEvent swipeBurst = SaveEvent<AbilityOnSpawnEvent>(AreaSpawnEventFolder, "Swipe Burst", ev =>
			{
				ev.OnConditionsMetActions.Add(new PlayFXAction { FXPrefab = swipeArc, Size = SwipeRadius * 2f, HeightOffset = 0.9f });
				ev.OnConditionsMetActions.Add(new AbilityApplyAreaAction
				{
					RadiusValue = new ConstantFloatValue { Amount = SwipeRadius },
					MaxHitsValue = new ConstantValue { Amount = SwipeMaxHits },
					TargetLayerMask = ~0,
					ArcDegrees = SwipeArc,
				});
			});
			AbilityOnHitEvent swipeHit = SaveEvent<AbilityOnHitEvent>(MeleeEventFolder, "Swipe Strike", ev =>
			{
				ev.TargetSelector = Hostiles(new EventTargetSelector { FallbackToInitiator = false });
				ev.OnConditionsMetActions.Add(Damage(5, physicalDamage));
				ev.OnConditionsMetActions.Add(new PlayFXAction { FXPrefab = strikeSparks, Size = 0.6f, HeightOffset = 1.1f });
			});
			abilities.Add(SaveAbility(MeleeAbilityFolder, "Swipe", a =>
			{
				a.Description = $"A wide sweep: 5 physical damage to up to {SwipeMaxHits} foes in a {SwipeArc:0}° arc in front of you, {SwipeRadius:0.#} m deep.";
				a.AbilityObjectPrefab = areaPulse;
				a.AbilitySpawnTarget = AbilitySpawnTarget.PointBlank;
				a.Type = AbilityType.GroundedPhysical;
				a.ActivationTime = 0.3f;
				a.Speed = 0f;
				a.LifeTime = 0.5f;
				a.Cooldown = 5f;
				a.HitCount = 1;
				a.AdditionalEventSlots = 1;
				a.ActivationConditions.Add(Cost(stamina, 12));
				a.OnSpawnEvents.Add(swipeBurst);
				a.OnHitEvents.Add(swipeHit);
			}));
			events.Add(swipeBurst);
			events.Add(swipeHit);

			// ── Disrupting Strike, Hamstring ────────────────────────────────────────────────────
			/* Bases and effects (Jim, 2026-11-22: players craft abilities from bases and effects). The rider of each of these
			 * is an EFFECT asset — Interrupt, Hamstring — bundled on its own base here, sold on its own, and craftable onto any
			 * base with a free slot. The damage is the base's (Light Strike Impact). */
			AbilityOnHitEvent lightStrike = SaveEvent<AbilityOnHitEvent>(MeleeEventFolder, "Light Strike Impact", ev =>
			{
				ev.TargetSelector = Hostiles(new EventTargetSelector { FallbackToInitiator = false });
				ev.OnConditionsMetActions.Add(Damage(2, physicalDamage));
				ev.OnConditionsMetActions.Add(new PlayFXAction { FXPrefab = strikeSparks, Size = 0.5f, HeightOffset = 1.1f });
			});
			AbilityOnHitEvent interruptEffect = SaveEvent<AbilityOnHitEvent>(MeleeEventFolder, "Interrupt", ev =>
			{
				ev.TargetSelector = Hostiles(new EventTargetSelector { FallbackToInitiator = false });
				ev.OnConditionsMetActions.Add(new InterruptAction());
				ev.OnConditionsMetActions.Add(new PlayFXAction { FXPrefab = strikeSparks, Size = 0.8f, HeightOffset = 1.5f });
			});
			AbilityOnHitEvent hamstringEffect = SaveEvent<AbilityOnHitEvent>(MeleeEventFolder, "Hamstring", ev =>
			{
				ev.TargetSelector = Hostiles(new EventTargetSelector { FallbackToInitiator = false });
				ev.OnConditionsMetActions.Add(Buff(hamstring));
				ev.OnConditionsMetActions.Add(new PlayFXAction { FXPrefab = drainBurst, Size = 0.6f, HeightOffset = 0.2f });
			});
			abilities.Add(Melee("Disrupting Strike", "A sharp blow to the throat: 2 physical damage, and whatever the foe was casting is broken off.", 10, 10f, lightStrike, interruptEffect));
			abilities.Add(Melee("Hamstring", "A cut behind the knee: 2 physical damage, and the foe moves 40% slower for 6 seconds.", 10, 8f, lightStrike, hamstringEffect));
			events.Add(lightStrike);
			events.Add(interruptEffect);
			events.Add(hamstringEffect);

			// ── Provoke (taunt) ─────────────────────────────────────────────────────────────────
			AbilityOnHitEvent provoke = SaveEvent<AbilityOnHitEvent>(UtilityEventFolder, "Provoke Event", ev =>
			{
				ev.TargetSelector = Hostiles(new TargetedEntitySelector { MaximumRange = 20f });
				ev.OnConditionsMetActions.Add(new ApplyTauntAction());
				ev.OnConditionsMetActions.Add(new PlayFXAction { FXPrefab = provokeBurst, Size = 1.4f, HeightOffset = 1.8f });
			});
			abilities.Add(SaveAbility(UtilityAbilityFolder, "Provoke", a =>
			{
				a.Description = "Bellow a challenge at your target: a creature within 20 m turns on you, whoever it was fighting.";
				a.AbilitySpawnTarget = AbilitySpawnTarget.Self;
				a.RequiresTarget = true;
				a.Type = AbilityType.Physical;
				a.ActivationTime = 0f;
				// Not a travel time: Speed x LifeTime is the reach a targeted cast is checked against (20 m).
				a.Speed = 20f;
				a.LifeTime = 1f;
				a.Cooldown = 12f;
				a.HitCount = 1;
				a.ActivationConditions.Add(Cost(stamina, 8));
				a.OnHitEvents.Add(provoke);
			}));
			events.Add(provoke);

			// ── Arcane Ward (absorb) ────────────────────────────────────────────────────────────
			AbilityOnHitEvent wardUp = SaveEvent<AbilityOnHitEvent>(UtilityEventFolder, "Arcane Ward Event", ev =>
			{
				ev.TargetSelector = new InitiatorTargetSelector();
				ev.OnConditionsMetActions.Add(Buff(ward));
				ev.OnConditionsMetActions.Add(new PlayFXAction { FXPrefab = wardFlash, Size = 1.8f, HeightOffset = 1f });
			});
			abilities.Add(SaveAbility(UtilityAbilityFolder, "Arcane Ward", a =>
			{
				a.Description = "A shell of force that absorbs the next 30 damage from any direction, for up to 20 seconds.";
				a.AbilitySpawnTarget = AbilitySpawnTarget.Self;
				a.Type = AbilityType.Magic;
				a.ActivationTime = 0.5f;
				a.Cooldown = 30f;
				a.HitCount = 1;
				a.ActivationConditions.Add(Cost(mana, 15));
				a.OnHitEvents.Add(wardUp);
			}));
			events.Add(wardUp);

			// ── Piercing Arrow ─────────────────────────────────────────────────────────────────
			AbilityOnHitEvent arrowHit = SaveEvent<AbilityOnHitEvent>(ProjectileEventFolder, "Piercing Arrow Impact", ev =>
			{
				ev.TargetSelector = Hostiles(new EventTargetSelector { FallbackToInitiator = false });
				ev.OnConditionsMetActions.Add(Damage(4, physicalDamage));
				ev.OnConditionsMetActions.Add(new PlayFXAction { FXPrefab = strikeSparks, Size = 0.6f });
			});
			abilities.Add(SaveAbility(ProjectileAbilityFolder, "Piercing Arrow", a =>
			{
				a.Description = "An arrow that does not stop: 4 physical damage to each of the first three bodies in its path. A wall stops it.";
				a.AbilityObjectPrefab = arrowProjectile;
				a.AbilitySpawnTarget = AbilitySpawnTarget.SpawnerWithCameraRotation;
				a.Type = AbilityType.GroundedPhysical;
				a.ActivationTime = 0.6f;
				a.Speed = 24f;
				a.LifeTime = 1.25f; // 30 m
				a.Cooldown = 4f;
				a.HitCount = 3;
				a.AdditionalEventSlots = 1;
				a.ActivationConditions.Add(Cost(stamina, 8));
				a.OnTickEvents.Add(projectileMove);
				a.OnHitEvents.Add(arrowHit);
			}));
			events.Add(arrowHit);

			// ── Crossbow Shot (hitscan) ────────────────────────────────────────────────────────
			AbilityOnSpawnEvent bolt = SaveEvent<AbilityOnSpawnEvent>(ProjectileEventFolder, "Crossbow Shot Event", ev =>
			{
				ev.OnConditionsMetActions.Add(new AbilityApplyHitscanAction
				{
					RangeValue = new ConstantFloatValue { Amount = 30f },
					MaxHitsValue = new ConstantValue { Amount = 1 },
					TargetLayerMask = ~0,
					BlockedByScenery = true,
				});
				ev.OnConditionsMetActions.Add(new PlayBeamFXAction { BeamPrefab = tracer, Range = 30f, Seconds = 0.12f });
			});
			AbilityOnHitEvent boltHit = SaveEvent<AbilityOnHitEvent>(ProjectileEventFolder, "Crossbow Shot Impact", ev =>
			{
				ev.TargetSelector = Hostiles(new EventTargetSelector { FallbackToInitiator = false });
				ev.OnConditionsMetActions.Add(Damage(6, physicalDamage));
				ev.OnConditionsMetActions.Add(new PlayFXAction { FXPrefab = strikeSparks, Size = 0.7f });
			});
			abilities.Add(SaveAbility(ProjectileAbilityFolder, "Crossbow Shot", a =>
			{
				a.Description = "A bolt that arrives as it leaves: 6 physical damage to the first body within 30 m along your aim. Walls stop it.";
				// The invisible point, not a fist: the shot is the hitscan, and a fist would also strike whoever stood at arm's length.
				a.AbilityObjectPrefab = areaPulse;
				a.AbilitySpawnTarget = AbilitySpawnTarget.SpawnerWithCameraRotation;
				a.Type = AbilityType.GroundedPhysical;
				a.ActivationTime = 0.5f;
				// The shot is the hitscan (30 m); the point object does not travel (Speed 0) and lives only to be drawn.
				a.Speed = 0f;
				a.LifeTime = 0.1f;
				a.Cooldown = 3f;
				a.HitCount = 1;
				a.AdditionalEventSlots = 1;
				a.ActivationConditions.Add(Cost(stamina, 8));
				a.OnSpawnEvents.Add(bolt);
				a.OnHitEvents.Add(boltHit);
			}));
			events.Add(bolt);
			events.Add(boltHit);

			// ── Drain Life ─────────────────────────────────────────────────────────────────────
			AbilityOnHitEvent drain = SaveEvent<AbilityOnHitEvent>(ProjectileEventFolder, "Drain Life Impact", ev =>
			{
				ev.TargetSelector = Hostiles(new EventTargetSelector { FallbackToInitiator = false });
				ev.OnConditionsMetActions.Add(new ApplyDamageAction { DamageValue = new ConstantValue { Amount = 5 }, DamageAttributeTemplate = deathDamage });
			});
			/* The feeding half as an effect: whatever base it is crafted onto heals its caster 5 for each foe it strikes. The
			 * event's selector picks the struck foe (so only a hostile hit feeds it); the heal's own selector points it home. */
			AbilityOnHitEvent lifesteal = SaveEvent<AbilityOnHitEvent>(ProjectileEventFolder, "Lifesteal", ev =>
			{
				ev.TargetSelector = Hostiles(new EventTargetSelector { FallbackToInitiator = false });
				ev.OnConditionsMetActions.Add(new ApplyHealAction
				{
					HealValue = new ConstantValue { Amount = 5 },
					TargetSelector = new InitiatorTargetSelector(),
				});
				// Health seen running home: a stream from the foe back to the caster, on every client.
				ev.OnConditionsMetActions.Add(new PlayLinkFXAction { LinkPrefab = drainStream });
			});
			AbilityOnDestroyEvent drainFX = SaveEvent<AbilityOnDestroyEvent>(DestroyEventFolder, "Drain Life FX Event", ev =>
			{
				ev.OnConditionsMetActions.Add(new PlayFXAction { FXPrefab = drainBurst, Size = 1.2f });
			});
			abilities.Add(SaveAbility(ProjectileAbilityFolder, "Drain Life", a =>
			{
				a.Description = "A bolt that feeds on its target: 5 death damage to the foe it strikes, and you regain 5 health.";
				a.AbilityObjectPrefab = drainProjectile;
				a.AbilitySpawnTarget = AbilitySpawnTarget.SpawnerWithCameraRotation;
				a.Type = AbilityType.GroundedMagic;
				a.ActivationTime = 1.0f;
				a.Speed = 16f;
				a.LifeTime = 1.5f; // 24 m
				a.Cooldown = 8f;
				a.HitCount = 1;
				a.ActivationConditions.Add(Cost(mana, 12));
				a.OnTickEvents.Add(projectileMove);
				a.AdditionalEventSlots = 1;
				a.OnHitEvents.Add(drain);
				a.OnHitEvents.Add(lifesteal);
				a.OnDestroyEvents.Add(drainFX);
			}));
			events.Add(drain);
			events.Add(lifesteal);
			events.Add(drainFX);

			// ── Searing Beam (held, pulsed) ───────────────────────────────────────────────────
			AbilityOnSpawnEvent beamPulse = SaveEvent<AbilityOnSpawnEvent>(BeamEventFolder, "Searing Beam Pulse", ev =>
			{
				ev.OnConditionsMetActions.Add(new AbilityApplyHitscanAction
				{
					RangeValue = new ConstantFloatValue { Amount = BeamRange },
					MaxHitsValue = new ConstantValue { Amount = 1 },
					TargetLayerMask = ~0,
					BlockedByScenery = true,
				});
				// Redrawn every pulse, a little longer than one, so the beam reads as steady.
				ev.OnConditionsMetActions.Add(new PlayBeamFXAction { BeamPrefab = beamLine, Range = BeamRange, Seconds = BeamPulse + 0.06f });
			});
			AbilityOnHitEvent beamHit = SaveEvent<AbilityOnHitEvent>(BeamEventFolder, "Searing Beam Burn", ev =>
			{
				ev.TargetSelector = Hostiles(new EventTargetSelector { FallbackToInitiator = false });
				ev.OnConditionsMetActions.Add(Damage(BeamDamagePerPulse, fireDamage));
				ev.OnConditionsMetActions.Add(new PlayFXAction { FXPrefab = beamSpark, Size = 0.7f });
			});
			abilities.Add(SaveAbility(BeamAbilityFolder, "Searing Beam", a =>
			{
				a.Description = $"Hold to pour a beam of fire along your aim: {BeamDamagePerPulse * 5} fire damage a second to the first foe it touches, " +
					$"costing {BeamManaPerPulse * 5} mana a second, for up to {BeamLongestHold:0} seconds. It ends when you let go or your mana runs dry.";
				a.AbilityObjectPrefab = areaPulse; // an invisible point at the hand; the beam is the hitscan and the line
				a.AbilitySpawnTarget = AbilitySpawnTarget.SpawnerWithCameraRotation;
				a.Type = AbilityType.GroundedMagic;
				a.PulseSeconds = BeamPulse;
				a.ActivationTime = BeamLongestHold; // the longest hold
				// The point object does not travel (Speed 0): the reach is the hitscan's. It lives only long enough to be drawn.
				a.Speed = 0f;
				a.LifeTime = 0.1f;
				a.Cooldown = 3f;
				a.HitCount = 1;
				a.AdditionalEventSlots = 1;
				a.ActivationConditions.Add(Cost(mana, BeamManaPerPulse)); // paid every pulse
				a.OnSpawnEvents.Add(beamPulse);
				a.OnHitEvents.Add(beamHit);
			}));
			events.Add(beamPulse);
			events.Add(beamHit);

			AbilityTemplate Melee(string name, string description, int staminaCost, float cooldown, params AbilityOnHitEvent[] hits)
			{
				return SaveAbility(MeleeAbilityFolder, name, a =>
				{
					a.Description = description;
					a.AbilityObjectPrefab = punchPrefab;
					a.AbilitySpawnTarget = AbilitySpawnTarget.Forward;
					a.Type = AbilityType.GroundedPhysical;
					a.ActivationTime = 0.2f;
					a.Speed = 0f;
					a.LifeTime = 0.05f;
					a.Cooldown = cooldown;
					a.HitCount = 1;
					a.AdditionalEventSlots = 1; // a base: room for one crafted effect
					a.ActivationConditions.Add(Cost(stamina, staminaCost));
					a.OnHitEvents.AddRange(hits);
				});
			}
		}

		/// <summary>Adds <typeparamref name="T"/> to a saved prefab's root (an effect's behaviour), saving it again.</summary>
		private static void AddToPrefab<T>(GameObject prefab) where T : Component
		{
			string path = AssetDatabase.GetAssetPath(prefab);
			GameObject root = PrefabUtility.LoadPrefabContents(path);
			try
			{
				if (root.GetComponent<T>() == null)
				{
					root.AddComponent<T>();
				}
				PrefabUtility.SaveAsPrefabAsset(root, path);
			}
			finally
			{
				PrefabUtility.UnloadPrefabContents(root);
			}
		}
	}
}
