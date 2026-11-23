using FishMMO.Core;
using FishMMO.Shared.Core;
using UnityEngine;

[assembly: GameModule(typeof(FishMMO.Shared.MountsModule))]
// Every player can ride: the mount part is added as the character wakes, no prefab authors it.
[assembly: FishMMO.Shared.CharacterPart(typeof(FishMMO.Shared.MountController), typeof(FishMMO.Shared.Core.IPlayerCharacter))]

namespace FishMMO.Shared
{
	/// <summary>
	/// The Mounts module (issue #109): the Mount Saddle socket's saddles, the Mount ability that puts a player on
	/// the saddle's mount (a buff), how each mount moves (read by the core's motor through <see cref="ICharacterMount"/>), the
	/// drawn mount, the Dismount effect and the Stablemaster who sells them. See the module's README.
	/// </summary>
	public sealed class MountsModule : GameModule
	{
		/// <summary>The module's id.</summary>
		public const string ModuleId = "mounts";

		/// <inheritdoc />
		public override string Id => ModuleId;

		/// <inheritdoc />
		public override void Install(ModuleBuilder builder)
		{
		}

		private static bool hooked;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetHooks()
		{
			if (hooked)
			{
				ICharacterDamageController.OnDamaged -= OnDamaged;
				AbilityPressOverrides.Unregister(MountPressOverride.Instance);
			}
			hooked = false;
		}

		/// <summary>
		/// The module's process-wide hooks, once: a damaging hit gets its target off its mount, and pressing
		/// Mount while mounted gets off (<see cref="MountPressOverride"/>). Called by every <see cref="MountController"/> as it
		/// starts, and by tests.
		/// </summary>
		public static void EnsureHooks()
		{
			if (hooked)
			{
				return;
			}
			hooked = true;
			ICharacterDamageController.OnDamaged += OnDamaged;
			AbilityPressOverrides.Register(MountPressOverride.Instance);
		}

		/// <summary>A hit that did damage takes its target off its mount (where buffs are simulated; the owner hears by reconcile).</summary>
		internal static void OnDamaged(ICharacter attacker, ICharacter defender, int amount, DamageAttributeTemplate damageType)
		{
			if (amount <= 0 || defender == null || !defender.TryGet(out ICharacterMount mount) || !mount.IsMounted)
			{
				return;
			}
			mount.Dismount();
		}
	}
}
