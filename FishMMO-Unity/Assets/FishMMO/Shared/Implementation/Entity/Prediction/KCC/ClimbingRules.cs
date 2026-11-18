using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>Where a climber is in its climb.</summary>
	/// <remarks>Carried in the reconcile as a byte (<c>KinematicCharacterMotorState.ClimbPhase</c>): append only.</remarks>
	public enum ClimbPhase : byte
	{
		/// <summary>Not climbing.</summary>
		None = 0,
		/// <summary>On the climbable: up, down and across it at will.</summary>
		Holding = 1,
		/// <summary>At the top and going off it: up past the edge, then over it onto the upper floor.</summary>
		SteppingOff = 2,
		/// <summary>Come from the upper floor and going onto it: out over the edge, then down to the top hold.</summary>
		SteppingOn = 3,
	}

	/// <summary>A character's climb: which climbable, how far through it, and the ticks its current step has left.</summary>
	public struct ClimbState
	{
		/// <summary>The climbable's index in the scene's list (<see cref="SceneClimbables"/>), −1 when not climbing.</summary>
		public int Index;
		public ClimbPhase Phase;
		/// <summary>
		/// While stepping on or off: ticks left before the step is given up. While not climbing: ticks left before the
		/// character may take hold of anything again.
		/// </summary>
		public byte Ticks;

		public bool IsClimbing => Phase != ClimbPhase.None && Index >= 0;

		public static ClimbState Free => new ClimbState { Index = -1 };
	}

	/// <summary>One tick's controls, as climbing reads them.</summary>
	public struct ClimbIntent
	{
		/// <summary>−1..1: up the climbable, or down it (<see cref="ClimbingRules.Axes"/>).</summary>
		public float Up;
		/// <summary>−1..1: a shuffle across a ladder, along the right of its hang direction (<see cref="ClimbingRules.Right"/>).</summary>
		public float Side;
		/// <summary>The move input in the world, the way the camera turns it: what taking hold reads.</summary>
		public Vector3 Move;
		/// <summary>Jump: let go and push off.</summary>
		public bool Jump;
		/// <summary>Crouch held: slide down.</summary>
		public bool Slide;
		/// <summary>There is floor under the climber's feet (the caller's probe of the scene): going down, it steps off.</summary>
		public bool FloorBelow;
	}

	/// <summary>What one tick of climbing came to.</summary>
	public struct ClimbStep
	{
		/// <summary>The velocity the motor is given.</summary>
		public Vector3 Velocity;
		/// <summary>The climber let go this tick (at the bottom, off the top, by a jump, or having lost its hold).</summary>
		public bool Released;
		/// <summary>It let go by jumping off.</summary>
		public bool Jumped;
	}

	/// <summary>
	/// The climbing movement mode's rules, as pure functions of the climbable, the climber's feet and the tick's controls:
	/// the KCC runs them on the server and on the predicting owner alike (and in every replay), so the same inputs give the
	/// same climb; the AI walks NPCs along the same geometry; and tests run them with no motor.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Stateless but for <see cref="ClimbState"/>.</b> Where the climber is on the climbable is read from its feet each
	/// tick (height up the line, offset across it), never stored, so the reconcile only needs which climbable, the phase
	/// and the step's ticks. A rope's side is likewise the side its climber is on.
	/// </para>
	/// <para>
	/// <b>The feet are the character's pivot</b> (the capsule's bottom, as the KCC places it). A climber hangs
	/// <c>hang</c> metres out of the line (the body's radius and <see cref="Constants.Character.ClimbHangClearance"/>), so
	/// its capsule never touches the climbable's own line.
	/// </para>
	/// </remarks>
	public static class ClimbingRules
	{
		/// <summary>The least rise, metres, of a climbable worth climbing.</summary>
		public const float MinRise = 0.5f;
		/// <summary>The least share of its length a climbable rises by (a lean of about 45 degrees at most).</summary>
		public const float MinUprightness = 0.7f;
		/// <summary>How far below a climbable's foot a character can still take hold of it, metres (its feet down a slope).</summary>
		public const float GrabBelowMetres = 0.5f;
		/// <summary>How far above or below the top a character on the upper floor can be and still step on, metres.</summary>
		public const float TopGrabHeight = 0.5f;
		/// <summary>How far past the landing point a character can be and still step on, metres.</summary>
		public const float TopGrabBeyond = 0.6f;
		/// <summary>How far either side of the climbable's width a character can be and still step on from the top, metres.</summary>
		public const float TopGrabSide = 0.4f;
		/// <summary>How squarely the move input has to point at a climbable to take hold (a cosine).</summary>
		public const float IntentDot = 0.6f;
		/// <summary>The least move input that counts as meaning to go somewhere.</summary>
		public const float MinIntent = 0.25f;
		/// <summary>How far off its climbable a climber may be before its hold is lost (a teleport, a shove), metres.</summary>
		public const float LostMetres = 3f;
		/// <summary>How near a target counts as there, metres.</summary>
		public const float ArriveMetres = 0.05f;
		/// <summary>How near the bottom (or the top hold) counts as at it, metres along the line.</summary>
		public const float SettleMetres = 0.02f;
		/// <summary>How far round a character the scene's climbables are looked for, metres: past any reach a rule allows.</summary>
		public const float QueryReach = 2.5f;

		/// <summary>Whether a climbable is one that can be climbed: finite, rising at least <see cref="MinRise"/>, near upright.</summary>
		public static bool IsValid(in SceneClimbable c)
		{
			if (!Finite(c.Bottom) || !Finite(c.Top) || !Finite(c.Out) || !Finite(c.TopExit) || float.IsNaN(c.HalfWidth) || float.IsInfinity(c.HalfWidth))
			{
				return false;
			}
			Vector3 axis = c.Top - c.Bottom;
			float length = axis.magnitude;
			return axis.y >= MinRise && axis.y >= MinUprightness * length;
		}

		/// <summary>The share of <see cref="Constants.Character.ClimbSpeed"/> a kind is climbed at.</summary>
		public static float KindShare(SceneClimbableKind kind)
		{
			switch (kind)
			{
				case SceneClimbableKind.RopeLadder: return 0.85f;
				case SceneClimbableKind.Rope: return 0.7f;
				default: return 1f;
			}
		}

		/// <summary>Whether a climber can shuffle across it: a ladder with a wall behind it, not a rope.</summary>
		public static bool CanShuffle(in SceneClimbable c) => c.Kind != SceneClimbableKind.Rope && Flat(c.Out).sqrMagnitude > 1e-4f;

		/// <summary>The unit direction from the bottom to the top, and the length of the climb.</summary>
		public static Vector3 Axis(in SceneClimbable c, out float length)
		{
			Vector3 axis = c.Top - c.Bottom;
			length = axis.magnitude;
			return length > 1e-4f ? axis / length : Vector3.up;
		}

		/// <summary>How far along the line (from the bottom, metres) a pair of feet are, by their height.</summary>
		public static float Along(in SceneClimbable c, Vector3 feet)
		{
			Vector3 axis = Axis(in c, out _);
			return (feet.y - c.Bottom.y) / Mathf.Max(1e-3f, axis.y);
		}

		/// <summary>The highest a climber's feet go while holding on (metres along the line), before it steps off the top.</summary>
		public static float TopHold(in SceneClimbable c)
		{
			Axis(in c, out float length);
			return Mathf.Max(0f, length - Constants.Character.ClimbTopHoldMetres);
		}

		/// <summary>The way off at the top onto the upper floor: its <see cref="SceneClimbable.TopExit"/>, else away from where a climber hangs.</summary>
		public static Vector3 Exit(in SceneClimbable c)
		{
			Vector3 exit = Flat(c.TopExit);
			if (exit.sqrMagnitude > 1e-4f)
			{
				return exit.normalized;
			}
			Vector3 back = -Flat(c.Out);
			return back.sqrMagnitude > 1e-4f ? back.normalized : Vector3.forward;
		}

		/// <summary>Where a climber stepping off the top stands on the upper floor.</summary>
		public static Vector3 Landing(in SceneClimbable c) => c.Top + Exit(in c) * Constants.Character.ClimbLandingMetres;

		/// <summary>The two ends a NavMesh link joins: the climb's foot on the lower floor and its landing on the upper one.</summary>
		public static void LinkEnds(in SceneClimbable c, out Vector3 lower, out Vector3 upper)
		{
			lower = c.Bottom;
			upper = Landing(in c);
		}

		/// <summary>
		/// The horizontal unit direction a climber hangs off the line in: the climbable's <see cref="SceneClimbable.Out"/>,
		/// or for a free rope the side the feet are on (the pit side of the top, −<see cref="Exit"/>, when stepping on from it).
		/// </summary>
		public static Vector3 HangDirection(in SceneClimbable c, Vector3 feet, ClimbPhase phase)
		{
			Vector3 o = Flat(c.Out);
			if (o.sqrMagnitude > 1e-4f)
			{
				return o.normalized;
			}
			if (phase != ClimbPhase.SteppingOn)
			{
				Vector3 axis = Axis(in c, out float length);
				float s = Mathf.Clamp(Along(in c, feet), 0f, length);
				Vector3 side = Flat(feet - (c.Bottom + axis * s));
				if (side.sqrMagnitude > 1e-4f)
				{
					return side.normalized;
				}
			}
			return -Exit(in c);
		}

		/// <summary>The way a climber faces: into the climbable while on it, and along the way off once it is over the top.</summary>
		public static Vector3 Facing(in SceneClimbable c, ClimbPhase phase, Vector3 feet)
		{
			if (phase == ClimbPhase.SteppingOff && feet.y >= c.Top.y + Constants.Character.ClimbStepClearance - SettleMetres)
			{
				return Exit(in c);
			}
			return -HangDirection(in c, feet, phase);
		}

		/// <summary>
		/// The climb's axes from the move input in the world: pushing into the climbable climbs it, pulling away goes down,
		/// and pushing along it shuffles across. The same push that takes hold (<see cref="TryAttach"/>) climbs on, and the
		/// one that walks off the top onto a ladder carries on down it, whichever way the camera looks; a climber facing
		/// its ladder with the camera behind it climbs on forward and comes down on back, as anyone would expect.
		/// </summary>
		public static void Axes(in SceneClimbable c, ClimbPhase phase, Vector3 feet, Vector3 move, out float up, out float side)
		{
			Vector3 hangDirection = HangDirection(in c, feet, phase);
			Vector3 wish = Flat(move);
			up = Mathf.Clamp(Vector3.Dot(wish, -hangDirection), -1f, 1f);
			side = Mathf.Clamp(Vector3.Dot(wish, Right(hangDirection)), -1f, 1f);
		}

		/// <summary>Whole ticks covering <paramref name="seconds"/> at a tick of <paramref name="deltaTime"/>, 1..255.</summary>
		public static byte TicksFor(float seconds, float deltaTime)
		{
			if (seconds <= 0f)
			{
				return 0;
			}
			if (deltaTime <= 0f)
			{
				return byte.MaxValue;
			}
			return (byte)Mathf.Clamp(Mathf.CeilToInt(seconds / deltaTime), 1, byte.MaxValue);
		}

		/// <summary>
		/// Whether a character can take hold of a climbable now, and how: <see cref="ClimbPhase.Holding"/> reached from
		/// the floor at its foot or anywhere along it (in the air too) while moving toward it; or
		/// <see cref="ClimbPhase.SteppingOn"/> from the upper floor, walking off the top's edge onto it.
		/// <paramref name="distance"/> ranks the climbables a character could take, nearest first.
		/// </summary>
		public static bool TryAttach(in SceneClimbable c, Vector3 feet, Vector3 move, bool grounded, float hang, out ClimbPhase phase, out float distance)
		{
			phase = ClimbPhase.None;
			distance = float.MaxValue;
			if (!IsValid(in c))
			{
				return false;
			}
			Vector3 wish = Flat(move);
			if (wish.sqrMagnitude < MinIntent * MinIntent || !Finite(feet))
			{
				return false;
			}
			wish.Normalize();
			float halfWidth = Mathf.Max(0f, c.HalfWidth);

			// From the top: on the upper floor at the landing, walking toward the edge.
			if (grounded && Mathf.Abs(feet.y - c.Top.y) <= TopGrabHeight)
			{
				Vector3 exit = Exit(in c);
				Vector3 fromTop = Flat(feet - c.Top);
				float beyond = Vector3.Dot(fromTop, exit);
				float across = Vector3.Dot(fromTop, Right(exit));
				if (beyond >= -0.1f && beyond <= Constants.Character.ClimbLandingMetres + TopGrabBeyond &&
					Mathf.Abs(across) <= halfWidth + TopGrabSide &&
					Vector3.Dot(wish, -exit) >= IntentDot)
				{
					phase = ClimbPhase.SteppingOn;
					distance = fromTop.magnitude;
					return true;
				}
			}

			// Along it: between a little under its foot and the top hold, within reach, moving at it.
			float s = Along(in c, feet);
			if (s < -GrabBelowMetres || s > TopHold(in c) + 0.05f)
			{
				return false;
			}
			Vector3 axis = Axis(in c, out float length);
			Vector3 offset = Flat(feet - (c.Bottom + axis * Mathf.Clamp(s, 0f, length)));
			float reach = Constants.Character.ClimbReach;
			Vector3 o = Flat(c.Out);
			if (o.sqrMagnitude > 1e-4f)
			{
				o.Normalize();
				float outward = Vector3.Dot(offset, o);
				float across = Vector3.Dot(offset, Right(o));
				if (outward < -0.1f || outward > hang + reach || Mathf.Abs(across) > halfWidth + reach || Vector3.Dot(wish, -o) < IntentDot)
				{
					return false;
				}
			}
			else
			{
				float radial = offset.magnitude;
				if (radial > hang + halfWidth + reach)
				{
					return false;
				}
				if (radial > ArriveMetres && Vector3.Dot(wish, -offset / radial) < IntentDot)
				{
					return false;
				}
			}
			phase = ClimbPhase.Holding;
			distance = offset.magnitude;
			return true;
		}

		/// <summary>Starts a climb on climbable <paramref name="index"/>; a step on from the top is given its ticks.</summary>
		public static void Attach(ref ClimbState state, int index, ClimbPhase phase, float deltaTime)
		{
			state.Index = index;
			state.Phase = phase;
			state.Ticks = phase == ClimbPhase.SteppingOn || phase == ClimbPhase.SteppingOff
				? TicksFor(Constants.Character.ClimbStepTimeoutSeconds, deltaTime)
				: (byte)0;
		}

		/// <summary>Ends a climb, and keeps the character from taking hold again for <paramref name="regrabSeconds"/>.</summary>
		public static void Release(ref ClimbState state, float regrabSeconds, float deltaTime)
		{
			state.Index = -1;
			state.Phase = ClimbPhase.None;
			state.Ticks = TicksFor(regrabSeconds, deltaTime);
		}

		/// <summary>One tick off the wait before a character not climbing may take hold again.</summary>
		public static void TickCooldown(ref ClimbState state)
		{
			if (!state.IsClimbing && state.Ticks > 0)
			{
				state.Ticks--;
			}
		}

		/// <summary>
		/// One tick of a climb: the velocity that carries the feet where this tick's controls put them, and the change of
		/// phase that comes of it. <paramref name="speedScale"/> is the character's speed attributes (1 = stock).
		/// </summary>
		public static ClimbStep Step(in SceneClimbable c, ref ClimbState state, Vector3 feet, in ClimbIntent intent, float hang, float speedScale, float deltaTime)
		{
			ClimbStep step = default;
			if (!state.IsClimbing || !IsValid(in c) || deltaTime <= 0f || !Finite(feet))
			{
				Release(ref state, 0f, deltaTime);
				step.Released = true;
				return step;
			}

			Vector3 axis = Axis(in c, out float length);
			float s = Along(in c, feet);
			Vector3 offset = Flat(feet - (c.Bottom + axis * Mathf.Clamp(s, 0f, length)));
			if (offset.sqrMagnitude > LostMetres * LostMetres || s < -LostMetres || s > length + LostMetres)
			{
				// Moved off it by something other than climbing (a teleport, a shove): the hold is gone.
				Release(ref state, 0f, deltaTime);
				step.Released = true;
				return step;
			}

			Vector3 hangDirection = HangDirection(in c, feet, state.Phase);
			float topHold = TopHold(in c);
			float riseY = c.Top.y + Constants.Character.ClimbStepClearance;
			Vector3 topHang = new Vector3(c.Top.x + hangDirection.x * hang, riseY, c.Top.z + hangDirection.z * hang);

			if (state.Phase == ClimbPhase.Holding)
			{
				if (intent.Jump)
				{
					step.Velocity = hangDirection * Constants.Character.ClimbJumpOffSpeed + Vector3.up * Constants.Character.ClimbJumpOffUpSpeed;
					Release(ref state, Constants.Character.ClimbJumpRegrabSeconds, deltaTime);
					step.Released = true;
					step.Jumped = true;
					return step;
				}

				float up = Mathf.Clamp(intent.Up, -1f, 1f);
				float rate = up * Constants.Character.ClimbSpeed * KindShare(c.Kind) * Mathf.Clamp(speedScale, 0f, 3f);
				if (intent.Slide && up <= 0f)
				{
					rate = -Constants.Character.ClimbSlideSpeed;
				}
				if (rate > 0f && s >= topHold - SettleMetres)
				{
					// At the top hold and still climbing: off over the top.
					Attach(ref state, state.Index, ClimbPhase.SteppingOff, deltaTime);
					return SteppingOff(in c, ref state, feet, hangDirection, topHang, riseY, deltaTime);
				}
				if (rate < 0f && (s <= SettleMetres || intent.FloorBelow))
				{
					// At the foot and still going down: onto the floor. The ground takes it from here.
					Release(ref state, Constants.Character.ClimbRegrabSeconds, deltaTime);
					step.Released = true;
					return step;
				}

				float target = Mathf.Clamp(s + rate * deltaTime, 0f, topHold);
				float lateral = 0f;
				Vector3 right = Right(hangDirection);
				if (CanShuffle(in c))
				{
					float halfWidth = Mathf.Max(0f, c.HalfWidth);
					lateral = Mathf.Clamp(Vector3.Dot(offset, right) + Mathf.Clamp(intent.Side, -1f, 1f) * Constants.Character.ClimbShuffleSpeed * deltaTime, -halfWidth, halfWidth);
				}
				Vector3 hold = c.Bottom + axis * target + hangDirection * hang + right * lateral;
				step.Velocity = Toward(feet, hold, Constants.Character.ClimbSnapSpeed, deltaTime);
				return step;
			}

			if (state.Phase == ClimbPhase.SteppingOff)
			{
				step = SteppingOff(in c, ref state, feet, hangDirection, topHang, riseY, deltaTime);
				if (!step.Released && state.Ticks > 0 && --state.Ticks == 0)
				{
					// Something is in the way over the top: back onto the climbable, to try again or go down.
					state.Phase = ClimbPhase.Holding;
				}
				return step;
			}

			// Stepping on from the top: out over the edge with the feet lifted clear of it, then straight down to the top hold.
			if (feet.y > c.Top.y - 0.3f && Flat(feet - topHang).magnitude > ArriveMetres)
			{
				step.Velocity = Toward(feet, topHang, Constants.Character.ClimbMantleSpeed, deltaTime);
			}
			else
			{
				float holdY = (c.Bottom + axis * topHold).y;
				Vector3 below = new Vector3(topHang.x, holdY, topHang.z);
				if (feet.y <= holdY + ArriveMetres)
				{
					state.Phase = ClimbPhase.Holding;
					state.Ticks = 0;
					Vector3 hold = c.Bottom + axis * topHold + hangDirection * hang;
					step.Velocity = Toward(feet, hold, Constants.Character.ClimbSnapSpeed, deltaTime);
					return step;
				}
				step.Velocity = Toward(feet, below, Constants.Character.ClimbMantleSpeed, deltaTime);
			}
			if (state.Ticks > 0 && --state.Ticks == 0)
			{
				// Could not get over the edge (something in the way): stay on the floor.
				Release(ref state, Constants.Character.ClimbRegrabSeconds, deltaTime);
				step.Velocity = Vector3.zero;
				step.Released = true;
			}
			return step;
		}

		/// <summary>Up past the top's edge, then over it to the landing, where the climb ends.</summary>
		private static ClimbStep SteppingOff(in SceneClimbable c, ref ClimbState state, Vector3 feet, Vector3 hangDirection, Vector3 topHang, float riseY, float deltaTime)
		{
			ClimbStep step = default;
			if (feet.y < riseY - SettleMetres)
			{
				step.Velocity = Toward(feet, topHang, Constants.Character.ClimbMantleSpeed, deltaTime);
				return step;
			}
			Vector3 landing = Landing(in c);
			Vector3 over = new Vector3(landing.x, riseY, landing.z);
			if (Flat(feet - over).magnitude <= ArriveMetres)
			{
				Release(ref state, Constants.Character.ClimbRegrabSeconds, deltaTime);
				step.Released = true;
				return step;
			}
			step.Velocity = Toward(feet, over, Constants.Character.ClimbMantleSpeed, deltaTime);
			return step;
		}

		/// <summary>
		/// The way an NPC takes along a climbable's NavMesh link, as points for its feet: from where the link starts, to
		/// its hold at the foot, up to the top lifted clear of the edge, over to the landing, and on to where the link
		/// ends; or the same the other way round going down.
		/// </summary>
		public static void TraversalPoints(in SceneClimbable c, bool upward, float hang, Vector3 start, Vector3 end, List<Vector3> into)
		{
			into.Clear();
			Vector3 hangDirection = HangDirection(in c, c.Bottom, ClimbPhase.SteppingOn);
			float riseY = c.Top.y + Constants.Character.ClimbStepClearance;
			Vector3 foot = new Vector3(c.Bottom.x + hangDirection.x * hang, c.Bottom.y, c.Bottom.z + hangDirection.z * hang);
			Vector3 topHang = new Vector3(c.Top.x + hangDirection.x * hang, riseY, c.Top.z + hangDirection.z * hang);
			Vector3 landing = Landing(in c);
			Vector3 over = new Vector3(landing.x, riseY, landing.z);
			into.Add(start);
			if (upward)
			{
				into.Add(foot);
				into.Add(topHang);
				into.Add(over);
			}
			else
			{
				into.Add(over);
				into.Add(topHang);
				into.Add(foot);
			}
			into.Add(end);
		}

		/// <summary>The length of a run of points, metres.</summary>
		public static float PathLength(List<Vector3> points)
		{
			float length = 0f;
			for (int i = 1; i < points.Count; i++)
			{
				length += Vector3.Distance(points[i - 1], points[i]);
			}
			return length;
		}

		/// <summary>The point <paramref name="distance"/> metres along a run of points (clamped to its ends), and the leg it is on.</summary>
		public static Vector3 PointAlong(List<Vector3> points, float distance, out int leg)
		{
			leg = 0;
			if (points.Count == 0)
			{
				return Vector3.zero;
			}
			float left = Mathf.Max(0f, distance);
			for (int i = 1; i < points.Count; i++)
			{
				float span = Vector3.Distance(points[i - 1], points[i]);
				if (left <= span)
				{
					leg = i - 1;
					return span > 1e-5f ? Vector3.Lerp(points[i - 1], points[i], left / span) : points[i];
				}
				left -= span;
			}
			leg = Mathf.Max(0, points.Count - 2);
			return points[points.Count - 1];
		}

		/// <summary>The velocity that moves <paramref name="from"/> toward <paramref name="to"/> this tick, at most <paramref name="speed"/>.</summary>
		public static Vector3 Toward(Vector3 from, Vector3 to, float speed, float deltaTime)
		{
			Vector3 delta = to - from;
			float most = speed * deltaTime;
			float distance = delta.magnitude;
			if (distance > most && distance > 1e-6f)
			{
				delta *= most / distance;
			}
			return delta / deltaTime;
		}

		/// <summary>The horizontal right of a horizontal facing direction (up × it).</summary>
		public static Vector3 Right(Vector3 horizontal) => new Vector3(horizontal.z, 0f, -horizontal.x);

		public static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

		private static bool Finite(Vector3 v) => !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
	}
}
