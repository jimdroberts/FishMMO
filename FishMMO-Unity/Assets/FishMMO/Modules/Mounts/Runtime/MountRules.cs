using System.Collections.Generic;
using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// The mount rules (issue #109) as small functions both peers and the tests share: which mount a character rides, which
	/// one its saddle names, may it mount here and now, and how high the saddle is.
	/// </summary>
	public static class MountRules
	{
		/// <summary>A saddle's thickness over the back, metres: where the rider's hip joints sit.</summary>
		public const float SaddleThickness = 0.08f;

		/// <summary>Where the rider's hip joints sit over the ground on <paramref name="body"/>, metres.</summary>
		public static float SaddleHeight(WildlifeBodySpec body)
		{
			return Mathf.Max(0.2f, body.LegLength + body.Depth) + SaddleThickness;
		}

		/// <summary>The mount <paramref name="character"/> rides now (its first mount buff), or null on foot.</summary>
		/// <remarks>
		/// Reads the buff set, which every peer holds (the owner and the server simulate it, observers materialise it from the
		/// server's message), so the motor, the drawing and the rules all agree on it. Allocation-free.
		/// </remarks>
		public static MountBuffTemplate ActiveMount(ICharacter character)
		{
			if (character == null || !character.TryGet(out IBuffController buffs))
			{
				return null;
			}
			return ActiveMount(buffs.Buffs);
		}

		/// <summary>The first mount buff in <paramref name="buffs"/>, or null.</summary>
		public static MountBuffTemplate ActiveMount(SortedDictionary<int, Buff> buffs)
		{
			if (buffs == null || buffs.Count == 0)
			{
				return null;
			}
			foreach (KeyValuePair<int, Buff> pair in buffs)
			{
				if (pair.Value?.Template is MountBuffTemplate mount)
				{
					return mount;
				}
			}
			return null;
		}

		/// <summary>The mount the saddle in <paramref name="character"/>'s Mount Saddle socket names, or null.</summary>
		public static MountBuffTemplate SaddleMount(ICharacter character)
		{
			if (character == null || !character.TryGet(out IEquipmentController equipment))
			{
				return null;
			}
			return SaddleMount(equipment);
		}

		/// <summary>The mount the saddle in a Mount Saddle socket names, or null (no saddle, or one that names nothing).</summary>
		public static MountBuffTemplate SaddleMount(IItemContainer equipment)
		{
			if (equipment == null || !equipment.TryGetItem((int)ItemSlot.MountSaddle, out Item item) || item == null)
			{
				return null;
			}
			return item.Template is SaddleTemplate saddle ? saddle.Mount : null;
		}

		/// <summary>Why <paramref name="character"/> may not mount now (null when it may): the Mount ability's refusals.</summary>
		/// <remarks>
		/// No saddle; in combat; swimming or on a ladder; dead; or indoors or
		/// underground (<see cref="MountShelter"/>).
		/// </remarks>
		public static string RefusalFor(ICharacter character, bool checkShelter = true)
		{
			if (character == null)
			{
				return "Nobody to mount.";
			}
			if (SaddleMount(character) == null)
			{
				return "Equip a saddle to mount.";
			}
			if (character.IsFlagged(CharacterFlags.IsDead))
			{
				return "The dead cannot ride.";
			}
			if (character.IsFlagged(CharacterFlags.IsInCombat))
			{
				return "You cannot mount in combat.";
			}
			if (character is IPlayerCharacter player && player.CharacterController != null &&
				(player.CharacterController.IsSwimming || player.CharacterController.IsClimbing))
			{
				return "You cannot mount here.";
			}
			if (checkShelter && MountShelter.IsSheltered(character))
			{
				return "There is no room to ride here.";
			}
			return null;
		}

		/// <summary>The tooltip lines for a mount: what riding it changes.</summary>
		public static IEnumerable<(string Label, string Value)> Describe(MountBuffTemplate mount)
		{
			if (mount == null)
			{
				yield break;
			}
			if (!Mathf.Approximately(mount.GroundSpeedMultiplier, 1f))
			{
				int percent = Mathf.RoundToInt((mount.GroundSpeedMultiplier - 1f) * 100f);
				yield return ("Speed", $"{(percent >= 0 ? "+" : string.Empty)}{percent}%");
			}
			if (mount.JumpHeightMultiplier > 1f)
			{
				yield return ("Jump", $"{mount.JumpHeightMultiplier:0.#}× height");
			}
			if (mount.LeapSpeed > 0f)
			{
				yield return ("Leaps", $"+{mount.LeapSpeed:0.#} m/s forward");
			}
			if (mount.MaxSlopeDegrees > 0f)
			{
				yield return ("Climbs", $"slopes up to {mount.MaxSlopeDegrees:0}°");
			}
			if (mount.Flies)
			{
				yield return ("Flies", $"{mount.FlySpeed:0.#} m/s, {mount.FlightStaminaPerSecond:0.#} stamina/s");
			}
		}
	}
}
