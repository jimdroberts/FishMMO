using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Buff template that enables CharacterFlags on apply and disables them on remove.
	/// Used for crowd-control effects such as Frozen, Stunned, and Mesmerized.
	/// Flags are additive per stack: each stack enables the same flag (idempotent via bitwise OR),
	/// but removal only clears the flag when the last stack (and base) are removed.
	/// </summary>
	[CreateAssetMenu(fileName = "New State Buff Template", menuName = "FishMMO/Character/Buff/State Buff", order = 2)]
	public class StateBuffTemplate : BaseBuffTemplate
	{
		/// <summary>
		/// The character flag to enable while this buff is active (e.g., IsFrozen, IsStunned, IsMesmerized).
		/// </summary>
		[Tooltip("The CharacterFlag to enable while this buff is active.")]
		public CharacterFlags Flag;

		/// <summary>
		/// Appends a secondary tooltip describing the state effect.
		/// </summary>
		/// <param name="builder">The tooltip builder to populate.</param>
		public override void SecondaryTooltip(TooltipBuilder builder)
		{
			builder.AddLine($"Applies: {Flag}", 20, TooltipColors.Stat);
		}

		/// <summary>
		/// Enables the configured CharacterFlag on the target.
		/// </summary>
		/// <param name="buff">The buff instance being applied.</param>
		/// <param name="target">The character receiving the buff.</param>
		public override void OnApply(Buff buff, ICharacter target)
		{
			if (target == null) return;
			target.EnableFlags(Flag);
		}

		/// <summary>
		/// Disables the configured CharacterFlag on the target.
		/// </summary>
		/// <param name="buff">The buff instance being removed.</param>
		/// <param name="target">The character losing the buff.</param>
		public override void OnRemove(Buff buff, ICharacter target)
		{
			if (target == null) return;
			target.DisableFlags(Flag);
		}

		/// <summary>
		/// Stacking is idempotent for flags — the flag is already enabled.
		/// No additional action needed on stack add.
		/// </summary>
		/// <param name="buff">The buff instance being stacked.</param>
		/// <param name="target">The character receiving the stack.</param>
		public override void OnApplyStack(Buff buff, ICharacter target)
		{
			// Flag is already enabled from base apply; stacking only extends duration.
		}

		/// <summary>
		/// Stack removal does not disable the flag until the final removal via OnRemove.
		/// </summary>
		/// <param name="buff">The buff instance being unstacked.</param>
		/// <param name="target">The character losing the stack.</param>
		public override void OnRemoveStack(Buff buff, ICharacter target)
		{
			// Flag stays enabled until the base buff is fully removed.
		}
	}
}