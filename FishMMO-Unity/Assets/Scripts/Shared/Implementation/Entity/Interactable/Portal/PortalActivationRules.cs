using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// Who a portal opens for once it is activated (Jim, 2026-10-10: chosen per portal; generated
	/// portals get a seeded mix).
	/// </summary>
	public enum PortalActivationScope : byte
	{
		/// <summary>Opens for the character who activated it, forever, and for nobody else.</summary>
		PerCharacter = 0,

		/// <summary>Opens for everyone in the world for <see cref="PortalActivation.DurationSeconds"/>, then closes again.</summary>
		WorldTimed = 1,

		/// <summary>Opens for everyone in the world, for good.</summary>
		WorldPermanent = 2,
	}

	/// <summary>
	/// What happens when a player uses a portal.
	/// </summary>
	public enum PortalActivationVerdict : byte
	{
		/// <summary>The portal is already open for this player: travel.</summary>
		Open = 0,

		/// <summary>Closed, and a condition passed (or the portal asks for nothing): activate, then travel.</summary>
		Activate = 1,

		/// <summary>Closed, no condition passed, but the player holds the key: activate (spending the key when the portal says so), then travel.</summary>
		ActivateWithKey = 2,

		/// <summary>Closed, and nothing the player has opens it.</summary>
		Locked = 3,

		/// <summary>The record that decides is not loaded on this server yet. Refused for now; never treated as closed.</summary>
		NotReady = 4,
	}

	/// <summary>
	/// A portal's world-wide opening: for good, or until a moment.
	/// </summary>
	/// <remarks>
	/// Merged rather than replaced (<see cref="Merge"/>): permanent ORs and the deadline takes the
	/// later moment, which is exactly what the <c>world_portal_state</c> upsert does, so the memory
	/// copy and the row converge whichever write lands first.
	/// </remarks>
	public readonly struct PortalWorldState : IEquatable<PortalWorldState>
	{
		/// <summary>Open for good.</summary>
		public readonly bool Permanent;

		/// <summary>Unix milliseconds a timed opening ends at; 0 when never timed.</summary>
		public readonly long ActiveUntilUnixMs;

		public PortalWorldState(bool permanent, long activeUntilUnixMs)
		{
			Permanent = permanent;
			ActiveUntilUnixMs = Math.Max(0L, activeUntilUnixMs);
		}

		/// <summary>Whether the portal is open at <paramref name="nowUnixMs"/>.</summary>
		public bool IsActive(long nowUnixMs) => Permanent || ActiveUntilUnixMs > nowUnixMs;

		/// <summary>
		/// Whole seconds the opening has left at <paramref name="nowUnixMs"/>, rounded up;
		/// <see cref="PortalActivationRules.NoExpiry"/> when permanent, 0 when closed.
		/// </summary>
		public int RemainingSeconds(long nowUnixMs)
		{
			if (Permanent)
			{
				return PortalActivationRules.NoExpiry;
			}
			long remaining = ActiveUntilUnixMs - nowUnixMs;
			if (remaining <= 0)
			{
				return 0;
			}
			return (int)Math.Min(int.MaxValue, (remaining + 999L) / 1000L);
		}

		/// <summary>Whether <paramref name="other"/> says nothing this state does not already say.</summary>
		public bool Covers(PortalWorldState other) => (Permanent || !other.Permanent) && ActiveUntilUnixMs >= other.ActiveUntilUnixMs;

		/// <summary>The union of two openings: permanent if either is, open until the later deadline.</summary>
		public static PortalWorldState Merge(PortalWorldState a, PortalWorldState b)
		{
			return new PortalWorldState(a.Permanent || b.Permanent, Math.Max(a.ActiveUntilUnixMs, b.ActiveUntilUnixMs));
		}

		public bool Equals(PortalWorldState other) => Permanent == other.Permanent && ActiveUntilUnixMs == other.ActiveUntilUnixMs;

		public override bool Equals(object obj) => obj is PortalWorldState other && Equals(other);

		public override int GetHashCode() => (Permanent ? 1 : 0) ^ ActiveUntilUnixMs.GetHashCode();

		public override string ToString() => Permanent ? "permanent" : $"until {ActiveUntilUnixMs}";
	}

	/// <summary>
	/// The portal activation rules as pure functions: scope × conditions × stored state → what a use
	/// of the portal does. <see cref="PortalGate"/> is the only caller in the game; tests drive these
	/// directly.
	/// </summary>
	/// <remarks>
	/// <para><b>Unknown is not closed.</b> A server that has not yet read a character's (or a
	/// scene's) activations from the database answers <c>null</c> for "is it open", and the verdict
	/// for that is <see cref="PortalActivationVerdict.NotReady"/>, a refusal that changes nothing.
	/// Treating it as closed would let a player who already opened a portal open it again, and a
	/// portal that spends its key would take a second key for it.</para>
	/// <para><b>Requirements are any-of.</b> One passing condition opens the portal; the key is one
	/// more alternative, tried after the conditions so a player who qualifies some other way keeps
	/// the key. A portal with no conditions and no key opens for the first player who uses it — a
	/// generated portal ships that way until a designer assigns its requirements.</para>
	/// </remarks>
	public static class PortalActivationRules
	{
		/// <summary><see cref="PortalWorldState.RemainingSeconds"/> of an opening that never ends.</summary>
		public const int NoExpiry = -1;

		/// <summary>Shortest timed opening, seconds. A shorter one would close before the traveller arrived.</summary>
		public const float MinTimedSeconds = 10.0f;

		/// <summary>Longest timed opening, seconds (thirty days). A longer one is a permanent portal.</summary>
		public const float MaxTimedSeconds = 30.0f * 24.0f * 3600.0f;

		/// <summary>Whether a scope's opening is shared by the whole world.</summary>
		public static bool IsWorldScope(PortalActivationScope scope) => scope != PortalActivationScope.PerCharacter;

		/// <summary>Clamps an authored timed duration into [<see cref="MinTimedSeconds"/>, <see cref="MaxTimedSeconds"/>].</summary>
		public static float ClampDuration(float seconds)
		{
			if (float.IsNaN(seconds))
			{
				return MinTimedSeconds;
			}
			return Math.Max(MinTimedSeconds, Math.Min(MaxTimedSeconds, seconds));
		}

		/// <summary>
		/// Whether a portal is open for one player.
		/// </summary>
		/// <param name="scope">The portal's scope; it decides which record counts.</param>
		/// <param name="characterActive">The player's own record for this portal; null when not loaded.</param>
		/// <param name="world">The world record for this portal; null when the scene's rows are not loaded, default when none exists.</param>
		/// <param name="nowUnixMs">The current time.</param>
		/// <returns>True open, false closed, null not known yet.</returns>
		public static bool? IsActive(PortalActivationScope scope, bool? characterActive, PortalWorldState? world, long nowUnixMs)
		{
			if (scope == PortalActivationScope.PerCharacter)
			{
				return characterActive;
			}
			if (!world.HasValue)
			{
				return null;
			}
			return world.Value.IsActive(nowUnixMs);
		}

		/// <summary>
		/// What a use of the portal does.
		/// </summary>
		/// <param name="active">From <see cref="IsActive"/>.</param>
		/// <param name="hasRequirements">Whether the portal names any condition or a key.</param>
		/// <param name="anyConditionMet">Whether at least one of its conditions passed for the player. Ignored unless closed.</param>
		/// <param name="keyHeld">Whether the player holds the key. Ignored unless closed and no condition passed.</param>
		public static PortalActivationVerdict Decide(bool? active, bool hasRequirements, bool anyConditionMet, bool keyHeld)
		{
			if (!active.HasValue)
			{
				return PortalActivationVerdict.NotReady;
			}
			if (active.Value)
			{
				return PortalActivationVerdict.Open;
			}
			if (!hasRequirements || anyConditionMet)
			{
				return PortalActivationVerdict.Activate;
			}
			return keyHeld ? PortalActivationVerdict.ActivateWithKey : PortalActivationVerdict.Locked;
		}

		/// <summary>The world record an activation writes for a world-scoped portal.</summary>
		/// <param name="scope">The portal's scope. <see cref="PortalActivationScope.PerCharacter"/> yields the empty state.</param>
		/// <param name="durationSeconds">The timed opening's length; clamped.</param>
		/// <param name="nowUnixMs">The moment of activation.</param>
		public static PortalWorldState WorldStateFor(PortalActivationScope scope, float durationSeconds, long nowUnixMs)
		{
			switch (scope)
			{
				case PortalActivationScope.WorldPermanent:
					return new PortalWorldState(true, 0L);
				case PortalActivationScope.WorldTimed:
					return new PortalWorldState(false, nowUnixMs + (long)Math.Round(ClampDuration(durationSeconds) * 1000.0));
				default:
					return default;
			}
		}

		/// <summary>
		/// Seconds a portal stays open for one player, for the owner's client: <see cref="NoExpiry"/>
		/// for a per-character or permanent opening, the remainder for a timed one, 0 when closed or unknown.
		/// </summary>
		public static int RemainingSeconds(PortalActivationScope scope, bool? characterActive, PortalWorldState? world, long nowUnixMs)
		{
			if (scope == PortalActivationScope.PerCharacter)
			{
				return characterActive == true ? NoExpiry : 0;
			}
			return world.HasValue ? world.Value.RemainingSeconds(nowUnixMs) : 0;
		}
	}
}
