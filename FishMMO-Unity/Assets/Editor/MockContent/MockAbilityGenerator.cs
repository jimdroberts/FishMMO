using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.MockContent
{
	/// <summary>
	/// Generates the "Mock " ability / ability-event / buff content set used to exercise every
	/// authored ability shape the combat and prediction systems support.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Written as a generator rather than hand-authored YAML because ECA content is serialized with
	/// <c>[SerializeReference]</c>, whose <c>references: version: 2 / RefIds:</c> block carries
	/// arbitrary long ids that Unity alone assigns correctly.
	/// </para>
	/// <para>
	/// Run from the menu (<c>FishMMO/Mock Content/Generate Mock Abilities</c>) or headlessly with
	/// <c>-executeMethod FishMMO.MockContent.MockAbilityGenerator.GenerateAll</c>.
	/// </para>
	/// <para>
	/// Everything it writes lives under <see cref="AbilityFolder"/>, <see cref="EventFolder"/> and
	/// <see cref="BuffFolder"/> and is named with a "Mock " prefix, so the whole set can be deleted
	/// by removing those three folders.
	/// </para>
	/// </remarks>
	public static class MockAbilityGenerator
	{
		public const string AbilityFolder = "Assets/Templates/Entity/Abilities/Mock";
		public const string EventFolder = "Assets/Templates/Entity/Abilities/Events/Mock";
		public const string BuffFolder = "Assets/Templates/Entity/Buffs/Mock";

		private const string AddressableGroupName = "Shared_Static_Permanent";

		// ---------------------------------------------------------------------------------------
		// Existing project assets reused by reference rather than duplicated.
		// ---------------------------------------------------------------------------------------
		private const string PathPhysicalDamage = "Assets/Templates/Entity/CharacterAttributes/Damage/Physical Damage.asset";
		private const string PathFireDamage = "Assets/Templates/Entity/CharacterAttributes/Damage/Fire Damage.asset";
		private const string PathHealth = "Assets/Templates/Entity/CharacterAttributes/Resource/Health.asset";
		private const string PathMana = "Assets/Templates/Entity/CharacterAttributes/Resource/Mana.asset";
		private const string PathStamina = "Assets/Templates/Entity/CharacterAttributes/Resource/Stamina.asset";
		private const string PathArmor = "Assets/Templates/Entity/CharacterAttributes/Resistance/Armor.asset";
		private const string PathMoveSpeed = "Assets/Templates/Entity/CharacterAttributes/Speed/Move Speed.asset";

		private const string PathFireballPrefab = "Assets/Prefabs/Shared/Entity/Abilities/Types/Fireball/Lesser Fireball.prefab";
		private const string PathFlamePrefab = "Assets/Prefabs/Shared/Entity/Abilities/Types/Flame/Flame.prefab";
		private const string PathPunchPrefab = "Assets/Prefabs/Shared/Entity/Abilities/Types/Punch/Punch.prefab";

		/// <summary>Layer ability objects live on (see the ability prefabs' m_Layer).</summary>
		private const int AbilityObjectLayer = 9;

		private static readonly List<string> created = new List<string>();
		private static readonly List<string> problems = new List<string>();

		private static DamageAttributeTemplate physicalDamage;
		private static DamageAttributeTemplate fireDamage;
		private static CharacterAttributeTemplate health;
		private static CharacterAttributeTemplate mana;
		private static CharacterAttributeTemplate stamina;
		private static CharacterAttributeTemplate armor;
		private static CharacterAttributeTemplate moveSpeed;

		private static GameObject fireballPrefab;
		private static GameObject flamePrefab;
		private static GameObject punchPrefab;

		[MenuItem("FishMMO/Mock Content/Generate Mock Abilities")]
		public static void GenerateAll()
		{
			created.Clear();
			problems.Clear();

			try
			{
				EnsureFolder(AbilityFolder);
				EnsureFolder(EventFolder);
				EnsureFolder(BuffFolder);

				LoadDependencies();

				/* Deliberately NOT wrapped in StartAssetEditing/StopAssetEditing: each ability
				 * references event assets created moments earlier in the same pass, and the
				 * existence check in Save() reads the database back. Both want imports to have
				 * actually happened. */
				GenerateBuffs(out var buffs);
				GenerateEvents(buffs, out var events);
				GenerateAbilities(events);

				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();

				Debug.Log($"[MockAbilityGenerator] SUCCESS — created/updated {created.Count} assets.");
				for (int i = 0; i < created.Count; ++i)
				{
					Debug.Log($"[MockAbilityGenerator] asset: {created[i]}");
				}
				for (int i = 0; i < problems.Count; ++i)
				{
					Debug.LogWarning($"[MockAbilityGenerator] PROBLEM: {problems[i]}");
				}
				Debug.Log($"[MockAbilityGenerator] DONE assets={created.Count} problems={problems.Count}");
			}
			catch (Exception ex)
			{
				Debug.LogError($"[MockAbilityGenerator] FAILED: {ex}");
				throw;
			}
		}

		/// <summary>
		/// Opt-in: registers every generated mock asset as an Addressable in the
		/// <see cref="AddressableGroupName"/> group, which is what makes templates reachable to the
		/// runtime template cache (<c>ServerLauncher</c> / <c>ClientPostbootSystem</c> call
		/// <c>ICachedObject.AddToCache</c> from the addressable load callback). Deliberately NOT run
		/// by <see cref="GenerateAll"/> because it modifies a shared settings asset.
		/// </summary>
		[MenuItem("FishMMO/Mock Content/Register Mock Abilities As Addressables")]
		public static void RegisterAddressables()
		{
			var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null)
			{
				Debug.LogError("[MockAbilityGenerator] No Addressable settings found.");
				return;
			}

			var group = settings.FindGroup(AddressableGroupName);
			if (group == null)
			{
				Debug.LogError($"[MockAbilityGenerator] Addressable group '{AddressableGroupName}' not found.");
				return;
			}

			int count = 0;
			foreach (string guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { AbilityFolder, EventFolder, BuffFolder }))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				var entry = settings.CreateOrMoveEntry(guid, group, false, false);
				if (entry == null)
				{
					continue;
				}
				entry.address = Path.GetFileNameWithoutExtension(path);
				entry.SetLabel(AddressableGroupName, true, true, false);
				++count;
			}

			AssetDatabase.SaveAssets();
			Debug.Log($"[MockAbilityGenerator] Registered {count} mock assets as addressables in '{AddressableGroupName}'.");
		}

		[MenuItem("FishMMO/Mock Content/Delete Mock Abilities")]
		public static void DeleteAll()
		{
			foreach (string folder in new[] { AbilityFolder, EventFolder, BuffFolder })
			{
				if (AssetDatabase.IsValidFolder(folder))
				{
					AssetDatabase.DeleteAsset(folder);
				}
			}
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
			Debug.Log("[MockAbilityGenerator] Deleted mock content folders.");
		}

		// =======================================================================================
		// Dependencies
		// =======================================================================================

		private static void LoadDependencies()
		{
			physicalDamage = Require<DamageAttributeTemplate>(PathPhysicalDamage);
			fireDamage = Require<DamageAttributeTemplate>(PathFireDamage);
			health = Require<CharacterAttributeTemplate>(PathHealth);
			mana = Require<CharacterAttributeTemplate>(PathMana);
			stamina = Require<CharacterAttributeTemplate>(PathStamina);
			armor = Require<CharacterAttributeTemplate>(PathArmor);
			moveSpeed = Require<CharacterAttributeTemplate>(PathMoveSpeed);

			fireballPrefab = Require<GameObject>(PathFireballPrefab);
			flamePrefab = Require<GameObject>(PathFlamePrefab);
			punchPrefab = Require<GameObject>(PathPunchPrefab);
		}

		private static T Require<T>(string path) where T : UnityEngine.Object
		{
			T asset = AssetDatabase.LoadAssetAtPath<T>(path);
			if (asset == null)
			{
				problems.Add($"Missing dependency {typeof(T).Name} at '{path}'.");
			}
			return asset;
		}

		// =======================================================================================
		// Buffs
		// =======================================================================================

		private sealed class Buffs
		{
			public BaseBuffTemplate DamageOverTime;
			public BaseBuffTemplate HealOverTime;
			public BaseBuffTemplate MinorArmor;
			public BaseBuffTemplate Slow;
			public BaseBuffTemplate Absorb;
			public BaseBuffTemplate BlockGuard;
			public BaseBuffTemplate Deflect;
			public BaseBuffTemplate AttributeTick;
			public BaseBuffTemplate Stun;
		}

		private static void GenerateBuffs(out Buffs buffs)
		{
			buffs = new Buffs();

			// Damage over time — resource tick, negative health, fire typed so resistance applies.
			var dot = ScriptableObject.CreateInstance<ResourceTickBuffTemplate>();
			dot.Description = "Mock damage over time. 3 health per second for 6 seconds.";
			dot.Duration = 6f;
			dot.TickRate = 1f;
			dot.MaxStacks = 3u;
			dot.IsDebuff = true;
			dot.DamageAttribute = fireDamage;
			dot.TickAttributes = new List<BuffAttributeTemplate>
			{
				new BuffAttributeTemplate { Template = health, Value = -3 },
			};
			buffs.DamageOverTime = Save(dot, BuffFolder, "Mock Damage Over Time Buff");

			// Heal over time — resource tick, positive health.
			var hot = ScriptableObject.CreateInstance<ResourceTickBuffTemplate>();
			hot.Description = "Mock heal over time. 4 health per second for 8 seconds.";
			hot.Duration = 8f;
			hot.TickRate = 1f;
			hot.MaxStacks = 3u;
			hot.TickAttributes = new List<BuffAttributeTemplate>
			{
				new BuffAttributeTemplate { Template = health, Value = 4 },
			};
			buffs.HealOverTime = Save(hot, BuffFolder, "Mock Heal Over Time Buff");

			// Flat attribute buff (self buff target).
			var armorBuff = ScriptableObject.CreateInstance<AttributeBuffTemplate>();
			armorBuff.Description = "Mock armor buff. +5 armor for 60 seconds.";
			armorBuff.Duration = 60f;
			armorBuff.BonusAttributes = new List<BuffAttributeTemplate>
			{
				new BuffAttributeTemplate { Template = armor, Value = 5 },
			};
			buffs.MinorArmor = Save(armorBuff, BuffFolder, "Mock Minor Armor Buff");

			// Flat attribute DEBUFF (cross-character target).
			var slow = ScriptableObject.CreateInstance<AttributeBuffTemplate>();
			slow.Description = "Mock slow debuff. -2 move speed for 10 seconds.";
			slow.Duration = 10f;
			slow.IsDebuff = true;
			slow.BonusAttributes = new List<BuffAttributeTemplate>
			{
				new BuffAttributeTemplate { Template = moveSpeed, Value = -2 },
			};
			buffs.Slow = Save(slow, BuffFolder, "Mock Slow Debuff");

			// Absorb shield — spends RemainingCharges, so it exercises the reconciled-charge path.
			var absorb = ScriptableObject.CreateInstance<DamageNegationBuffTemplate>();
			absorb.Description = "Mock absorb shield. Absorbs 40 damage from any direction.";
			absorb.Duration = 15f;
			absorb.Mode = DamageNegationMode.Absorb;
			absorb.Amount = 40;
			absorb.RequiresFacing = false;
			buffs.Absorb = Save(absorb, BuffFolder, "Mock Absorb Shield Buff");

			// Block guard — percentage reduction, facing gated, with a real shield VOLUME so the
			// volume-block path (DamageMitigation.TryBlockAtVolume) has content.
			var guard = ScriptableObject.CreateInstance<DamageNegationBuffTemplate>();
			guard.Description = "Mock block guard. 50% off frontal damage while raised.";
			guard.Duration = 2f;
			guard.Mode = DamageNegationMode.Reduce;
			guard.Amount = 50;
			guard.RequiresFacing = true;
			guard.FacingAngleDegrees = 120f;
			guard.VolumeBlockCost = 0;
			guard.Shield = new ShieldVolume
			{
				Shape = ShieldShape.Box,
				LocalCenter = new Vector3(0f, 1.1f, 0.75f),
				Size = new Vector3(1.2f, 1.4f, 0.2f),
				Radius = 0.6f,
				Height = 1.6f,
			};
			buffs.BlockGuard = Save(guard, BuffFolder, "Mock Block Guard Buff");

			// Deflect.
			var deflect = ScriptableObject.CreateInstance<DeflectBuffTemplate>();
			deflect.Description = "Mock deflect stance. Deflects up to 3 frontal projectiles.";
			deflect.Duration = 6f;
			deflect.DeflectAngleDegrees = 120f;
			deflect.MaxDeflections = 3;
			buffs.Deflect = Save(deflect, BuffFolder, "Mock Deflect Stance Buff");

			// Attribute tick buff (distinct from the resource tick used for DoT/HoT).
			var attributeTick = ScriptableObject.CreateInstance<AttributeTickBuffTemplate>();
			attributeTick.Description = "Mock ramping armor. +1 armor every 2 seconds for 10 seconds.";
			attributeTick.Duration = 10f;
			attributeTick.TickRate = 2f;
			attributeTick.TickAttributes = new List<BuffAttributeTemplate>
			{
				new BuffAttributeTemplate { Template = armor, Value = 1 },
			};
			buffs.AttributeTick = Save(attributeTick, BuffFolder, "Mock Attribute Tick Armor Buff");

			// Crowd control state buff.
			var stun = ScriptableObject.CreateInstance<StateBuffTemplate>();
			stun.Description = "Mock stun. Sets IsStunned for 2 seconds.";
			stun.Duration = 2f;
			stun.IsDebuff = true;
			stun.Flag = CharacterFlags.IsStunned;
			buffs.Stun = Save(stun, BuffFolder, "Mock Stun Buff");
		}

		// =======================================================================================
		// Ability events
		// =======================================================================================

		private sealed class Events
		{
			public AbilityOnHitEvent SingleTargetDamage;
			public AbilityOnHitEvent ProjectileImpactDamage;
			public AbilityOnHitEvent AreaDamage;
			public AbilityOnHitEvent ConeDamage;
			public AbilityOnHitEvent LineDamage;
			public AbilityOnHitEvent ChainDamage;
			public AbilityOnHitEvent RandomDamage;
			public AbilityOnHitEvent NearestDamage;
			public AbilityOnHitEvent FurthestDamage;
			public AbilityOnHitEvent DirectHeal;
			public AbilityOnHitEvent AreaHeal;
			public AbilityOnHitEvent ApplyDamageOverTime;
			public AbilityOnHitEvent ApplyHealOverTime;
			public AbilityOnHitEvent ApplyAbsorb;
			public AbilityOnHitEvent ApplyDeflect;
			public AbilityOnHitEvent SelfArmorBuff;
			public AbilityOnHitEvent DebuffSlow;
			public AbilityOnHitEvent ApplyStun;
			public AbilityOnHitEvent Knockback;
			public AbilityOnHitEvent Fork;
			public AbilityOnHitEvent Pierce;
			public AbilityOnHitEvent Taunt;
			public AbilityOnHitEvent ThreatPulse;
			public AbilityOnHitEvent Interrupt;
			public AbilityOnHitEvent Dispel;
			public AbilityOnHitEvent Cleanse;
			public AbilityOnHitEvent ConsumableSelfHeal;

			public AbilityOnTickEvent ProjectileMove;
			public AbilityOnTickEvent AreaPulse;

			public AbilityOnSpawnEvent BlockRaise;
			public AbilityOnSpawnEvent SpawnMultiply;
			public AbilityOnSpawnEvent HitscanBullet;
			public AbilityOnSpawnEvent HitscanBeam;
			public AbilityOnSpawnEvent ChannelMarker;
			public AbilityOnSpawnEvent ChargeMarker;

			public AbilityOnPreSpawnEvent PreSpawnCost;
			public AbilityOnDestroyEvent DestroyFX;
		}

		private static void GenerateEvents(Buffs buffs, out Events events)
		{
			events = new Events();

			// --- 1. Single-target direct damage -------------------------------------------------
			events.SingleTargetDamage = SaveEvent<AbilityOnHitEvent>("Mock Single Target Damage Event", e =>
			{
				e.TargetSelector = Enemies(new TargetedEntitySelector { MaximumRange = 25f, RequireLineOfSight = false });
				e.OnConditionsMetActions.Add(Damage(6, physicalDamage));
			});

			// --- 2. Projectile impact damage ----------------------------------------------------
			events.ProjectileImpactDamage = SaveEvent<AbilityOnHitEvent>("Mock Projectile Impact Damage Event", e =>
			{
				e.TargetSelector = new EventTargetSelector { FallbackToInitiator = false };
				e.OnConditionsMetActions.Add(Damage(4, fireDamage));
				e.OnConditionsMetActions.Add(new PlayFXAction { FXPrefab = flamePrefab });
			});

			// --- 3. Point-blank area damage (capped) --------------------------------------------
			events.AreaDamage = SaveEvent<AbilityOnHitEvent>("Mock Area Damage Event", e =>
			{
				e.TargetSelector = Enemies(new AreaTargetSelector { Radius = 6f, MaxHits = 4, TargetLayer = ~0 });
				e.OnConditionsMetActions.Add(Damage(3, fireDamage));
			});

			// --- 4. Cone damage -----------------------------------------------------------------
			events.ConeDamage = SaveEvent<AbilityOnHitEvent>("Mock Cone Damage Event", e =>
			{
				e.TargetSelector = Enemies(new ConeTargetSelector { Radius = 7f, Angle = 60f, MaxHits = 5, TargetLayer = ~0 });
				e.OnConditionsMetActions.Add(Damage(4, physicalDamage));
			});

			// --- 5. Line / beam damage ----------------------------------------------------------
			events.LineDamage = SaveEvent<AbilityOnHitEvent>("Mock Line Damage Event", e =>
			{
				e.TargetSelector = Enemies(new LineTargetSelector { Length = 12f, MaxHits = 4, TargetLayer = ~0 });
				e.OnConditionsMetActions.Add(Damage(4, physicalDamage));
			});

			// --- 6. Chain / bouncing damage -----------------------------------------------------
			events.ChainDamage = SaveEvent<AbilityOnHitEvent>("Mock Chain Damage Event", e =>
			{
				e.TargetSelector = Enemies(new ChainTargetSelector { ChainLength = 4, ChainRadius = 6f, TargetLayer = ~0, QueryBufferHint = 16 });
				e.OnConditionsMetActions.Add(Damage(3, fireDamage));
			});

			// --- 7. Random target damage — the deterministic-RNG value provider case -------------
			events.RandomDamage = SaveEvent<AbilityOnHitEvent>("Mock Random Target Damage Event", e =>
			{
				e.TargetSelector = Enemies(new RandomTargetSelector { Radius = 10f, MaxHits = 3, TargetLayer = ~0 });
				e.OnConditionsMetActions.Add(new ApplyDamageAction
				{
					DamageValue = new RandomRangeValue { Min = 2, Max = 6 },
					DamageAttributeTemplate = fireDamage,
				});
			});

			// --- 8. Nearest / Furthest ----------------------------------------------------------
			events.NearestDamage = SaveEvent<AbilityOnHitEvent>("Mock Nearest Target Damage Event", e =>
			{
				e.TargetSelector = Enemies(new NearestTargetSelector { Radius = 12f, TargetLayer = ~0, QueryBufferHint = 16 });
				e.OnConditionsMetActions.Add(Damage(5, physicalDamage));
			});

			events.FurthestDamage = SaveEvent<AbilityOnHitEvent>("Mock Furthest Target Damage Event", e =>
			{
				e.TargetSelector = Enemies(new FurthestTargetSelector { Radius = 18f, TargetLayer = ~0, QueryBufferHint = 16 });
				e.OnConditionsMetActions.Add(Damage(5, physicalDamage));
			});

			// --- 9. Heals -----------------------------------------------------------------------
			events.DirectHeal = SaveEvent<AbilityOnHitEvent>("Mock Direct Heal Event", e =>
			{
				e.TargetSelector = Friends(new TargetedEntitySelector { MaximumRange = 25f });
				e.OnConditionsMetActions.Add(new ApplyHealAction { HealValue = new ConstantValue { Amount = 8 } });
			});

			events.AreaHeal = SaveEvent<AbilityOnHitEvent>("Mock Area Heal Event", e =>
			{
				e.TargetSelector = Friends(new AreaTargetSelector { Radius = 6f, MaxHits = 5, TargetLayer = ~0 });
				e.OnConditionsMetActions.Add(new ApplyHealAction { HealValue = new ConstantValue { Amount = 5 } });
			});

			// --- 10. DoT / HoT ------------------------------------------------------------------
			events.ApplyDamageOverTime = SaveEvent<AbilityOnHitEvent>("Mock Damage Over Time Event", e =>
			{
				e.TargetSelector = Enemies(new TargetedEntitySelector { MaximumRange = 25f });
				e.OnConditionsMetActions.Add(Buff(buffs.DamageOverTime, 1));
			});

			events.ApplyHealOverTime = SaveEvent<AbilityOnHitEvent>("Mock Heal Over Time Event", e =>
			{
				e.TargetSelector = new InitiatorTargetSelector();
				e.OnConditionsMetActions.Add(Buff(buffs.HealOverTime, 1));
			});

			// --- 11. Absorb / Deflect -----------------------------------------------------------
			events.ApplyAbsorb = SaveEvent<AbilityOnHitEvent>("Mock Absorb Shield Event", e =>
			{
				e.TargetSelector = new InitiatorTargetSelector();
				e.OnConditionsMetActions.Add(Buff(buffs.Absorb, 1));
			});

			events.ApplyDeflect = SaveEvent<AbilityOnHitEvent>("Mock Deflect Stance Event", e =>
			{
				e.TargetSelector = new InitiatorTargetSelector();
				e.OnConditionsMetActions.Add(Buff(buffs.Deflect, 1));
			});

			// --- 12. Block: raise the guard buff AND sweep incoming objects out of the air -------
			events.BlockRaise = SaveEvent<AbilityOnSpawnEvent>("Mock Block Raise Event", e =>
			{
				e.OnConditionsMetActions.Add(new ApplyBuffAction
				{
					TargetSelector = new InitiatorTargetSelector(),
					StacksValue = new ConstantValue { Amount = 1 },
					BuffTemplate = buffs.BlockGuard,
				});
				// Shape left at None so the sweep uses the volume the buff above already defines.
				e.OnConditionsMetActions.Add(new ShieldInterceptAction
				{
					Volume = new ShieldVolume { Shape = ShieldShape.None },
					InterceptLayers = 1 << AbilityObjectLayer,
					MaxIntercepts = 3,
				});
			});

			// --- 13. Knockback ------------------------------------------------------------------
			events.Knockback = SaveEvent<AbilityOnHitEvent>("Mock Knockback Impact Event", e =>
			{
				e.TargetSelector = new EventTargetSelector { FallbackToInitiator = false };
				e.OnConditionsMetActions.Add(Damage(2, physicalDamage));
				e.OnConditionsMetActions.Add(new KnockbackHitAction { ForceValue = new ConstantFloatValue { Amount = 8f } });
			});

			// --- 14. Fork / pierce / multiply / move --------------------------------------------
			// No TargetSelector: these act on the ability OBJECT, once per hit, not per target.
			events.Fork = SaveEvent<AbilityOnHitEvent>("Mock Fork On Hit Event", e =>
			{
				e.OnConditionsMetActions.Add(new AbilityForkHitAction { ArcValue = new ConstantFloatValue { Amount = 90f } });
			});

			events.Pierce = SaveEvent<AbilityOnHitEvent>("Mock Pierce On Hit Event", e =>
			{
				e.OnConditionsMetActions.Add(new AbilityHitCountAction { AmountValue = new ConstantValue { Amount = 1 } });
			});

			events.SpawnMultiply = SaveEvent<AbilityOnSpawnEvent>("Mock Spawn Multiply Event", e =>
			{
				e.OnConditionsMetActions.Add(new AbilitySpawnMultiplyAction { SpawnCountValue = new ConstantValue { Amount = 3 } });
			});

			events.ProjectileMove = SaveEvent<AbilityOnTickEvent>("Mock Projectile Move Event", e =>
			{
				e.OnConditionsMetActions.Add(new AbilityMoveTransformAction { MoveDirection = new Vector3(0f, 0f, 1f) });
			});

			// --- 15. Hitscan: bullet (instant) and beam (channelled) ----------------------------
			events.HitscanBullet = SaveEvent<AbilityOnSpawnEvent>("Mock Hitscan Bullet Event", e =>
			{
				e.OnConditionsMetActions.Add(new AbilityApplyHitscanAction
				{
					RangeValue = new ConstantFloatValue { Amount = 30f },
					MaxHitsValue = new ConstantValue { Amount = 1 },
					TargetLayerMask = ~0,
					BlockedByScenery = true,
				});
			});

			events.HitscanBeam = SaveEvent<AbilityOnSpawnEvent>("Mock Hitscan Beam Event", e =>
			{
				// Per-tick channel cost first, aborting the rest of the chain when it cannot be paid.
				e.OnConditionsMetActions.Add(new ConsumeResourceAction
				{
					ResourceTemplateID = TemplateID(mana),
					AmountValue = new ConstantValue { Amount = 1 },
					StopChainOnFailure = true,
				});
				// MaxHits 0 pierces everything on the line.
				e.OnConditionsMetActions.Add(new AbilityApplyHitscanAction
				{
					RangeValue = new ConstantFloatValue { Amount = 20f },
					MaxHitsValue = new ConstantValue { Amount = 0 },
					TargetLayerMask = ~0,
					BlockedByScenery = true,
				});
			});

			// --- 16. Area-apply action (re-runs the ability's OnHit set over a radius) -----------
			events.AreaPulse = SaveEvent<AbilityOnTickEvent>("Mock Area Pulse Tick Event", e =>
			{
				e.OnConditionsMetActions.Add(new AbilityApplyAreaAction
				{
					RadiusValue = new ConstantFloatValue { Amount = 5f },
					MaxHitsValue = new ConstantValue { Amount = 4 },
					TargetLayerMask = ~0,
				});
			});

			// --- 17. Self buff and cross-character debuff ---------------------------------------
			events.SelfArmorBuff = SaveEvent<AbilityOnHitEvent>("Mock Self Armor Buff Event", e =>
			{
				e.TargetSelector = new InitiatorTargetSelector();
				e.OnConditionsMetActions.Add(Buff(buffs.MinorArmor, 1));
			});

			events.DebuffSlow = SaveEvent<AbilityOnHitEvent>("Mock Debuff Slow Event", e =>
			{
				e.TargetSelector = Enemies(new TargetedEntitySelector { MaximumRange = 25f });
				e.OnConditionsMetActions.Add(Buff(buffs.Slow, 1));
			});

			events.ApplyStun = SaveEvent<AbilityOnHitEvent>("Mock Stun Event", e =>
			{
				e.TargetSelector = Enemies(new TargetedEntitySelector { MaximumRange = 20f });
				e.OnConditionsMetActions.Add(Buff(buffs.Stun, 1));
			});

			// --- 18. Consumable-style instant self heal ------------------------------------------
			events.ConsumableSelfHeal = SaveEvent<AbilityOnHitEvent>("Mock Consumable Self Heal Event", e =>
			{
				e.TargetSelector = new InitiatorTargetSelector();
				e.OnConditionsMetActions.Add(new ApplyHealAction { HealValue = new ConstantValue { Amount = 12 } });
			});

			// Channel / charge markers. These are EMPTY on purpose — a channelled or charged ability
			// is one that carries the event asset assigned to AbilityController.ChanneledTemplate /
			// ChargedTemplate, and those fields are currently unassigned on every character prefab.
			events.ChannelMarker = SaveEvent<AbilityOnSpawnEvent>("Mock Channel Marker Event", e => { });
			events.ChargeMarker = SaveEvent<AbilityOnSpawnEvent>("Mock Charge Marker Event", e => { });

			// --- 19. Taunt and threat -----------------------------------------------------------
			events.Taunt = SaveEvent<AbilityOnHitEvent>("Mock Taunt Event", e =>
			{
				e.TargetSelector = Enemies(new TargetedEntitySelector { MaximumRange = 30f });
				e.OnConditionsMetActions.Add(new ApplyTauntAction
				{
					ThreatPoints = 250f,
					GuaranteeTopThreat = true,
					LeadOverHighest = 50f,
					ForceImmediateTargetSwitch = true,
				});
			});

			events.ThreatPulse = SaveEvent<AbilityOnHitEvent>("Mock Threat Pulse Event", e =>
			{
				// No selector: ApplyThreatAction sweeps its own radius around the initiator.
				e.OnConditionsMetActions.Add(new ApplyThreatAction
				{
					Radius = 15f,
					NPCLayers = ~0,
					ThreatPoints = 100f,
					ResourceSpent = 0,
				});
			});

			// --- 20. Interrupt and dispel -------------------------------------------------------
			events.Interrupt = SaveEvent<AbilityOnHitEvent>("Mock Interrupt Event", e =>
			{
				e.TargetSelector = Enemies(new TargetedEntitySelector { MaximumRange = 25f });
				e.OnConditionsMetActions.Add(new InterruptAction());
			});

			events.Dispel = SaveEvent<AbilityOnHitEvent>("Mock Dispel Event", e =>
			{
				e.TargetSelector = Enemies(new TargetedEntitySelector { MaximumRange = 25f });
				e.OnConditionsMetActions.Add(new ApplyDispelAction
				{
					AmountToRemoveValue = new ConstantValue { Amount = 2 },
					IncludeBuffs = true,
					IncludeDebuffs = false,
				});
			});

			events.Cleanse = SaveEvent<AbilityOnHitEvent>("Mock Cleanse Event", e =>
			{
				e.TargetSelector = Friends(new TargetedEntitySelector { MaximumRange = 25f });
				e.OnConditionsMetActions.Add(new ApplyDispelAction
				{
					AmountToRemoveValue = new ConstantValue { Amount = 2 },
					IncludeBuffs = false,
					IncludeDebuffs = true,
				});
			});

			// --- Pre-spawn cost and destroy FX (the two remaining event shapes) -----------------
			events.PreSpawnCost = SaveEvent<AbilityOnPreSpawnEvent>("Mock Pre Spawn Cost Event", e =>
			{
				e.OnConditionsMetActions.Add(new ConsumeResourceAction
				{
					ResourceTemplateID = TemplateID(mana),
					AmountValue = new ConstantValue { Amount = 2 },
					StopChainOnFailure = true,
				});
			});

			events.DestroyFX = SaveEvent<AbilityOnDestroyEvent>("Mock Destroy FX Event", e =>
			{
				e.OnConditionsMetActions.Add(new PlayFXAction { FXPrefab = flamePrefab });
			});
		}

		// =======================================================================================
		// Abilities
		// =======================================================================================

		private static void GenerateAbilities(Events e)
		{
			// 1. Single-target direct damage.
			SaveAbility("Mock Single Target Strike", a =>
			{
				a.Description = "Mock: instant single-target strike resolved through TargetedEntitySelector.";
				a.AbilitySpawnTarget = AbilitySpawnTarget.Self;
				a.RequiresTarget = true;
				a.Type = AbilityType.Physical;
				/* Range = Speed x LifeTime; for non-spawning shapes these are inert to the
				 * runtime effect and exist so Ability.Range is honest - the AI holds THIS
				 * distance, and the mocks originally shipped Range 0 on every instant, which
				 * marched every fighter to point-blank. Cast times pace the burst. */
				a.Speed = 4f;
				a.LifeTime = 1f;
				a.Cooldown = 1.5f;
				a.HitCount = 1;
				a.ActivationConditions.Add(Cost(stamina, 3));
				a.OnHitEvents.Add(e.SingleTargetDamage);
			});

			// 2. Projectile damage.
			SaveAbility("Mock Fireball Projectile", a =>
			{
				a.Description = "Mock: travelling projectile with an OnHit damage event.";
				a.AbilityObjectPrefab = fireballPrefab;
				a.AbilitySpawnTarget = AbilitySpawnTarget.SpawnerWithCameraRotation;
				a.Type = AbilityType.Magic;
				a.ActivationTime = 0.4f;
				a.LifeTime = 3f;
				a.Speed = 14f;
				a.Cooldown = 2f;
				a.HitCount = 1;
				a.ActivationConditions.Add(Cost(mana, 5));
				a.OnTickEvents.Add(e.ProjectileMove);
				a.OnHitEvents.Add(e.ProjectileImpactDamage);
				a.OnDestroyEvents.Add(e.DestroyFX);
			});

			// 3. Point-blank AoE with a MaxHits cap.
			SaveAbility("Mock Point Blank Nova", a =>
			{
				a.Description = "Mock: point-blank area damage, capped at 4 targets.";
				a.AbilitySpawnTarget = AbilitySpawnTarget.Self;
				a.Type = AbilityType.Magic;
				a.Cooldown = 4f;
				a.HitCount = 1;
				a.ActivationConditions.Add(Cost(mana, 8));
				a.OnHitEvents.Add(e.AreaDamage);
			});

			// 4. Cone.
			SaveAbility("Mock Cone Blast", a =>
			{
				a.Description = "Mock: 60 degree cone damage, capped at 5 targets.";
				a.AbilitySpawnTarget = AbilitySpawnTarget.Self;
				a.Type = AbilityType.Magic;
				a.Speed = 8f;
				a.LifeTime = 1f;
				a.ActivationTime = 0.4f;
				a.Cooldown = 3f;
				a.HitCount = 1;
				a.ActivationConditions.Add(Cost(mana, 6));
				a.OnHitEvents.Add(e.ConeDamage);
			});

			// 5. Line / beam selector.
			SaveAbility("Mock Line Lance", a =>
			{
				a.Description = "Mock: 12m line damage, capped at 4 targets.";
				a.AbilitySpawnTarget = AbilitySpawnTarget.Self;
				a.Type = AbilityType.Magic;
				a.Speed = 15f;
				a.LifeTime = 1f;
				a.ActivationTime = 0.5f;
				a.Cooldown = 3f;
				a.HitCount = 1;
				a.ActivationConditions.Add(Cost(mana, 6));
				a.OnHitEvents.Add(e.LineDamage);
			});

			// 6. Chain.
			SaveAbility("Mock Chain Lightning", a =>
			{
				a.Description = "Mock: chains to 4 targets within 6m of each other.";
				a.AbilitySpawnTarget = AbilitySpawnTarget.Self;
				a.Type = AbilityType.Magic;
				a.Speed = 18f;
				a.LifeTime = 1f;
				a.ActivationTime = 0.8f;
				a.Cooldown = 5f;
				a.HitCount = 1;
				a.ActivationConditions.Add(Cost(mana, 7));
				a.OnHitEvents.Add(e.ChainDamage);
			});

			// 7. Random target (RandomRangeValue damage).
			SaveAbility("Mock Random Bolts", a =>
			{
				a.Description = "Mock: 3 random targets within 10m, 2-6 damage each (deterministic RNG).";
				a.AbilitySpawnTarget = AbilitySpawnTarget.Self;
				a.Type = AbilityType.Magic;
				a.Speed = 20f;
				a.LifeTime = 1f;
				a.ActivationTime = 0.6f;
				a.Cooldown = 3f;
				a.HitCount = 1;
				a.ActivationConditions.Add(Cost(mana, 5));
				a.OnHitEvents.Add(e.RandomDamage);
			});

			// 8. Nearest / furthest.
			SaveAbility("Mock Nearest Strike", a =>
			{
				a.Description = "Mock: hits the nearest valid enemy within 12m.";
				a.AbilitySpawnTarget = AbilitySpawnTarget.Self;
				a.Type = AbilityType.Physical;
				a.Speed = 5f;
				a.LifeTime = 1f;
				a.Cooldown = 2f;
				a.HitCount = 1;
				a.OnHitEvents.Add(e.NearestDamage);
			});

			SaveAbility("Mock Furthest Snipe", a =>
			{
				a.Description = "Mock: hits the furthest valid enemy within 18m.";
				a.AbilitySpawnTarget = AbilitySpawnTarget.Self;
				a.Type = AbilityType.Magic;
				a.Speed = 30f;
				a.LifeTime = 1f;
				a.ActivationTime = 1.2f;
				a.Cooldown = 4f;
				a.HitCount = 1;
				a.ActivationConditions.Add(Cost(mana, 4));
				a.OnHitEvents.Add(e.FurthestDamage);
			});

			// 9. Heals.
			SaveAbility("Mock Direct Heal", a =>
			{
				a.Description = "Mock: single-target heal.";
				a.AbilitySpawnTarget = AbilitySpawnTarget.Self;
				a.RequiresTarget = true;
				a.Type = AbilityType.Magic;
				a.ActivationTime = 1f;
				a.Speed = 20f;
				a.LifeTime = 1f;
				a.Cooldown = 2f;
				a.HitCount = 1;
				a.ActivationConditions.Add(Cost(mana, 6));
				a.OnHitEvents.Add(e.DirectHeal);
			});

			SaveAbility("Mock Area Heal", a =>
			{
				a.Description = "Mock: heals up to 5 allies within 6m.";
				a.AbilitySpawnTarget = AbilitySpawnTarget.Self;
				a.Type = AbilityType.Magic;
				a.ActivationTime = 1.5f;
				a.Cooldown = 6f;
				a.HitCount = 1;
				a.ActivationConditions.Add(Cost(mana, 10));
				a.OnHitEvents.Add(e.AreaHeal);
			});

			// 10. DoT / HoT.
			SaveAbility("Mock Damage Over Time", a =>
			{
				a.Description = "Mock: applies a 6 second damage-over-time debuff.";
				a.AbilitySpawnTarget = AbilitySpawnTarget.Self;
				a.RequiresTarget = true;
				a.Type = AbilityType.Magic;
				a.Speed = 15f;
				a.LifeTime = 1f;
				a.ActivationTime = 0.5f;
				a.Cooldown = 3f;
				a.HitCount = 1;
				a.ActivationConditions.Add(Cost(mana, 4));
				a.OnHitEvents.Add(e.ApplyDamageOverTime);
			});

			SaveAbility("Mock Heal Over Time", a =>
			{
				a.Description = "Mock: applies an 8 second heal-over-time buff to self.";
				a.AbilitySpawnTarget = AbilitySpawnTarget.Self;
				a.Type = AbilityType.Magic;
				a.ActivationTime = 0.8f;
				a.Cooldown = 5f;
				a.HitCount = 1;
				a.ActivationConditions.Add(Cost(mana, 4));
				a.OnHitEvents.Add(e.ApplyHealOverTime);
			});

			// 11. Absorb / deflect.
			SaveAbility("Mock Absorb Shield", a =>
			{
				a.Description = "Mock: absorbs the next 40 damage.";
				a.AbilitySpawnTarget = AbilitySpawnTarget.Self;
				a.Type = AbilityType.Magic;
				a.Cooldown = 10f;
				a.HitCount = 1;
				a.ActivationConditions.Add(Cost(mana, 8));
				a.OnHitEvents.Add(e.ApplyAbsorb);
			});

			SaveAbility("Mock Deflect Stance", a =>
			{
				a.Description = "Mock: deflects up to 3 frontal projectiles for 6 seconds.";
				a.AbilitySpawnTarget = AbilitySpawnTarget.Self;
				a.Type = AbilityType.Magic;
				a.Cooldown = 12f;
				a.HitCount = 1;
				a.OnHitEvents.Add(e.ApplyDeflect);
			});

			// 12. Block — ShieldInterceptAction on OnSpawn of a Block-typed ability.
			SaveAbility("Mock Shield Block", a =>
			{
				a.Description = "Mock: raises a guard buff and sweeps incoming ability objects out of the air.";
				a.AbilityObjectPrefab = punchPrefab;
				a.AbilitySpawnTarget = AbilitySpawnTarget.Forward;
				a.Type = AbilityType.Block;
				a.LifeTime = 0.5f;
				a.Cooldown = 1f;
				a.HitCount = 1;
				a.OnSpawnEvents.Add(e.BlockRaise);
			});

			// 13. Knockback.
			SaveAbility("Mock Knockback Projectile", a =>
			{
				a.Description = "Mock: projectile that knocks its target back on impact.";
				a.AbilityObjectPrefab = punchPrefab;
				a.AbilitySpawnTarget = AbilitySpawnTarget.SpawnerWithCameraRotation;
				a.Type = AbilityType.Physical;
				a.LifeTime = 1f;
				a.Speed = 18f;
				a.Cooldown = 4f;
				a.HitCount = 1;
				a.OnTickEvents.Add(e.ProjectileMove);
				a.OnHitEvents.Add(e.Knockback);
			});

			// 14. Fork / pierce / multiply.
			SaveAbility("Mock Forking Projectile", a =>
			{
				a.Description = "Mock: projectile that ricochets within a 90 degree arc, 3 hits.";
				a.AbilityObjectPrefab = fireballPrefab;
				a.AbilitySpawnTarget = AbilitySpawnTarget.SpawnerWithCameraRotation;
				a.Type = AbilityType.Magic;
				a.LifeTime = 4f;
				a.Speed = 12f;
				a.Cooldown = 5f;
				a.HitCount = 3;
				a.ActivationConditions.Add(Cost(mana, 7));
				a.OnTickEvents.Add(e.ProjectileMove);
				a.OnHitEvents.Add(e.ProjectileImpactDamage);
				a.OnHitEvents.Add(e.Fork);
			});

			SaveAbility("Mock Piercing Projectile", a =>
			{
				a.Description = "Mock: projectile that pierces every body it meets until its lifetime expires.";
				a.AbilityObjectPrefab = fireballPrefab;
				a.AbilitySpawnTarget = AbilitySpawnTarget.SpawnerWithCameraRotation;
				a.Type = AbilityType.Magic;
				a.LifeTime = 2f;
				a.Speed = 16f;
				a.Cooldown = 5f;
				a.HitCount = 1;
				a.ActivationConditions.Add(Cost(mana, 7));
				a.OnTickEvents.Add(e.ProjectileMove);
				a.OnHitEvents.Add(e.ProjectileImpactDamage);
				a.OnHitEvents.Add(e.Pierce);
			});

			SaveAbility("Mock Shotgun Projectile", a =>
			{
				a.Description = "Mock: pays a pre-spawn cost, then multiplies into 3 projectiles.";
				a.AbilityObjectPrefab = fireballPrefab;
				a.AbilitySpawnTarget = AbilitySpawnTarget.SpawnerWithCameraRotation;
				a.Type = AbilityType.Magic;
				a.LifeTime = 2f;
				a.Speed = 12f;
				a.Cooldown = 6f;
				a.HitCount = 1;
				a.OnPreSpawnEvents.Add(e.PreSpawnCost);
				a.OnSpawnEvents.Add(e.SpawnMultiply);
				a.OnTickEvents.Add(e.ProjectileMove);
				a.OnHitEvents.Add(e.ProjectileImpactDamage);
			});

			// 15. Hitscan bullet and beam.
			SaveAbility("Mock Hitscan Bullet", a =>
			{
				a.Description = "Mock: instant hitscan shot; the ray resolves on OnSpawn and stops at the first body.";
				a.AbilityObjectPrefab = punchPrefab;
				a.AbilitySpawnTarget = AbilitySpawnTarget.SpawnerWithCameraRotation;
				a.Type = AbilityType.Physical;
				a.LifeTime = 0.1f;
				a.Speed = 200f;
				a.ActivationTime = 0.3f;
				a.Cooldown = 2f;
				a.HitCount = 1;
				a.OnSpawnEvents.Add(e.HitscanBullet);
				a.OnHitEvents.Add(e.ProjectileImpactDamage);
			});

			SaveAbility("Mock Hitscan Beam", a =>
			{
				a.Description = "Mock: channelled beam. Re-spawns per tick and pierces everything on the line. " +
					"Requires AbilityController.ChanneledTemplate to point at 'Mock Channel Marker Event'.";
				a.AbilityObjectPrefab = flamePrefab;
				a.AbilitySpawnTarget = AbilitySpawnTarget.SpawnerWithCameraRotation;
				a.Type = AbilityType.Magic;
				a.ActivationTime = 2f;
				a.LifeTime = 0.2f;
				a.Speed = 100f;
				a.Cooldown = 3f;
				a.HitCount = 1;
				a.OnSpawnEvents.Add(e.ChannelMarker);
				a.OnSpawnEvents.Add(e.HitscanBeam);
				a.OnHitEvents.Add(e.ProjectileImpactDamage);
			});

			// 16. Area-apply action variant.
			SaveAbility("Mock Area Pulse Trap", a =>
			{
				a.Description = "Mock: stationary object that re-applies its OnHit set over a 5m radius every tick.";
				a.AbilityObjectPrefab = flamePrefab;
				a.AbilitySpawnTarget = AbilitySpawnTarget.PointBlank;
				a.Type = AbilityType.Magic;
				a.LifeTime = 5f;
				a.Speed = 0f;
				a.Cooldown = 12f;
				a.HitCount = 1;
				a.ActivationConditions.Add(Cost(mana, 10));
				a.OnTickEvents.Add(e.AreaPulse);
				a.OnHitEvents.Add(e.ProjectileImpactDamage);
			});

			// 17. Self buff and cross-character debuff.
			SaveAbility("Mock Self Armor Buff", a =>
			{
				a.Description = "Mock: self-targeted armor buff.";
				a.AbilitySpawnTarget = AbilitySpawnTarget.Self;
				a.Type = AbilityType.Magic;
				a.Cooldown = 30f;
				a.HitCount = 1;
				a.ActivationConditions.Add(Cost(mana, 3));
				a.OnHitEvents.Add(e.SelfArmorBuff);
			});

			SaveAbility("Mock Debuff Slow", a =>
			{
				a.Description = "Mock: CROSS-CHARACTER debuff. Applies a move speed penalty to the target.";
				a.AbilitySpawnTarget = AbilitySpawnTarget.Self;
				a.RequiresTarget = true;
				a.Type = AbilityType.Magic;
				a.Speed = 15f;
				a.LifeTime = 1f;
				a.ActivationTime = 0.5f;
				a.Cooldown = 8f;
				a.HitCount = 1;
				a.ActivationConditions.Add(Cost(mana, 4));
				a.OnHitEvents.Add(e.DebuffSlow);
			});

			SaveAbility("Mock Stun", a =>
			{
				a.Description = "Mock: cross-character crowd control. Sets IsStunned for 2 seconds.";
				a.AbilitySpawnTarget = AbilitySpawnTarget.Self;
				a.RequiresTarget = true;
				a.Type = AbilityType.Magic;
				a.Speed = 6f;
				a.LifeTime = 1f;
				a.ActivationTime = 0.3f;
				a.Cooldown = 12f;
				a.HitCount = 1;
				a.ActivationConditions.Add(Cost(mana, 6));
				a.OnHitEvents.Add(e.ApplyStun);
			});

			// 18. Consumable-style, channelled, charged.
			SaveAbility("Mock Consumable Minor Healing", a =>
			{
				a.Description = "Mock: instant self heal, the shape a potion/scroll consumable would invoke.";
				a.AbilitySpawnTarget = AbilitySpawnTarget.Self;
				a.Type = AbilityType.Magic;
				a.Cooldown = 5f;
				a.HitCount = 1;
				a.OnHitEvents.Add(e.ConsumableSelfHeal);
			});

			SaveAbility("Mock Channeled Flame", a =>
			{
				a.Description = "Mock: channelled ability. Spawns one object per tick for the channel's duration. " +
					"Requires AbilityController.ChanneledTemplate to point at 'Mock Channel Marker Event'.";
				a.AbilityObjectPrefab = flamePrefab;
				a.AbilitySpawnTarget = AbilitySpawnTarget.SpawnerWithCameraRotation;
				a.Type = AbilityType.Magic;
				a.ActivationTime = 3f;
				a.LifeTime = 0.3f;
				a.Speed = 6f;
				a.Cooldown = 4f;
				a.HitCount = 1;
				a.OnSpawnEvents.Add(e.ChannelMarker);
				a.OnTickEvents.Add(e.ProjectileMove);
				a.OnHitEvents.Add(e.ProjectileImpactDamage);
			});

			SaveAbility("Mock Charged Shot", a =>
			{
				a.Description = "Mock: charged/held ability. " +
					"Requires AbilityController.ChargedTemplate to point at 'Mock Charge Marker Event'.";
				a.AbilityObjectPrefab = fireballPrefab;
				a.AbilitySpawnTarget = AbilitySpawnTarget.SpawnerWithCameraRotation;
				a.Type = AbilityType.Magic;
				a.ActivationTime = 1.5f;
				a.LifeTime = 2f;
				a.Speed = 20f;
				a.Cooldown = 6f;
				a.HitCount = 1;
				a.ActivationConditions.Add(Cost(mana, 8));
				a.OnSpawnEvents.Add(e.ChargeMarker);
				a.OnTickEvents.Add(e.ProjectileMove);
				a.OnHitEvents.Add(e.ProjectileImpactDamage);
			});

			// 19. Taunt and threat.
			SaveAbility("Mock Taunt", a =>
			{
				a.Description = "Mock: forces the target NPC's threat onto the caster.";
				a.AbilitySpawnTarget = AbilitySpawnTarget.Self;
				a.RequiresTarget = true;
				a.Type = AbilityType.Physical;
				a.Speed = 10f;
				a.LifeTime = 1f;
				a.Cooldown = 8f;
				a.HitCount = 1;
				a.OnHitEvents.Add(e.Taunt);
			});

			SaveAbility("Mock Threat Pulse", a =>
			{
				a.Description = "Mock: adds threat to every hostile NPC within 15m.";
				a.AbilitySpawnTarget = AbilitySpawnTarget.Self;
				a.Type = AbilityType.Physical;
				a.Cooldown = 6f;
				a.HitCount = 1;
				a.ActivationConditions.Add(Cost(stamina, 5));
				a.OnHitEvents.Add(e.ThreatPulse);
			});

			// 20. Interrupt and dispel.
			SaveAbility("Mock Interrupt", a =>
			{
				a.Description = "Mock: interrupts the target's current cast.";
				a.AbilitySpawnTarget = AbilitySpawnTarget.Self;
				a.RequiresTarget = true;
				a.Type = AbilityType.Physical;
				a.Speed = 12f;
				a.LifeTime = 1f;
				a.Cooldown = 10f;
				a.HitCount = 1;
				a.ActivationConditions.Add(Cost(stamina, 4));
				a.OnHitEvents.Add(e.Interrupt);
			});

			SaveAbility("Mock Dispel", a =>
			{
				a.Description = "Mock: strips up to 2 beneficial buffs from an enemy.";
				a.AbilitySpawnTarget = AbilitySpawnTarget.Self;
				a.RequiresTarget = true;
				a.Type = AbilityType.Magic;
				a.Speed = 15f;
				a.LifeTime = 1f;
				a.ActivationTime = 0.5f;
				a.Cooldown = 6f;
				a.HitCount = 1;
				a.ActivationConditions.Add(Cost(mana, 5));
				a.OnHitEvents.Add(e.Dispel);
			});

			SaveAbility("Mock Cleanse", a =>
			{
				a.Description = "Mock: strips up to 2 debuffs from an ally.";
				a.AbilitySpawnTarget = AbilitySpawnTarget.Self;
				a.RequiresTarget = true;
				a.Type = AbilityType.Magic;
				a.ActivationTime = 0.5f;
				a.Cooldown = 6f;
				a.HitCount = 1;
				a.ActivationConditions.Add(Cost(mana, 5));
				a.OnHitEvents.Add(e.Cleanse);
			});
		}

		// =======================================================================================
		// Authoring helpers
		// =======================================================================================

		private static ApplyDamageAction Damage(int amount, DamageAttributeTemplate type)
		{
			return new ApplyDamageAction
			{
				DamageValue = new ConstantValue { Amount = amount },
				DamageAttributeTemplate = type,
			};
		}

		private static ApplyBuffAction Buff(BaseBuffTemplate template, int stacks)
		{
			return new ApplyBuffAction
			{
				StacksValue = new ConstantValue { Amount = stacks },
				BuffTemplate = template,
			};
		}

		private static HasResourceCondition Cost(CharacterAttributeTemplate template, int amount)
		{
			return new HasResourceCondition { Template = template, RequiredAmount = amount };
		}

		/// <summary>Adds an "enemies and alive only" per-candidate filter to a selector.</summary>
		private static T Enemies<T>(T selector) where T : TargetSelector
		{
			selector.Conditions.Add(new TargetAllianceCondition
			{
				ApplyToSelf = false,
				ApplyToEnemy = true,
				ApplyToNeutral = false,
				ApplyToAllies = false,
			});
			selector.Conditions.Add(new IsCharacterAliveCondition());
			return selector;
		}

		/// <summary>Adds a "self and allies, alive only" per-candidate filter to a selector.</summary>
		private static T Friends<T>(T selector) where T : TargetSelector
		{
			selector.Conditions.Add(new TargetAllianceCondition
			{
				ApplyToSelf = true,
				ApplyToEnemy = false,
				ApplyToNeutral = false,
				ApplyToAllies = true,
			});
			selector.Conditions.Add(new IsCharacterAliveCondition());
			return selector;
		}

		/// <summary>
		/// The deterministic id a <c>[TemplateReference]</c> int field holds — the same formula
		/// <c>CachedScriptableObject.AddToCache</c> and <c>TemplateReferenceDrawer</c> use.
		/// </summary>
		private static int TemplateID(ScriptableObject template)
		{
			if (template == null)
			{
				return 0;
			}
			return (template.GetType().Name + template.name).GetDeterministicHashCode();
		}

		private static T SaveEvent<T>(string assetName, Action<T> configure) where T : AbilityEvent
		{
			T instance = ScriptableObject.CreateInstance<T>();
			configure(instance);
			return Save(instance, EventFolder, assetName);
		}

		private static AbilityTemplate SaveAbility(string assetName, Action<AbilityTemplate> configure)
		{
			AbilityTemplate instance = ScriptableObject.CreateInstance<AbilityTemplate>();
			configure(instance);
			return Save(instance, AbilityFolder, assetName);
		}

		private static T Save<T>(T instance, string folder, string assetName) where T : ScriptableObject
		{
			string path = $"{folder}/{assetName}.asset";
			instance.name = assetName;

			/* Overwrite in place when an asset of the same type is already there, rather than
			 * delete-and-recreate. A recreate hands out a fresh GUID every run, which rewrites the
			 * .meta of every mock asset and breaks any addressable entry or external reference that
			 * already points at it. CopySerialized moves the whole serialized state — the
			 * [SerializeReference] graph included — onto the existing object. */
			ScriptableObject existing = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
			if (existing != null && existing.GetType() == typeof(T))
			{
				EditorUtility.CopySerialized(instance, existing);
				EditorUtility.SetDirty(existing);
				UnityEngine.Object.DestroyImmediate(instance);
				created.Add(path);
				return (T)existing;
			}

			if (existing != null)
			{
				AssetDatabase.DeleteAsset(path);
			}
			AssetDatabase.CreateAsset(instance, path);
			EditorUtility.SetDirty(instance);
			created.Add(path);
			return instance;
		}

		private static void EnsureFolder(string folder)
		{
			if (AssetDatabase.IsValidFolder(folder))
			{
				return;
			}
			Directory.CreateDirectory(folder);
			AssetDatabase.Refresh();
		}
	}
}
