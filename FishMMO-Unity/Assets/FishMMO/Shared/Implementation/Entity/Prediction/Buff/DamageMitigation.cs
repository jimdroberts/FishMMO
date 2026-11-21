using System.Collections.Generic;
using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// What a defender's buffs do to an incoming hit: absorb it (a shield's charges), deflect a projectile, block it at a
	/// guard volume, and the active shield volumes. The Combat and Abilities modules call these; the Buffs module installs
	/// the implementation (2026-11-20, <c>BuffDamageMitigation</c>). Without it nothing is mitigated.
	/// </summary>
	public static class DamageMitigation
	{
		/// <summary>The shape of <see cref="Deflector"/>.</summary>
		public delegate bool DeflectHook(ICharacter defender, Vector3 incomingHeading, Vector3 impactNormal, bool mutate, out Vector3 deflectedHeading);

		/// <summary>Installed by the Buffs module.</summary>
		public static System.Func<ICharacter, ICharacter, int, bool, int> Negator;

		/// <summary>Installed by the Buffs module.</summary>
		public static DeflectHook Deflector;

		/// <summary>Installed by the Buffs module.</summary>
		public static System.Func<ICharacter, Vector3, bool, bool> VolumeBlocker;

		/// <summary>Installed by the Buffs module.</summary>
		public static System.Action<ICharacter, List<ShieldVolume>> ShieldCollector;

		/// <summary>
		/// The damage left after the defender's negation buffs absorb what they can (<paramref name="mutate"/> spends their
		/// charges and removes the emptied ones).
		/// </summary>
		public static int Negate(ICharacter defender, ICharacter attacker, int amount, bool mutate)
		{
			return Negator != null ? Negator(defender, attacker, amount, mutate) : amount;
		}

		/// <summary>True when a deflection buff turns the object aside; <paramref name="deflectedHeading"/> is its new heading.</summary>
		public static bool TryDeflect(ICharacter defender, Vector3 incomingHeading, Vector3 impactNormal, bool mutate, out Vector3 deflectedHeading)
		{
			if (Deflector != null)
			{
				return Deflector(defender, incomingHeading, impactNormal, mutate, out deflectedHeading);
			}
			deflectedHeading = incomingHeading;
			return false;
		}

		/// <summary>True when a guard volume on the defender stops a hit at <paramref name="localImpactPoint"/>.</summary>
		public static bool TryBlockAtVolume(ICharacter defender, Vector3 localImpactPoint, bool mutate)
		{
			return VolumeBlocker != null && VolumeBlocker(defender, localImpactPoint, mutate);
		}

		/// <summary>Appends the defender's active shield volumes to <paramref name="into"/> (not cleared first).</summary>
		public static void CollectShieldVolumes(ICharacter defender, List<ShieldVolume> into)
		{
			ShieldCollector?.Invoke(defender, into);
		}
	}
}
