using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.AbilityContent
{
	/// <summary>
	/// Dispel Magic and Self Dispel Magic, with the dispel's own burst.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Dispel Magic</b>: cast at your current target (25 m). On a foe it strips up to two beneficial effects; on a
	/// friend it lifts up to two harmful ones. Two hit events, each gated by alliance, so one spell serves both; the strip
	/// also answers to the central harm rule (<see cref="ApplyDispelAction"/>, FactionHarm), so a neutral player is never
	/// stripped. 6 mana, 6 s, half-second cast. It cannot be cast on yourself: the targeted selector never resolves onto
	/// its own caster.
	/// </para>
	/// <para>
	/// <b>Self Dispel Magic</b>: the same lift on yourself, needing no target. 6 mana, 6 s, half-second cast.
	/// </para>
	/// <para>
	/// <b>Dispel Burst</b> is the Explosion effect recoloured magenta to pearl — a hue no other ability effect uses — with
	/// its sparks set spinning about the target as they spread, so a dispel reads as magic being unwound rather than as a
	/// heal or a blast. Played on whoever was dispelled, at chest height.
	/// </para>
	/// <para>
	/// Part of <see cref="GenerateAll"/>, and runnable alone (<see cref="GenerateDispelOnly"/>) so the dispel content can be
	/// rebuilt without rewriting the rest of the suite.
	/// </para>
	/// </remarks>
	public static partial class TestAbilitySuiteGenerator
	{
		public const string DispelAbilityFolder = "Assets/Templates/Entity/Abilities/Types/Dispel";
		public const string DispelEventFolder = "Assets/Templates/Entity/Abilities/Events/Hit/Dispel";

		private static readonly Palette DispelPalette = new Palette(new Color(0.95f, 0.20f, 0.70f), new Color(1.0f, 0.90f, 1.0f));

		/// <summary>How many effects one dispel removes.</summary>
		private const int DispelAmount = 2;

		/// <summary>Dispel Magic's reach, in metres: the target selector's range and Speed x LifeTime agree on it.</summary>
		private const float DispelRange = 25f;

		[DashboardTool(DashboardToolAttribute.Maintenance, "Generate Dispel Abilities", Section = "Ability Content", Order = 2,
			Tooltip = "Generates Dispel Magic, Self Dispel Magic and the Dispel Burst effect, registers them as addressables and adds them to the General Goods merchant. Touches nothing else.")]
		public static void GenerateDispelOnly()
		{
			written.Clear();
			problems.Clear();
			try
			{
				EnsureFolder(FXFolder);
				LoadDependencies();
				if (problems.Count > 0)
				{
					throw new InvalidOperationException("Missing dependencies:\n" + string.Join("\n", problems));
				}

				List<AbilityTemplate> abilities = new List<AbilityTemplate>();
				List<AbilityEvent> events = new List<AbilityEvent>();
				GenerateDispel(abilities, events);
				WireMerchant(abilities, events, new List<PremadeAbilityTemplate>());

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

		private static void GenerateDispel(List<AbilityTemplate> abilities, List<AbilityEvent> events)
		{
			EnsureFolder(DispelAbilityFolder);
			EnsureFolder(DispelEventFolder);

			GameObject burst = BuildEffect(explosionFX, "Dispel Burst", go => { Recolour(go, DispelPalette); MakeUnweave(go); });

			// ── Dispel Magic: one spell, two gated halves ──
			AbilityOnHitEvent purge = SaveEvent<AbilityOnHitEvent>(DispelEventFolder, "Dispel Magic Purge", ev =>
			{
				ev.TargetSelector = Hostiles(new TargetedEntitySelector { MaximumRange = DispelRange });
				ev.OnConditionsMetActions.Add(Dispel(buffs: true));
				ev.OnConditionsMetActions.Add(DispelFX(burst));
			});
			AbilityOnHitEvent cleanse = SaveEvent<AbilityOnHitEvent>(DispelEventFolder, "Dispel Magic Cleanse", ev =>
			{
				/* Not the heals' rule (Jim, 11-16, later): a cleanse lifts harmful magic from you, allies and ANY neutral
				 * character, so neutral NPCs, other players' pets and wildlife are freed too. Only enemies are left out; they
				 * get the purge. */
				ev.TargetSelector = Cleansable(new TargetedEntitySelector { MaximumRange = DispelRange });
				ev.OnConditionsMetActions.Add(Dispel(buffs: false));
				ev.OnConditionsMetActions.Add(DispelFX(burst));
			});

			// ── Self Dispel Magic ──
			AbilityOnHitEvent self = SaveEvent<AbilityOnHitEvent>(DispelEventFolder, "Self Dispel Magic Event", ev =>
			{
				ev.TargetSelector = new InitiatorTargetSelector();
				ev.OnConditionsMetActions.Add(Dispel(buffs: false));
				ev.OnConditionsMetActions.Add(DispelFX(burst));
			});

			abilities.Add(SaveAbility(DispelAbilityFolder, "Dispel Magic", a =>
			{
				a.Description = $"Unweaves the magic on your target. Strips up to {DispelAmount} beneficial effects from an enemy, or lifts up to {DispelAmount} harmful ones from anyone else.";
				a.AbilitySpawnTarget = AbilitySpawnTarget.Self;
				a.RequiresTarget = true;
				a.Type = AbilityType.GroundedMagic;
				a.ActivationTime = 0.5f;
				// Not a travel time: Speed x LifeTime is the reach the client checks a targeted cast against.
				a.Speed = DispelRange;
				a.LifeTime = 1f;
				a.Cooldown = 6f;
				a.HitCount = 1;
				a.ActivationConditions.Add(Cost(mana, 6));
				a.OnHitEvents.Add(purge);
				a.OnHitEvents.Add(cleanse);
			}));

			abilities.Add(SaveAbility(DispelAbilityFolder, "Self Dispel Magic", a =>
			{
				a.Description = $"Unweaves the harmful magic on yourself, lifting up to {DispelAmount} harmful effects.";
				a.AbilitySpawnTarget = AbilitySpawnTarget.Self;
				a.Type = AbilityType.GroundedMagic;
				a.ActivationTime = 0.5f;
				a.Cooldown = 6f;
				a.HitCount = 1;
				a.ActivationConditions.Add(Cost(mana, 6));
				a.OnHitEvents.Add(self);
			}));

			events.Add(purge);
			events.Add(cleanse);
			events.Add(self);
		}

		/// <summary>Who a cleanse reaches: you, allies and every neutral character (NPCs, pets, wildlife); never an enemy.</summary>
		private static T Cleansable<T>(T selector) where T : TargetSelector
		{
			selector.Conditions.Add(new TargetAllianceCondition
			{
				ApplyToSelf = true,
				ApplyToEnemy = false,
				ApplyToNeutral = true,
				NeutralPlayersOnly = false,
				ApplyToAllies = true,
			});
			selector.Conditions.Add(new IsCharacterAliveCondition());
			return selector;
		}

		/// <summary>Removes up to <see cref="DispelAmount"/> buffs, or debuffs.</summary>
		private static ApplyDispelAction Dispel(bool buffs)
		{
			return new ApplyDispelAction
			{
				AmountToRemoveValue = new ConstantValue { Amount = DispelAmount },
				IncludeBuffs = buffs,
				IncludeDebuffs = !buffs,
			};
		}

		/// <summary>The burst on whoever was dispelled, at chest height.</summary>
		private static PlayFXAction DispelFX(GameObject burst)
		{
			return new PlayFXAction { FXPrefab = burst, Size = 1.5f, HeightOffset = 1f };
		}

		/// <summary>
		/// Sets a burst's sparks turning about the vertical as they spread, so it spirals out — magic unwound — instead of
		/// bursting straight. Only the orbit is added; how far the sparks travel is the source effect's, so the burst still
		/// fits the one-metre cube every ability effect is authored to.
		/// </summary>
		private static void MakeUnweave(GameObject go)
		{
			foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
			{
				var main = ps.main;
				main.scalingMode = ParticleSystemScalingMode.Hierarchy;

				var velocity = ps.velocityOverLifetime;
				velocity.enabled = true;
				velocity.space = ParticleSystemSimulationSpace.Local;
				velocity.x = new ParticleSystem.MinMaxCurve(0f);
				velocity.y = new ParticleSystem.MinMaxCurve(0f);
				velocity.z = new ParticleSystem.MinMaxCurve(0f);
				velocity.orbitalX = new ParticleSystem.MinMaxCurve(0f);
				velocity.orbitalY = new ParticleSystem.MinMaxCurve(6f);
				velocity.orbitalZ = new ParticleSystem.MinMaxCurve(0f);
				velocity.radial = new ParticleSystem.MinMaxCurve(0f);
			}
		}
	}
}
