using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// The arithmetic the server uses to decide whether a hit the owner claims (see
	/// <see cref="CharacterReplicateData.HitClaimVictim"/>) could have been what the owner saw.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>What is being bounded.</b> The owner resolves its projectile against characters where it draws them; the
	/// server resolves the same sweep against where its position history says they were drawn. The history reproduces
	/// the owner's view exactly while a character moves steadily and its updates arrive on time. It cannot when the
	/// owner drew something the server never had: the straight line between two position updates two ticks apart while
	/// the character turned between them, a character held at its last update because the next was late, a position
	/// rounded to the wire's centimetre grid. Every one of those errors is a DISTANCE THE CHARACTER MOVED over a tick or
	/// two, so the tolerance is a small constant plus a few ticks of the victim's own speed: a character standing still
	/// gets almost none, because nothing about a still character can have been drawn elsewhere.
	/// </para>
	/// <para>
	/// <b>Why geometry and not a physics query.</b> The check needs one distance between one swept shape and one
	/// character, against a pose out of the history. Moving the character there, syncing physics and casting would
	/// cost a rewind scope for an answer two segments give exactly, and could not be tested without a physics scene.
	/// </para>
	/// </remarks>
	public static class HitClaimGeometry
	{
		/// <summary>Tolerance every claim gets, in metres: the wire grid, the claim's sixteenth-of-a-tick hold, float noise.</summary>
		public const float BaseTolerance = 0.1f;

		/// <summary>Ticks of the victim's own movement a claim may be off by.</summary>
		/// <remarks>
		/// Two for the straight line a client draws between updates two ticks apart, and a half for an update that
		/// arrived inside the playback margin. A character that outran this was drawn somewhere the connection, not the
		/// aim, put it; the claim is refused and the server's own sweep stands.
		/// </remarks>
		public const float ToleranceTicksOfMovement = 2.5f;

		/// <summary>The most the tolerance grows to, in metres, however fast the victim was moving.</summary>
		public const float MaximumTolerance = 1.5f;

		/// <summary>How far a claim's victim may be from the object's swept path and still be accepted.</summary>
		/// <param name="victimSpeed">The victim's speed at the moment claimed, metres a second, from the history.</param>
		/// <param name="tickDelta">Seconds per tick.</param>
		public static float Tolerance(float victimSpeed, float tickDelta)
		{
			if (float.IsNaN(victimSpeed) || victimSpeed < 0f)
			{
				victimSpeed = 0f;
			}
			return Mathf.Min(MaximumTolerance, BaseTolerance + victimSpeed * tickDelta * ToleranceTicksOfMovement);
		}

		/// <summary>
		/// The clear distance between an object swept from <paramref name="from"/> to <paramref name="to"/> and an upright
		/// character: zero when they touch or overlap.
		/// </summary>
		/// <param name="from">Where the sweep began.</param>
		/// <param name="to">Where it ended.</param>
		/// <param name="shapeReach">The swept shape's size about its centre (its largest half extent).</param>
		/// <param name="victimFeet">The character's position (its feet).</param>
		/// <param name="victimUp">The character's up.</param>
		/// <param name="victimRadius">The character's capsule radius.</param>
		/// <param name="victimHeight">The character's capsule height.</param>
		/// <param name="pointOnVictim">The point on the character's surface nearest the sweep.</param>
		/// <param name="normal">From that point toward the sweep.</param>
		public static float Gap(Vector3 from, Vector3 to, float shapeReach, Vector3 victimFeet, Vector3 victimUp,
			float victimRadius, float victimHeight, out Vector3 pointOnVictim, out Vector3 normal)
		{
			float radius = Mathf.Max(0f, victimRadius);
			float axisLength = Mathf.Max(0f, victimHeight - 2f * radius);
			Vector3 axisStart = victimFeet + victimUp * radius;
			Vector3 axisEnd = axisStart + victimUp * axisLength;

			float distance = SegmentDistance(from, to, axisStart, axisEnd, out Vector3 onSweep, out Vector3 onAxis);
			Vector3 outward = onSweep - onAxis;
			normal = outward.sqrMagnitude > 1e-10f ? outward.normalized : -SafeDirection(to - from, victimUp);
			pointOnVictim = onAxis + normal * Mathf.Min(radius, distance);
			return Mathf.Max(0f, distance - radius - Mathf.Max(0f, shapeReach));
		}

		/// <summary>The shortest distance between two segments, and the points on each where it is reached.</summary>
		public static float SegmentDistance(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2, out Vector3 c1, out Vector3 c2)
		{
			Vector3 d1 = q1 - p1;
			Vector3 d2 = q2 - p2;
			Vector3 r = p1 - p2;
			float a = Vector3.Dot(d1, d1);
			float e = Vector3.Dot(d2, d2);
			float f = Vector3.Dot(d2, r);
			float s;
			float t;

			const float Epsilon = 1e-10f;
			if (a <= Epsilon && e <= Epsilon)
			{
				c1 = p1;
				c2 = p2;
				return (c1 - c2).magnitude;
			}
			if (a <= Epsilon)
			{
				s = 0f;
				t = Mathf.Clamp01(f / e);
			}
			else
			{
				float c = Vector3.Dot(d1, r);
				if (e <= Epsilon)
				{
					t = 0f;
					s = Mathf.Clamp01(-c / a);
				}
				else
				{
					float b = Vector3.Dot(d1, d2);
					float denominator = a * e - b * b;
					s = denominator > Epsilon ? Mathf.Clamp01((b * f - c * e) / denominator) : 0f;
					t = (b * s + f) / e;
					if (t < 0f)
					{
						t = 0f;
						s = Mathf.Clamp01(-c / a);
					}
					else if (t > 1f)
					{
						t = 1f;
						s = Mathf.Clamp01((b - c) / a);
					}
				}
			}

			c1 = p1 + d1 * s;
			c2 = p2 + d2 * t;
			return (c1 - c2).magnitude;
		}

		private static Vector3 SafeDirection(Vector3 direction, Vector3 fallback)
		{
			return direction.sqrMagnitude > 1e-10f ? direction.normalized : fallback;
		}
	}
}
