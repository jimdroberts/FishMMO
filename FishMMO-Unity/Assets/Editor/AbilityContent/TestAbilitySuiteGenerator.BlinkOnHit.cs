using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.AbilityContent
{
	/// <summary>
	/// The "Blink On Hit" components and two abilities built on them.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Components</b> (Events/Hit/Blink): Blink On Hit - In Front / Behind / Left / Right / Above, each a
	/// <see cref="BlinkToHitTargetAction"/> on the character struck (alive, friend or foe — what the ability's other events
	/// do to it is theirs to filter), with the teleport's burst where the caster leaves and the inward burst where it lands.
	/// Sold on General Goods as crafting components.
	/// </para>
	/// <para>
	/// <b>Shadowstep</b>: an arcane bolt (25 m) that strikes for 4 physical and puts the caster at its target's back. 12 mana,
	/// 12 s. <b>Blink Bolt</b>: the same bolt for 2, with one free event slot: craft any Blink On Hit component into it at an
	/// Ability Crafter to make your own (a bolt that lands you over a foe's head, at an ally's side...). 10 mana, 10 s.
	/// </para>
	/// </remarks>
	public static partial class TestAbilitySuiteGenerator
	{
		public const string BlinkAbilityFolder = "Assets/Templates/Entity/Abilities/Types/Blink";
		public const string BlinkPrefabFolder = "Assets/Prefabs/Shared/Entity/Abilities/Types/Blink";
		public const string BlinkEventFolder = "Assets/Templates/Entity/Abilities/Events/Hit/Blink";

		private static void GenerateBlinkOnHit(List<AbilityTemplate> abilities, List<AbilityEvent> events)
		{
			foreach (string folder in new[] { BlinkAbilityFolder, BlinkPrefabFolder, BlinkEventFolder })
			{
				EnsureFolder(folder);
			}

			// The teleport's two bursts (made by GenerateMovementAndControl, which runs first).
			GameObject depart = Require<GameObject>($"{FXFolder}/Teleport Out.prefab");
			GameObject arrive = Require<GameObject>($"{FXFolder}/Teleport In.prefab");
			GameObject arcaneBolt = BuildEffect(fireFX, "Arcane Bolt", go => Recolour(go, ArcanePalette));
			GameObject boltProjectile = BuildFireballLikeProjectile(arcaneBolt, "Arcane Bolt", $"{BlinkPrefabFolder}/Arcane Bolt Projectile.prefab");

			// ── The five components ────────────────────────────────────────────────────────────
			Dictionary<BlinkPlacement, AbilityOnHitEvent> blinks = new Dictionary<BlinkPlacement, AbilityOnHitEvent>();
			foreach ((BlinkPlacement placement, string label) in new[]
			{
				(BlinkPlacement.InFront, "In Front"), (BlinkPlacement.Behind, "Behind"), (BlinkPlacement.Left, "Left"),
				(BlinkPlacement.Right, "Right"), (BlinkPlacement.Above, "Above"),
			})
			{
				AbilityOnHitEvent blink = SaveEvent<AbilityOnHitEvent>(BlinkEventFolder, $"Blink On Hit - {label}", ev =>
				{
					EventTargetSelector selector = new EventTargetSelector { FallbackToInitiator = false };
					selector.Conditions.Add(new IsCharacterAliveCondition());
					ev.TargetSelector = selector;
					ev.OnConditionsMetActions.Add(new BlinkToHitTargetAction
					{
						Placement = placement,
						Frame = BlinkFrame.TargetFacing,
						Distance = placement == BlinkPlacement.Above ? 0.4f : 1.2f,
						DepartFX = depart,
						ArriveFX = arrive,
						FXSize = 2f,
						FXHeight = 1f,
					});
				});
				blinks[placement] = blink;
				events.Add(blink);
			}

			// ── Shadowstep and Blink Bolt ──────────────────────────────────────────────────────
			AbilityOnHitEvent shadowstepStrike = SaveEvent<AbilityOnHitEvent>(BlinkEventFolder, "Shadowstep Strike", ev =>
			{
				ev.TargetSelector = Hostiles(new EventTargetSelector { FallbackToInitiator = false });
				ev.OnConditionsMetActions.Add(Damage(4, physicalDamage));
			});
			AbilityOnHitEvent boltImpact = SaveEvent<AbilityOnHitEvent>(BlinkEventFolder, "Blink Bolt Impact", ev =>
			{
				ev.TargetSelector = Hostiles(new EventTargetSelector { FallbackToInitiator = false });
				ev.OnConditionsMetActions.Add(Damage(2, physicalDamage));
			});
			AbilityOnDestroyEvent boltFX = SaveEvent<AbilityOnDestroyEvent>(DestroyEventFolder, "Arcane Impact FX Event", ev =>
			{
				ev.OnConditionsMetActions.Add(new PlayFXAction { FXPrefab = depart, Size = 1f });
			});
			events.Add(shadowstepStrike);
			events.Add(boltImpact);
			events.Add(boltFX);

			abilities.Add(SaveAbility(BlinkAbilityFolder, "Shadowstep", a =>
			{
				a.Description = "Hurls an arcane bolt. The first thing it strikes takes 4 damage, and you step out of the air at its back.";
				ArcaneBolt(a, boltProjectile);
				a.Cooldown = 12f;
				a.ActivationConditions.Add(Cost(mana, 12));
				a.OnHitEvents.Add(shadowstepStrike);
				a.OnHitEvents.Add(blinks[BlinkPlacement.Behind]);
				a.OnDestroyEvents.Add(boltFX);
			}));

			abilities.Add(SaveAbility(BlinkAbilityFolder, "Blink Bolt", a =>
			{
				a.Description = "An arcane bolt that strikes for 2 damage. It has a free slot: craft a Blink On Hit component into it " +
					"at an Ability Crafter to choose where it puts you (in front of, behind, beside or above what it strikes).";
				ArcaneBolt(a, boltProjectile);
				a.AdditionalEventSlots = 1;
				a.Cooldown = 10f;
				a.ActivationConditions.Add(Cost(mana, 10));
				a.OnHitEvents.Add(boltImpact);
				a.OnDestroyEvents.Add(boltFX);
			}));
		}

		/// <summary>The bolt both blink abilities fly: 20 m/s for 1.25 s (25 m), a half-second cast.</summary>
		private static void ArcaneBolt(AbilityTemplate a, GameObject projectile)
		{
			a.AbilityObjectPrefab = projectile;
			a.AbilitySpawnTarget = AbilitySpawnTarget.SpawnerWithCameraRotation;
			a.Type = AbilityType.GroundedMagic;
			a.ActivationTime = 0.5f;
			a.Speed = 20f;
			a.LifeTime = 1.25f;
			a.HitCount = 1;
			a.OnTickEvents.Add(projectileMove);
		}
	}
}
