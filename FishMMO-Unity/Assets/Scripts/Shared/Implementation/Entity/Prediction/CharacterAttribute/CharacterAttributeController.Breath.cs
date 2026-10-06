using UnityEngine;
using FishMMO.Shared.Core;
using FishMMO.Shared.Weather;

namespace FishMMO.Shared
{
	/// <summary>
	/// The arithmetic of breath and drowning, apart from any character, so it can be tested on its own.
	/// </summary>
	/// <remarks>
	/// Jim's rules (2026-10-05): a meter drains while the head is under water and refills in about three seconds at the
	/// surface; at zero, a FLAT tenth of maximum health a second. Lava burns 50 fire a second, which fire resistance takes
	/// off one for one and from which fire races are immune.
	/// </remarks>
	public static class BreathRules
	{
		/// <summary>Seconds an empty breath takes to fill again with the head out of the water.</summary>
		public const float RefillSeconds = 3f;

		/// <summary>The share of maximum health drowning takes each second.</summary>
		public const float DrowningShare = 0.1f;

		/// <summary>Fire damage lava does each second, before resistance.</summary>
		public const int LavaDamagePerSecond = 50;

		/// <summary>Breath after <paramref name="seconds"/>: down a second a second while holding it, up to full in <see cref="RefillSeconds"/> while breathing.</summary>
		public static float Step(float breath, float max, bool holding, float seconds)
		{
			if (max <= 0f)
			{
				return 0f;
			}
			return holding
				? Mathf.Max(0f, breath - seconds)
				: Mathf.Min(max, breath + max * seconds / RefillSeconds);
		}

		/// <summary>Damage one second of drowning does to a character with <paramref name="maxHealth"/>: never less than one.</summary>
		public static int DrowningDamage(int maxHealth) => Mathf.Max(1, Mathf.CeilToInt(DrowningShare * maxHealth));

		/// <summary>True on the ticks a once-a-second environmental pulse lands: the first tick of each synced second.</summary>
		public static bool IsPulseTick(uint syncedTick, double tickDelta)
		{
			uint perSecond = (uint)Mathf.Max(1, Mathf.RoundToInt((float)(1.0 / System.Math.Max(1e-4, tickDelta))));
			return syncedTick % perSecond == 0u;
		}
	}

	public partial class CharacterAttributeController
	{
		/// <summary>The template IDs the two damage types resolve to when none is set on the prefab: the assets of these names.</summary>
		private static readonly int DefaultDrowningDamageID = (nameof(DamageAttributeTemplate) + "Drowning Damage").GetDeterministicHashCode();
		private static readonly int DefaultLavaDamageID = (nameof(DamageAttributeTemplate) + "Fire Damage").GetDeterministicHashCode();

		[Header("Breath")]
		[Tooltip("Seconds a character can hold its breath under water, before Breath Capacity.")]
		public float BaseBreathSeconds = 30f;

		/// <summary>An attribute that scales the breath held (a percentage, as the speed attributes are): a swimming skill, a buff. Optional.</summary>
		[TemplateReference(typeof(CharacterAttributeTemplate))]
		public int BreathCapacityTemplateID;

		/// <summary>The damage drowning does. Zero: the "Drowning Damage" asset.</summary>
		[TemplateReference(typeof(DamageAttributeTemplate))]
		public int DrowningDamageTemplateID;

		/// <summary>The damage lava does. Zero: the "Fire Damage" asset, so fire resistance and fire immunity apply.</summary>
		[TemplateReference(typeof(DamageAttributeTemplate))]
		public int LavaDamageTemplateID;

		// Seconds of breath left; negative until first set, which reads as full.
		private float breath = -1f;

		/// <summary>The most breath this character holds, seconds.</summary>
		public float MaxBreathSeconds
		{
			get
			{
				float max = BaseBreathSeconds;
				if (BreathCapacityTemplateID != 0 && TryGetAttribute(BreathCapacityTemplateID, out CharacterAttribute capacity))
				{
					max *= capacity.FinalValueAsPct;
				}
				return Mathf.Max(0f, max);
			}
		}

		/// <summary>Seconds of breath left.</summary>
		public float BreathSeconds => breath < 0f ? MaxBreathSeconds : Mathf.Min(breath, MaxBreathSeconds);

		/// <summary>True while the head is under water this character cannot breathe (what the breath bar shows itself for).</summary>
		public bool IsHoldingBreath { get; private set; }

		private void ResetBreath()
		{
			breath = -1f;
			IsHoldingBreath = false;
		}

		private void ApplyBreathState(float seconds)
		{
			breath = Mathf.Max(0f, seconds);
		}

		private static DamageAttributeTemplate ResolveDamage(int id, int fallbackID)
		{
			return CharacterAttributeTemplate.Get<DamageAttributeTemplate>(id != 0 ? id : fallbackID);
		}

		/// <summary>
		/// One tick of breath, after the character has moved (this runs after KCCPlayer): down while its head is under
		/// water it cannot breathe, back up when it is out. On the server, on the first tick of each synced second, the
		/// damage: drowning once the breath is gone, and lava's fire for as long as the character is in it.
		/// </summary>
		/// <remarks>
		/// The breath itself is predicted (it rides the reconcile in <see cref="CharacterAttributeResourceState.Breath"/>),
		/// so the owner's bar runs smoothly. The damage is the server's alone, as death is: health reaches the owner on
		/// the reconcile.
		/// </remarks>
		private void StepBreath()
		{
			if (Character == null || base.TimeManager == null)
			{
				return;
			}
			KCCController kcc = Character is IPlayerCharacter player && player.KCCPlayer != null ? player.KCCPlayer.CharacterController : null;
			if (kcc == null || kcc.Motor == null)
			{
				IsHoldingBreath = false;
				return;
			}

			WaterSample water = kcc.Water;
			bool lava = water.Body == WaterBody.Lava;
			RaceTemplate race = RaceTemplate.Of(Character);
			DamageAttributeTemplate lavaDamage = lava ? ResolveDamage(LavaDamageTemplateID, DefaultLavaDamageID) : null;
			bool lavaImmune = lava && race != null && race.IsImmuneTo(lavaDamage);
			// A water-breather breathes water, not lava; a fire race breathes in its lava.
			bool breathes = lava ? lavaImmune : race != null && race.BreathesWater;
			bool holding = water.Present && kcc.HeadDepth > 0f && !breathes;
			IsHoldingBreath = holding;

			float max = MaxBreathSeconds;
			breath = BreathRules.Step(BreathSeconds, max, holding, (float)base.TimeManager.TickDelta);

			if (!base.IsServerStarted || !BreathRules.IsPulseTick(base.TimeManager.Tick, base.TimeManager.TickDelta) ||
				!Character.TryGet(out ICharacterDamageController damage) || !damage.IsAlive)
			{
				return;
			}
			if (holding && breath <= 0f && TryGetHealthAttribute(out CharacterResourceAttribute health))
			{
				damage.Damage(null, BreathRules.DrowningDamage(health.FinalValue), ResolveDamage(DrowningDamageTemplateID, DefaultDrowningDamageID), ignoreAchievements: false, periodic: true);
			}
			if (lava && !lavaImmune && water.DepthOf(kcc.Motor.TransientPosition.y) > 0f)
			{
				damage.Damage(null, BreathRules.LavaDamagePerSecond, lavaDamage, ignoreAchievements: false, periodic: true);
			}
		}
	}
}
