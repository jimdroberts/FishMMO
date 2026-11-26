using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// The duel's rules that need no scene: the defaults, the ground's geometry (where the two start, what counts as
	/// inside) and the wording both peers use. Pure, so the tests check them directly.
	/// </summary>
	public static class DuelRules
	{
		/// <summary>The duel ground's size when none is configured: 15 m each way (issue #250).</summary>
		public static readonly Vector3 DefaultArenaSize = new Vector3(15f, 15f, 15f);

		/// <summary>The smallest side a duel ground may have: room for two starts apart and a step each way.</summary>
		public const float MinimumArenaSide = 6f;

		/// <summary>The largest side a duel ground may have.</summary>
		public const float MaximumArenaSide = 60f;

		/// <summary>Seconds from the teleport to the first blow.</summary>
		public const float DefaultCountdownSeconds = 10f;

		/// <summary>Seconds a fight may last before it is called a draw.</summary>
		public const float DefaultTimeLimitSeconds = 180f;

		/// <summary>Seconds a challenge waits for an answer.</summary>
		public const float DefaultInviteTtlSeconds = 30f;

		/// <summary>How far apart, in metres, two players may be when one challenges the other.</summary>
		public const float DefaultChallengeRange = 20f;

		/// <summary>How far each start stands in from the ground's end wall, in metres.</summary>
		public const float StartEdgeMargin = 2.5f;

		/// <summary>The closest the two starts may be to the centre: three metres apart at the least.</summary>
		public const float MinimumStartOffset = 1.5f;

		/// <summary>How far below the lower start the ground's floor lies, so a dip between them is still inside.</summary>
		public const float FloorMargin = 1f;

		/// <summary>
		/// Metres a duellist may stand outside the ground before it counts as leaving: the server reads a position the
		/// player's own movement just set, and a step onto the line must not forfeit.
		/// </summary>
		public const float DefaultLeaveTolerance = 0.25f;

		/// <summary>Within this many metres of a wall the duellist is warned that stepping out forfeits.</summary>
		public const float EdgeWarningDistance = 2f;

		/// <summary>The size made safe: every side between <see cref="MinimumArenaSide"/> and <see cref="MaximumArenaSide"/>.</summary>
		public static Vector3 ClampSize(Vector3 size)
		{
			return new Vector3(ClampSide(size.x), ClampSide(size.y), ClampSide(size.z));
		}

		private static float ClampSide(float side)
		{
			return float.IsNaN(side) ? DefaultArenaSize.x : Mathf.Clamp(side, MinimumArenaSide, MaximumArenaSide);
		}

		/// <summary>The ground's turn as a rotation.</summary>
		public static Quaternion Rotation(in DuelArena arena)
		{
			return Quaternion.Euler(0f, arena.Yaw, 0f);
		}

		/// <summary>How far each start stands from the centre along the ground's length.</summary>
		public static float StartOffset(Vector3 size)
		{
			return Mathf.Max(MinimumStartOffset, size.z * 0.5f - StartEdgeMargin);
		}

		/// <summary>The bearing, in degrees about the world's up, from <paramref name="from"/> to <paramref name="to"/>.</summary>
		public static float Bearing(Vector3 from, Vector3 to)
		{
			Vector3 d = to - from;
			d.y = 0f;
			return d.sqrMagnitude < 1e-6f ? 0f : Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
		}

		/// <summary>
		/// The two starts on a level ground through <paramref name="center"/>: the first behind the centre along the
		/// ground's length, the second ahead of it, both at the centre's height.
		/// </summary>
		public static void Starts(Vector3 center, Vector3 size, float yaw, out Vector3 first, out Vector3 second)
		{
			Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
			float offset = StartOffset(size);
			first = center - forward * offset;
			second = center + forward * offset;
		}

		/// <summary>The facing of the first start (towards the second); the second faces the opposite way.</summary>
		public static Quaternion FirstFacing(float yaw) => Quaternion.Euler(0f, yaw, 0f);

		/// <summary>The facing of the second start (towards the first).</summary>
		public static Quaternion SecondFacing(float yaw) => Quaternion.Euler(0f, yaw + 180f, 0f);

		/// <summary>
		/// The ground's box for two starts set down at the given heights: its floor <see cref="FloorMargin"/> below the
		/// lower one, its centre between them. False when the two are too far apart in height for a duellist to stand
		/// at the higher one with headroom (<paramref name="standingHeight"/>) below the roof.
		/// </summary>
		public static bool TryBuildArena(Vector3 firstGround, Vector3 secondGround, Vector3 size, float yaw, float standingHeight, out DuelArena arena)
		{
			size = ClampSize(size);
			float floor = Mathf.Min(firstGround.y, secondGround.y) - FloorMargin;
			float roof = floor + size.y;
			Vector3 middle = (firstGround + secondGround) * 0.5f;
			arena = new DuelArena
			{
				Center = new Vector3(middle.x, floor + size.y * 0.5f, middle.z),
				Size = size,
				Yaw = yaw,
			};
			return Mathf.Max(firstGround.y, secondGround.y) + Mathf.Max(0f, standingHeight) <= roof;
		}

		/// <summary><paramref name="point"/> in the ground's own frame: x across, y up from the centre, z along.</summary>
		public static Vector3 ToLocal(in DuelArena arena, Vector3 point)
		{
			return Quaternion.Inverse(Rotation(arena)) * (point - arena.Center);
		}

		/// <summary>Whether <paramref name="point"/> is inside the ground, or no further than <paramref name="tolerance"/> out of it.</summary>
		public static bool Contains(in DuelArena arena, Vector3 point, float tolerance = 0f)
		{
			Vector3 local = ToLocal(arena, point);
			Vector3 half = arena.Size * 0.5f;
			tolerance = Mathf.Max(0f, tolerance);
			return Mathf.Abs(local.x) <= half.x + tolerance &&
				Mathf.Abs(local.y) <= half.y + tolerance &&
				Mathf.Abs(local.z) <= half.z + tolerance;
		}

		/// <summary>
		/// How far <paramref name="point"/> is from the nearest wall, floor or roof of the ground: positive inside, negative
		/// outside (as far as the furthest side it is past).
		/// </summary>
		public static float DistanceToEdge(in DuelArena arena, Vector3 point)
		{
			Vector3 local = ToLocal(arena, point);
			Vector3 half = arena.Size * 0.5f;
			float dx = half.x - Mathf.Abs(local.x);
			float dy = half.y - Mathf.Abs(local.y);
			float dz = half.z - Mathf.Abs(local.z);
			return Mathf.Min(dx, Mathf.Min(dy, dz));
		}

		/// <summary>The yaws tried in turn for a ground: straight from one player to the other first, then turned away from it.</summary>
		public static readonly float[] YawOffsets = { 0f, 45f, -45f, 90f, -90f, 135f, -135f, 180f };

		/// <summary>Player wording for a refused or finished challenge.</summary>
		public static string DescribeRequest(DuelRequestFailure failure, string targetName)
		{
			string who = string.IsNullOrEmpty(targetName) ? "That player" : targetName;
			switch (failure)
			{
				case DuelRequestFailure.Sent: return $"You challenge {who} to a duel.";
				case DuelRequestFailure.TargetUnavailable: return $"{who} cannot duel right now.";
				case DuelRequestFailure.TargetBusy: return $"{who} is already duelling or answering another challenge.";
				case DuelRequestFailure.SelfBusy: return "You already have a duel or a challenge in hand.";
				case DuelRequestFailure.OutOfRange: return $"{who} is too far away to challenge.";
				case DuelRequestFailure.Declined: return $"{who} declines your challenge.";
				case DuelRequestFailure.Expired: return $"{who} did not answer your challenge.";
				case DuelRequestFailure.Throttled: return "Wait a moment before challenging again.";
				case DuelRequestFailure.CannotAct: return "You cannot duel right now.";
				case DuelRequestFailure.InCombat: return "Neither of you may be in combat to start a duel.";
				case DuelRequestFailure.NotHere: return "Duels are fought in the open world.";
				case DuelRequestFailure.NoRoom: return "There is no level ground here for a duel.";
				default: return "The challenge could not be made.";
			}
		}

		/// <summary>Player wording for the end of a duel.</summary>
		public static string DescribeEnd(DuelOutcome outcome, DuelEndReason reason, string opponentName)
		{
			string them = string.IsNullOrEmpty(opponentName) ? "your opponent" : opponentName;
			switch (outcome)
			{
				case DuelOutcome.Won:
					switch (reason)
					{
						case DuelEndReason.Yielded: return $"{them} yields. You win the duel.";
						case DuelEndReason.LeftGround: return $"{them} left the duel ground. You win the duel.";
						case DuelEndReason.Left: return $"{them} left. You win the duel.";
						case DuelEndReason.Died: return $"{them} has fallen. You win the duel.";
						default: return $"You have defeated {them}.";
					}
				case DuelOutcome.Lost:
					switch (reason)
					{
						case DuelEndReason.Yielded: return "You yield. The duel is lost.";
						case DuelEndReason.LeftGround: return "You left the duel ground. The duel is lost.";
						case DuelEndReason.Left: return "You left. The duel is lost.";
						case DuelEndReason.Died: return "You have fallen. The duel is lost.";
						default: return $"{them} has defeated you.";
					}
				case DuelOutcome.Draw:
					return reason == DuelEndReason.LeftGround ? "You both left the duel ground. A draw." : "Time is up. The duel is a draw.";
				default:
					return "The duel was called off.";
			}
		}
	}
}
