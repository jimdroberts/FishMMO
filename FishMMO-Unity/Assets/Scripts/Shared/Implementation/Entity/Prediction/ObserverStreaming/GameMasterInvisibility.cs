using System.Collections.Generic;
using FishMMO.Auth.Core;
using FishMMO.Shared.Core;
using FishNet.Connection;
using FishNet.Object;

namespace FishMMO.Shared
{
	/// <summary>
	/// Server-side set of characters hidden by <c>/gm invis</c>. A hidden character is not observed by any connection except
	/// its own and staff (game masters and above), so it is despawned on every other client: no model, nameplate, sound or
	/// broadcast reaches them.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Enforced by <see cref="ObserverBudgetCondition"/>, one of the scene server's default observer conditions, so it covers
	/// every character without a per-prefab condition. Staff keep seeing a hidden colleague so two game masters working the
	/// same scene do not lose each other.
	/// </para>
	/// <para>
	/// Keyed by the object, and cleared when the character despawns (the caller's job): characters are pooled, and a pooled
	/// object reused by another player must not arrive hidden. Not persisted: a game master who relogs is visible again.
	/// </para>
	/// </remarks>
	public static class GameMasterInvisibility
	{
		private static readonly HashSet<NetworkObject> hidden = new HashSet<NetworkObject>();

		/// <summary>True when no character is hidden: the observer condition's fast path.</summary>
		public static bool IsEmpty => hidden.Count == 0;

		/// <summary>Whether <paramref name="nob"/> is hidden.</summary>
		public static bool IsHidden(NetworkObject nob) => nob != null && hidden.Contains(nob);

		/// <summary>Hides or shows a character. Returns true when that changed anything.</summary>
		public static bool SetHidden(NetworkObject nob, bool value)
		{
			if (nob == null)
			{
				return false;
			}
			return value ? hidden.Add(nob) : hidden.Remove(nob);
		}

		/// <summary>Forgets every hidden character (server stop).</summary>
		public static void Clear() => hidden.Clear();

		/// <summary>
		/// Whether <paramref name="nob"/> must be withheld from <paramref name="viewer"/>: it is hidden, and the viewer is
		/// neither its owner nor staff.
		/// </summary>
		public static bool HidesFrom(NetworkObject nob, NetworkConnection viewer)
		{
			if (hidden.Count == 0 || nob == null || viewer == null || !hidden.Contains(nob))
			{
				return false;
			}
			if (viewer == nob.Owner)
			{
				return false;
			}
			return !IsStaff(viewer);
		}

		/// <summary>Whether the connection's character is a game master or above.</summary>
		private static bool IsStaff(NetworkConnection viewer)
		{
			NetworkObject first = viewer.FirstObject;
			return first != null &&
				first.TryGetComponent(out IPlayerCharacter character) &&
				character.AccessLevel >= AccessLevel.GameMaster;
		}
	}
}
