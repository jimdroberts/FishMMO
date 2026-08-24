using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Buff template that applies periodic resource modifications (heal-over-time / damage-over-time).
	/// Each tick adds or subtracts from the current value of target resource attributes.
	/// The tick amount scales linearly with the number of stacks (base + stacks).
	/// This buff is deterministic — identical inputs produce identical state changes.
	/// </summary>
	[CreateAssetMenu(fileName = "New Resource Tick Buff Template", menuName = "FishMMO/Character/Buff/Resource Tick Buff", order = 3)]
	public class ResourceTickBuffTemplate : BaseBuffTemplate
	{
		/// <summary>
		/// List of resource attribute modifications applied per tick.
		/// Positive values heal, negative values deal damage.
		/// </summary>
		[Tooltip("Resource attributes to modify per tick. Positive = heal, negative = damage.")]
		public List<BuffAttributeTemplate> TickAttributes;

		/// <summary>
		/// Damage type used to resolve resistance when a tick reduces health.
		/// </summary>
		/// <remarks>
		/// Leave empty for true damage — a null damage type bypasses resistance entirely, which is
		/// the right default for environmental and pure-magic effects. Set it to make a poison
		/// respect nature resistance, a burn respect fire resistance, and so on.
		/// <para>
		/// Ignored by ticks that heal or that target a resource other than health.
		/// </para>
		/// </remarks>
		[Tooltip("Damage type used for resistance when a tick reduces health. Leave empty for true damage.")]
		public DamageAttributeTemplate DamageAttribute;

		/// <summary>
		/// Appends a secondary tooltip describing the per-tick resource effects.
		/// </summary>
		/// <param name="builder">The tooltip builder to populate.</param>
		public override void SecondaryTooltip(TooltipBuilder builder)
		{
			if (TickAttributes == null || TickAttributes.Count < 1) return;

			builder.AddLine("Per Tick", 20, TooltipColors.Title, false, "140%");
			for (int i = 0; i < TickAttributes.Count; i++)
			{
				BuffAttributeTemplate tickAttribute = TickAttributes[i];
				if (tickAttribute?.Template == null) continue;

				string label = tickAttribute.Value >= 0 ? "+" : "";
				builder.AddLine($"{tickAttribute.Template.Name}: {label}{tickAttribute.Value}/tick", 21 + i, TooltipColors.Stat);
			}
		}

		/// <summary>
		/// No immediate effect on apply — all effects are tick-based.
		/// </summary>
		/// <param name="buff">The buff instance being applied.</param>
		/// <param name="target">The character receiving the buff.</param>
		public override void OnApply(Buff buff, ICharacter target)
		{
			// No immediate effect; resources are modified via OnTick.
		}

		/// <summary>
		/// No cleanup needed on remove — tick effects modify current values, not modifiers.
		/// </summary>
		/// <param name="buff">The buff instance being removed.</param>
		/// <param name="target">The character losing the buff.</param>
		public override void OnRemove(Buff buff, ICharacter target)
		{
			// Tick effects modify current resource values directly; no undo needed.
		}

		/// <summary>
		/// No immediate effect on stack add — stacking increases the tick multiplier.
		/// </summary>
		/// <param name="buff">The buff instance being stacked.</param>
		/// <param name="target">The character receiving the stack.</param>
		public override void OnApplyStack(Buff buff, ICharacter target)
		{
			// Stacking only affects the multiplier used in OnTick.
		}

		/// <summary>
		/// No cleanup on stack remove — tick multiplier decreases naturally.
		/// </summary>
		/// <param name="buff">The buff instance being unstacked.</param>
		/// <param name="target">The character losing the stack.</param>
		public override void OnRemoveStack(Buff buff, ICharacter target)
		{
			// Stacking only affects the multiplier used in OnTick.
		}

		/// <summary>
		/// Called each tick. Applies resource modifications scaled by (1 + Stacks) to the target.
		/// </summary>
		/// <remarks>
		/// Health ticks go through the damage pipeline rather than writing the resource directly,
		/// so a damage-over-time effect mitigates, generates threat, credits its caster and can
		/// actually kill. See <see cref="BaseBuffTemplate.ApplyResourceTick"/>.
		/// </remarks>
		/// <param name="buff">The buff instance.</param>
		/// <param name="target">The character affected.</param>
		public override void OnTick(Buff buff, ICharacter target)
		{
			base.OnTick(buff, target);
			ApplyResourceTick(buff, target, TickAttributes, DamageAttribute);
		}
	}
}