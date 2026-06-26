using System.Collections.Generic;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Controls damage, healing, kill, and resurrection logic. Handles resistance
	/// calculation, ECA trigger dispatch, immortal state, and combat state transitions.
	/// </summary>
	public class CharacterDamageController : CharacterBehaviour, ICharacterDamageController
	{
		// ───── ECA Trigger Lists ─────────────────────────────────────────────

		[Header("ECA - Damage")]
		[Tooltip("Triggers invoked when this character deals damage to another.")]
		/// <summary>Triggers invoked when this character deals damage to another.</summary>
		[SerializeField]
		private List<Trigger> onDamageTriggers = new List<Trigger>();
		[Tooltip("Triggers invoked when this character receives damage from another.")]
		/// <summary>Triggers invoked when this character receives damage from another.</summary>
		[SerializeField]
		private List<Trigger> onDamagedTriggers = new List<Trigger>();

		[Header("ECA - Healing")]
		[Tooltip("Triggers invoked when this character heals another.")]
		/// <summary>Triggers invoked when this character heals another.</summary>
		[SerializeField]
		private List<Trigger> onHealTriggers = new List<Trigger>();
		[Tooltip("Triggers invoked when this character is healed by another.")]
		/// <summary>Triggers invoked when this character is healed by another.</summary>
		[SerializeField]
		private List<Trigger> onHealedTriggers = new List<Trigger>();

		[Header("ECA - Kill")]
		[Tooltip("Triggers invoked when this character kills another.")]
		/// <summary>Triggers invoked when this character kills another.</summary>
		[SerializeField]
		private List<Trigger> onKillTriggers = new List<Trigger>();
		[Tooltip("Triggers invoked when this character is killed by another.")]
		/// <summary>Triggers invoked when this character is killed by another.</summary>
		[SerializeField]
		private List<Trigger> onKilledTriggers = new List<Trigger>();

		[Header("ECA - Resurrect")]
		[Tooltip("Triggers invoked when this character resurrects another.")]
		/// <summary>Triggers invoked when this character resurrects another.</summary>
		[SerializeField]
		private List<Trigger> onResurrectTriggers = new List<Trigger>();
		[Tooltip("Triggers invoked when this character is resurrected by another.")]
		/// <summary>Triggers invoked when this character is resurrected by another.</summary>
		[SerializeField]
		private List<Trigger> onResurrectedTriggers = new List<Trigger>();

		/// <inheritdoc />
		public List<Trigger> OnDamageTriggers => onDamageTriggers;
		/// <inheritdoc />
		public List<Trigger> OnDamagedTriggers => onDamagedTriggers;
		/// <inheritdoc />
		public List<Trigger> OnHealTriggers => onHealTriggers;
		/// <inheritdoc />
		public List<Trigger> OnHealedTriggers => onHealedTriggers;
		/// <inheritdoc />
		public List<Trigger> OnKillTriggers => onKillTriggers;
		/// <inheritdoc />
		public List<Trigger> OnKilledTriggers => onKilledTriggers;
		/// <inheritdoc />
		public List<Trigger> OnResurrectTriggers => onResurrectTriggers;
		/// <inheritdoc />
		public List<Trigger> OnResurrectedTriggers => onResurrectedTriggers;

		/// <summary>
		/// If true, this character cannot be damaged or killed.
		/// </summary>
		[SerializeField]
		private bool immortal = false;
		/// <summary>
		/// Gets or sets whether the character is immortal (cannot be damaged or killed).
		/// </summary>
		public bool Immortal { get { return this.immortal; } set { this.immortal = value; } }

		/// <summary>
		/// Returns true if the character is alive (resource attribute's current value is above zero).
		/// </summary>
		public bool IsAlive
		{
			get
			{
				if (ResourceInstance == null)
				{
					return false;
				}
				return ResourceInstance.CurrentValue > 0;
			}
		}

		//public List<Character> Attackers; // Uncomment and implement if tracking attackers is needed.

		/// <summary>
		/// Cached reference to the character's health resource attribute.
		/// Lazily initialized on first access; throws if missing.
		/// </summary>
		private CharacterResourceAttribute resourceInstance;
		/// <summary>
		/// Gets the cached health resource attribute for this character.
		/// Returns null and logs an error if the attribute controller or health attribute is missing.
		/// </summary>
		public CharacterResourceAttribute ResourceInstance
		{
			get
			{
				if (resourceInstance == null)
				{
					if (!Character.TryGet(out ICharacterAttributeController attributeController) ||
						!attributeController.TryGetHealthAttribute(out resourceInstance))
					{
						Log.Error("CharacterDamageController", $"{gameObject.name} is missing ICharacterAttributeController or Health Resource Attribute.");
					}
				}
				return resourceInstance;
			}
		}

		/// <summary>
		/// Applies resistance modifiers to the damage amount for the target character.
		/// Subtracts the target's resistance value from the incoming damage and clamps the result.
		/// <para>
		/// If <paramref name="target"/> has no <see cref="ICharacterAttributeController"/> or
		/// <paramref name="damageAttribute"/> is null, the resistance lookup is skipped and the
		/// original <paramref name="amount"/> is returned unchanged — absence of resistance
		/// metadata means untyped damage, not immunity.
		/// </para>
		/// </summary>
		/// <param name="target">The character receiving damage.</param>
		/// <param name="amount">The base damage amount.</param>
		/// <param name="damageAttribute">The damage type being applied.</param>
		/// <returns>The modified damage amount after resistance is applied.</returns>
		public int ApplyModifiers(ICharacter target, int amount, DamageAttributeTemplate damageAttribute)
		{
			const int MIN_DAMAGE = 0;
			const int MAX_DAMAGE = 999999;

			if (target == null || damageAttribute == null)
			{
				return amount;
			}

			// No attribute controller means no resistance stats — pass through at full value.
			// Returning 0 here would make the character silently invulnerable, which is wrong.
			if (!target.TryGet(out ICharacterAttributeController attributeController))
			{
				return amount;
			}

			// Resistance may be null for damage types that intentionally bypass all resistance
			// (environmental hazards, true damage, etc.). Guard before accessing .ID to prevent NPE.
			if (damageAttribute.Resistance != null &&
				attributeController.TryGetAttribute(damageAttribute.Resistance.ID, out CharacterAttribute resistance))
			{
				amount = (amount - resistance.FinalValue).Clamp(MIN_DAMAGE, MAX_DAMAGE);
			}
			return amount;
		}

		/// <summary>
		/// Applies damage to this character from an attacker. Handles resistance calculation,
		/// kill detection, and ECA trigger dispatch. Does nothing if the character is immortal
		/// or already dead. Resistance-reduced damage below 1 is silently discarded.
		/// </summary>
		/// <param name="attacker">The character dealing damage, or null for environmental damage.</param>
		/// <param name="amount">Base damage before resistance is applied.</param>
		/// <param name="damageAttribute">The damage type; determines which resistance stat is checked.</param>
		/// <param name="ignoreAchievements">If true, suppresses ECA trigger dispatch for this hit.</param>
		public void Damage(ICharacter attacker, int amount, DamageAttributeTemplate damageAttribute, bool ignoreAchievements = false)
		{
			if (Immortal)
			{
				return;
			}

			if (ResourceInstance == null)
			{
				return;
			}

			// We are already dead.
			if (ResourceInstance.CurrentValue <= 0.0f)
			{
				return;
			}

			amount = ApplyModifiers(Character, amount, damageAttribute);

			if (amount < 1)
			{
				return;
			}
			ResourceInstance.Consume(amount);

			ICharacterDamageController.OnDamaged?.Invoke(attacker, Character, amount, damageAttribute);

			if (!ignoreAchievements)
			{
				// Invoke attacker's OnDamage triggers (e.g. achievements for dealing damage)
				if (attacker != null &&
					attacker.TryGet(out ICharacterDamageController attackerDamage))
				{
					attacker.Invoke(attackerDamage.OnDamageTriggers, new DamageEventData(attacker, Character, amount, damageAttribute));
				}

				// Invoke defender's OnDamaged triggers (e.g. achievements for receiving damage)
				Character.Invoke(OnDamagedTriggers, new DamageEventData(Character, attacker, amount, damageAttribute));
			}

			// Check if we died after taking damage.
			if (ResourceInstance.CurrentValue <= 0.0f)
			{
				Kill(attacker);
			}
		}

		/// <summary>
		/// Kills this character. Handles faction rewards, ECA triggers, ability cancellation,
		/// death animation, and the OnKilled event. Buff removal and pet despawning are handled
		/// by the server-side OnKilled subscriber (CharacterSystem.Connection.cs).
		/// </summary>
		/// <param name="killer">The character responsible for the kill, or null for non-player kills.</param>
		public void Kill(ICharacter killer)
		{
			if (Immortal) return;

			if (killer != null)
			{
				if (killer.TryGet(out IFactionController fc) &&
					Character.TryGet(out IFactionController dfc))
					fc.AdjustFaction(dfc, 0.01f, 0.01f);

				if (killer.TryGet(out ICharacterDamageController kdc))
					killer.Invoke(kdc.OnKillTriggers, new EventData(killer, Character));
			}

			Character.Invoke(OnKilledTriggers, new EventData(Character, killer));

			if (base.IsServerStarted && Character.TryGet(out IAbilityController ac))
				ac.Cancel();

			if (Character.TryGet(out ICharacterAnimationController anim))
				anim.TriggerDeath();

			ICharacterDamageController.OnKilled?.Invoke(killer, Character);
		}

		/// <summary>
		/// Heals this character by the specified amount. Events and ECA triggers are only
		/// fired when healing actually changes the resource value; healing a dead character,
		/// healing for zero, or attempting to heal a full-health character are all silent no-ops.
		/// </summary>
		/// <param name="healer">The character providing the healing, or null.</param>
		/// <param name="amount">The amount to heal.</param>
		/// <param name="ignoreAchievements">If true, suppresses ECA trigger dispatch.</param>
		public void Heal(ICharacter healer, int amount, bool ignoreAchievements = false)
		{
			if (ResourceInstance == null || ResourceInstance.CurrentValue <= 0.0f)
			{
				return;
			}

			float valueBefore = ResourceInstance.CurrentValue;
			ResourceInstance.Gain(amount);

			// Suppress events if nothing actually changed (amount == 0 or resource was already full).
			// Firing OnHealed/achievement triggers for 0-effective healing wastes ECA evaluation
			// and can cause false achievement awards.
			if (ResourceInstance.CurrentValue <= valueBefore)
			{
				return;
			}

			ICharacterDamageController.OnHealed?.Invoke(healer, Character, amount);

			if (!ignoreAchievements)
			{
				// Invoke healer's OnHeal triggers (e.g. achievements for healing)
				if (healer != null &&
					healer.TryGet(out ICharacterDamageController healerDamage))
				{
					healer.Invoke(healerDamage.OnHealTriggers, new HealEventData(healer, Character, amount));
				}

				// Invoke healed character's OnHealed triggers (e.g. achievements for being healed)
				Character.Invoke(OnHealedTriggers, new HealEventData(Character, healer, amount));
			}
		}

		/// <summary>
		/// Fully restores this character's health resource to its maximum (final) value.
		/// Does nothing if the character is dead.
		/// </summary>
		public void CompleteHeal()
		{
			if (ResourceInstance != null && ResourceInstance.CurrentValue > 0.0f)
			{
				float toHeal = ResourceInstance.FinalValue - ResourceInstance.CurrentValue;
				ResourceInstance.Gain(toHeal);
			}
		}

		/// <inheritdoc />
		public void Revive(ICharacter resurrector, int amount)
		{
			if (ResourceInstance == null || amount <= 0) return;

			// Gain bypasses Heal() dead-character guard -- works on CurrentValue == 0.
			ResourceInstance.Gain(amount);

			// Reset death animation on the client.
			if (Character.TryGet(out ICharacterAnimationController animController))
			{
				animController.ResetDeath();
			}

			// Fire ECA resurrect triggers.
			if (resurrector != null)
			{
				resurrector.Invoke(onResurrectTriggers, new EventData(resurrector, Character));
			}
			Character.Invoke(onResurrectedTriggers, new EventData(Character, resurrector));

			ICharacterDamageController.OnResurrected?.Invoke(resurrector, Character);
		}

		/// <summary>
		/// Clears the cached health resource attribute reference so it is re-resolved on the
		/// next access. Prevents a stale object reference if the
		/// <see cref="CharacterAttributeController"/> is re-initialized (character pooling,
		/// hot-reload, or any scenario where attribute instances are recreated).
		/// </summary>
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);
			resourceInstance = null;
		}
	}
}