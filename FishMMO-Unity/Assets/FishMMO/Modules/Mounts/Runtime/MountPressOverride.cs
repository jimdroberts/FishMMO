using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Pressing the Mount ability while mounted gets off at once, instead of casting it (issue #109). Predicted: decided in the
	/// replicate from the buff set, on the server and the owner alike (<see cref="AbilityPressOverrides"/>).
	/// </summary>
	public sealed class MountPressOverride : IAbilityPressOverride
	{
		/// <summary>The one instance the module registers.</summary>
		public static readonly MountPressOverride Instance = new MountPressOverride();

		/// <summary>Whether <paramref name="ability"/> is a Mount ability: one of its events mounts its caster.</summary>
		public static bool IsMountAbility(Ability ability) => IsMountTemplate(ability?.Template);

		/// <summary>Whether <paramref name="template"/>'s hit or spawn events carry a <see cref="MountAction"/>.</summary>
		public static bool IsMountTemplate(AbilityTemplate template)
		{
			if (template == null)
			{
				return false;
			}
			if (Mounts(template.OnHitEvents) || Mounts(template.OnSpawnEvents))
			{
				return true;
			}
			return false;
		}

		private static bool Mounts<T>(System.Collections.Generic.List<T> events) where T : AbilityEvent
		{
			if (events == null)
			{
				return false;
			}
			foreach (T ev in events)
			{
				if (ev == null || ev.OnConditionsMetActions == null)
				{
					continue;
				}
				foreach (BaseAction action in ev.OnConditionsMetActions)
				{
					if (action is MountAction)
					{
						return true;
					}
				}
			}
			return false;
		}

		/// <inheritdoc />
		public bool Applies(ICharacter caster, Ability ability)
		{
			return caster != null && MountRules.ActiveMount(caster) != null && IsMountAbility(ability);
		}

		/// <inheritdoc />
		public void Press(ICharacter caster, Ability ability)
		{
			if (caster != null && caster.TryGet(out ICharacterMount mount))
			{
				mount.Dismount();
			}
		}
	}
}
