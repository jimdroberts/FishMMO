using System.Collections.Generic;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Something pressing an ability does INSTEAD of casting it: the Mount ability pressed while mounted gets off at once
	/// (issue #109, the Mounts module registers it). A seam, so this module names no other.
	/// </summary>
	public interface IAbilityPressOverride
	{
		/// <summary>Whether pressing <paramref name="ability"/> does this instead of casting it, now. No side effects.</summary>
		bool Applies(ICharacter caster, Ability ability);

		/// <summary>Does it. Called inside the replicate on the server and the owner (and again on the owner's replays).</summary>
		void Press(ICharacter caster, Ability ability);
	}

	/// <summary>
	/// The registered <see cref="IAbilityPressOverride"/>s, asked in order by <see cref="AbilityController"/> before a press
	/// becomes a cast.
	/// </summary>
	/// <remarks>
	/// <b>Predicted like the cast it replaces.</b> The press rides the replicate input as any activation does, and the
	/// override is decided where the cast would be started, from reconciled state (the buffs), so the server and the owner do
	/// the same thing on the same tick. It is asked before the cooldown and the activation conditions: a Mount on cooldown,
	/// or refused in combat, still lets its rider get off. Nothing is cast, so nothing goes on cooldown.
	/// </remarks>
	public static class AbilityPressOverrides
	{
		private static readonly List<IAbilityPressOverride> overrides = new List<IAbilityPressOverride>();

		/// <summary>Adds an override (once).</summary>
		public static void Register(IAbilityPressOverride handler)
		{
			if (handler != null && !overrides.Contains(handler))
			{
				overrides.Add(handler);
			}
		}

		/// <summary>Removes an override.</summary>
		public static void Unregister(IAbilityPressOverride handler)
		{
			overrides.Remove(handler);
		}

		/// <summary>The override that applies to this press, or null.</summary>
		public static IAbilityPressOverride Find(ICharacter caster, Ability ability)
		{
			if (caster == null || ability == null)
			{
				return null;
			}
			for (int i = 0; i < overrides.Count; i++)
			{
				if (overrides[i].Applies(caster, ability))
				{
					return overrides[i];
				}
			}
			return null;
		}
	}
}
