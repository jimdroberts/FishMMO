using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Where a player's shots go: from the character's eye to the point under the crosshair.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why the aim is not simply the camera's forward.</b> Shots start at the character's eye
	/// (<see cref="CharacterAimOrigin"/>, derived from the motor on every peer so a client cannot pick it), while the
	/// crosshair is on the camera's line, and the camera is never at the eye: in third person it sits behind and below
	/// it (the follow point is chest high), and over the shoulder it is also half a metre to the side. A shot along the
	/// camera's forward from the eye travels a line PARALLEL to the one the player aimed down, so it lands that offset
	/// away from the crosshair at every range: never on it.
	/// </para>
	/// <para>
	/// <b>The fix is to make the two lines meet.</b> The camera's ray is traced into the world to find what the
	/// crosshair is on (the apex), and the aim is the direction from the eye to that point. The camera and the eye then
	/// form a V whose point is the thing the player is looking at, at whatever range it is.
	/// </para>
	/// <para>
	/// <b>Two guards keep the V sane.</b> The camera's ray starts level with the eye, not at the camera, so nothing between
	/// the camera and the character (a wall behind them, their own back) can be the apex. And the apex is never closer than
	/// <see cref="MinConvergenceDistance"/> beyond that start: a crosshair resting on a post at the character's elbow would
	/// otherwise swing the aim to the side by up to ninety degrees.
	/// </para>
	/// <para>
	/// <b>The body is angled into the V too.</b> The character faces, and walks toward, the same point, measured from
	/// where it is drawn (<see cref="BodyHeading"/>), so camera, character and shot all look at one point. The body's
	/// point is held at least <see cref="BodyMinConvergenceDistance"/> out, so the crosshair crossing from a near object
	/// to a far one turns the body by a few degrees rather than snapping it. The replicate carries the body's heading as
	/// a small yaw offset from the aim (<see cref="CharacterReplicateData.HeadingOffset"/>).
	/// </para>
	/// <para>
	/// Computed by the owner only, in KCCPlayer.PopulateInput: the camera exists on no other peer. The result is quantised
	/// there before the owner predicts from it, like every other input (see <see cref="AimDirectionCompression"/>).
	/// </para>
	/// </remarks>
	public static class AimConvergence
	{
		/// <summary>
		/// How far the crosshair's ray is traced, and where the apex is put when it hits nothing (open sky, the horizon).
		/// </summary>
		/// <remarks>
		/// Far enough that the eye's remaining offset from the camera's line is a few thousandths of a radian there; the
		/// trace is once a tick on the owner only, so its length costs nothing measurable.
		/// </remarks>
		public const float FarDistance = 250f;

		/// <summary>The nearest the apex may be, beyond the eye's level along the camera's ray.</summary>
		public const float MinConvergenceDistance = 2f;

		/// <summary>
		/// The nearest the point the BODY turns to (and walks toward) may be, beyond the character's level along the
		/// camera's ray.
		/// </summary>
		/// <remarks>
		/// Farther than the shot's minimum on purpose. The apex jumps whenever the crosshair crosses from something near to
		/// something far, and the body follows it with the facing's own snap; at 6 m the shoulder's half metre turns the
		/// body at most about five degrees, which reads as leaning into the aim rather than twitching. Shots are not held
		/// to this: they still converge exactly from <see cref="MinConvergenceDistance"/>.
		/// </remarks>
		public const float BodyMinConvergenceDistance = 6f;

		/// <summary>
		/// The largest yaw the replicate may put between the aim and the body's heading: the whole circle.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The offset is measured from the heading of the AIM, and the aim's heading is not always near the body's. Over the
		/// shoulder looking down at nearby ground the eye is half a metre beside a ray that converges steeply, so the aim's
		/// heading swings toward the camera's side: 23 degrees off the body at 60 degrees down, 45 at 77, 79 at 89. Looking
		/// almost straight down while running, the eye leads the drawn body by more than the aim's few centimetres of ground
		/// run, and its heading points BACKWARDS: 180 degrees.
		/// </para>
		/// <para>
		/// A bound of 45 degrees clamped those cases, and the body then faced (and walked) up to 135 degrees off the view.
		/// Bending the aim to fit instead moved the shot off the crosshair by 15 to 20 degrees. Neither is needed: the body's
		/// heading is the owner's to choose (it steers the character with its own input anyway), so nothing is gained by a
		/// modified client sending any value it likes. Two bytes on the wire for an offset over 16 degrees, sent only when it
		/// changes.
		/// </para>
		/// </remarks>
		public const float MaxHeadingOffsetDegrees = 180f;

		/// <summary>Wire units per degree of <see cref="CharacterReplicateData.HeadingOffset"/>: quarters.</summary>
		/// <remarks>
		/// Coarse on purpose. An eighth of a degree is the most the body's heading can be off by, which nobody can see and
		/// which moves a walking character a couple of millimetres a metre; and at this size the value holds still over
		/// most ticks of a sweeping aim, so it is rarely sent, and it fits one byte up to sixteen degrees.
		/// </remarks>
		private const float HeadingOffsetUnitsPerDegree = 4f;

		/// <summary>Nearest distance the one-byte convergence code can state, in metres.</summary>
		private const float DistanceCodeNearest = 0.5f;

		/// <summary>
		/// Ratio between neighbouring convergence codes: 254 steps from <see cref="DistanceCodeNearest"/> to
		/// <see cref="FarDistance"/>, each 2.5% longer than the last.
		/// </summary>
		private static readonly float DistanceCodeLogStep = Mathf.Log(FarDistance / DistanceCodeNearest) / 254f;

		/// <summary>
		/// The one-byte code for a distance along the aim (<see cref="CharacterReplicateData.ConvergenceDistance"/>): 1 to
		/// 255 on a logarithmic scale, never 0, which means "not given".
		/// </summary>
		/// <remarks>
		/// Logarithmic because what the distance is used for is an ANGLE (the turn from the hand's line onto the eye's),
		/// and the angle's error goes with the distance's relative error: 1.25% at worst here, which puts a projectile
		/// from a hand 0.85 m off the eye's line within about a centimetre of the point aimed at, at any range.
		/// </remarks>
		public static byte EncodeDistance(float metres)
		{
			if (float.IsNaN(metres) || metres <= DistanceCodeNearest)
			{
				return 1;
			}
			int code = 1 + Mathf.RoundToInt(Mathf.Log(metres / DistanceCodeNearest) / DistanceCodeLogStep);
			return (byte)Mathf.Clamp(code, 1, 255);
		}

		/// <summary>The distance a convergence code stands for, in metres; 0 for the code 0 ("not given").</summary>
		public static float DecodeDistance(byte code)
		{
			return code == 0 ? 0f : DistanceCodeNearest * Mathf.Exp((code - 1) * DistanceCodeLogStep);
		}

		/// <summary>
		/// Distance along the camera's ray at which the trace starts: level with the eye, or the camera itself when the
		/// eye is behind it (first person, where the two are the same point).
		/// </summary>
		public static float RayStart(Vector3 cameraPosition, Vector3 cameraForward, Vector3 eye)
		{
			return Mathf.Max(0f, Vector3.Dot(eye - cameraPosition, cameraForward));
		}

		/// <summary>
		/// The point the shot should pass through.
		/// </summary>
		/// <param name="cameraPosition">The camera.</param>
		/// <param name="cameraForward">The camera's forward, unit length.</param>
		/// <param name="eye">The aim origin.</param>
		/// <param name="hitDistance">How far beyond <see cref="RayStart"/> the trace hit something; infinity for nothing.</param>
		public static Vector3 Apex(Vector3 cameraPosition, Vector3 cameraForward, Vector3 eye, float hitDistance)
		{
			float start = RayStart(cameraPosition, cameraForward, eye);
			float reach = float.IsNaN(hitDistance) || hitDistance > FarDistance ? FarDistance : hitDistance;
			return cameraPosition + cameraForward * (start + Mathf.Max(reach, MinConvergenceDistance));
		}

		/// <summary>The unit direction from the eye to the apex, or <paramref name="fallback"/> if they coincide.</summary>
		public static Vector3 Direction(Vector3 eye, Vector3 apex, Vector3 fallback)
		{
			Vector3 toApex = apex - eye;
			float length = toApex.magnitude;
			return length > 1e-4f ? toApex / length : fallback;
		}

		/// <summary>
		/// The point the body turns to and walks toward: the same thing under the crosshair, held at least
		/// <see cref="BodyMinConvergenceDistance"/> beyond the character's level along the camera's ray.
		/// </summary>
		/// <param name="cameraPosition">The camera.</param>
		/// <param name="cameraForward">The camera's forward, unit length.</param>
		/// <param name="eye">The aim origin, which <paramref name="hitDistance"/> is measured from.</param>
		/// <param name="hitDistance">How far beyond the eye's <see cref="RayStart"/> the trace hit; infinity for nothing.</param>
		/// <param name="drawnPosition">Where the character is drawn (the camera's follow point).</param>
		public static Vector3 BodyPoint(Vector3 cameraPosition, Vector3 cameraForward, Vector3 eye, float hitDistance,
			Vector3 drawnPosition)
		{
			float reach = float.IsNaN(hitDistance) || hitDistance > FarDistance ? FarDistance : hitDistance;
			float hitAlong = RayStart(cameraPosition, cameraForward, eye) + reach;
			float nearest = RayStart(cameraPosition, cameraForward, drawnPosition) + BodyFloorAlongRay(cameraForward);
			return cameraPosition + cameraForward * Mathf.Max(hitAlong, nearest);
		}

		/// <summary>
		/// How far along the camera's ray the body's point must be for it to be <see cref="BodyMinConvergenceDistance"/>
		/// away ON THE GROUND PLANE, which is the plane the body turns in.
		/// </summary>
		/// <remarks>
		/// The floor was a distance along the ray. Looking down, that distance covers less and less ground (by the cosine
		/// of the pitch) while the camera's sideways offset stays what it is, so with the crosshair on the ground nearby
		/// the body turned 10 degrees off the view at 60 down, 19 at 75 and 45 at 85, and walked that way. Measured on the
		/// ground the turn is the same small angle at any pitch.
		/// </remarks>
		public static float BodyFloorAlongRay(Vector3 cameraForward)
		{
			float planar = Vector3.ProjectOnPlane(cameraForward, Vector3.up).magnitude;
			return planar * FarDistance > BodyMinConvergenceDistance ? BodyMinConvergenceDistance / planar : FarDistance;
		}

		/// <summary>
		/// The heading the body turns to: from where the character is DRAWN to its <see cref="BodyPoint"/>, flat.
		/// </summary>
		/// <remarks>
		/// <para>
		/// From the drawn character, not the eye, because the view is built around the drawn one: the camera follows the
		/// smoothed body, which trails the simulated motor (and so the eye) by the owner's interpolation. Measured from the
		/// eye, a strafing character would be off the crosshair's line by that lag, turn toward it, and so steer its own
		/// strafe into a slow circle. Measured from the drawn body, the answer depends only on the camera's layout:
		/// </para>
		/// <list type="bullet">
		/// <item>third and first person: the drawn character is on the crosshair's line, so this is the camera's heading
		/// exactly, as before;</item>
		/// <item>over the shoulder: the character is the shoulder's offset left of that line, so this turns it in toward
		/// the crosshair, by about five degrees at the nearest and almost nothing at range.</item>
		/// </list>
		/// </remarks>
		public static Vector3 BodyHeading(Vector3 drawnPosition, Vector3 bodyPoint, Vector3 cameraForward)
		{
			Vector3 heading = Vector3.ProjectOnPlane(bodyPoint - drawnPosition, Vector3.up);
			if (heading.sqrMagnitude > 1e-8f)
			{
				return heading.normalized;
			}
			Vector3 fallback = Vector3.ProjectOnPlane(cameraForward, Vector3.up);
			return fallback.sqrMagnitude > 1e-8f ? fallback.normalized : cameraForward;
		}

		/// <summary>
		/// Traces the crosshair into the world and returns the aim from the eye to what it is on, and the heading the body
		/// turns to (<see cref="BodyHeading"/>).
		/// </summary>
		/// <param name="physicsScene">The physics scene the character lives in.</param>
		/// <param name="cameraPosition">The camera.</param>
		/// <param name="cameraForward">The camera's forward.</param>
		/// <param name="eye">The aim origin (<see cref="CharacterAimOrigin"/>).</param>
		/// <param name="drawnPosition">Where the character is drawn: the camera's follow point.</param>
		/// <param name="layerMask">What the crosshair can rest on: the layers the cast-time target trace sees.</param>
		/// <param name="ignored">The character's own colliders.</param>
		/// <param name="buffer">Hit buffer, grown as needed.</param>
		/// <param name="bodyHeading">The flat direction the body faces and walks in.</param>
		/// <param name="apexDistance">How far from the eye the point aimed at is, along the returned direction.</param>
		/// <param name="launchPoint">The point on the crosshair's line level with the eye: where the launch line starts.</param>
		/// <returns>The unit aim direction from the eye.</returns>
		public static Vector3 Resolve(PhysicsScene physicsScene, Vector3 cameraPosition, Vector3 cameraForward, Vector3 eye,
			Vector3 drawnPosition, int layerMask, IList<Collider> ignored, ref RaycastHit[] buffer, out Vector3 bodyHeading,
			out float apexDistance, out Vector3 launchPoint)
		{
			float length = cameraForward.magnitude;
			if (length < 1e-6f)
			{
				bodyHeading = cameraForward;
				apexDistance = 0f;
				launchPoint = eye;
				return cameraForward;
			}
			cameraForward /= length;

			float start = RayStart(cameraPosition, cameraForward, eye);
			Vector3 origin = cameraPosition + cameraForward * start;
			launchPoint = origin;

			/* A query that comes back full may have dropped the nearest hit, which is the only one read: grow and retry. */
			int count;
			while (true)
			{
				// The same trigger rule as the cast's own trace (TargetController.UpdateTarget): the crosshair must rest on
				// exactly what that trace will stop at, or the aim converges on one thing and the cast targets another.
				count = physicsScene.Raycast(origin, cameraForward, buffer, FarDistance, layerMask, QueryTriggerInteraction.UseGlobal);
				if (!TargetOrdering.TryGrowQueryBuffer(ref buffer, count))
				{
					break;
				}
			}

			float nearest = float.PositiveInfinity;
			for (int i = 0; i < count; i++)
			{
				RaycastHit hit = buffer[i];
				if (hit.distance < nearest && (ignored == null || !ignored.Contains(hit.collider)))
				{
					nearest = hit.distance;
				}
			}

			bodyHeading = BodyHeading(drawnPosition, BodyPoint(cameraPosition, cameraForward, eye, nearest, drawnPosition), cameraForward);
			Vector3 apex = Apex(cameraPosition, cameraForward, eye, nearest);
			apexDistance = (apex - eye).magnitude;
			return Direction(eye, apex, cameraForward);
		}

		/// <summary>
		/// The launch line as an input carries it: how far along the aim the point under the crosshair is, and where,
		/// beside the eye, the line to it starts (<see cref="CharacterReplicateData.ConvergenceDistance"/>).
		/// </summary>
		public readonly struct AimLaunch
		{
			/// <summary>The convergence distance code; 0 for "not given".</summary>
			public readonly byte Distance;
			/// <summary>The line's start, centimetres to the aim's right of the eye.</summary>
			public readonly sbyte Right;
			/// <summary>The line's start, centimetres up from the eye, across the aim.</summary>
			public readonly sbyte Up;

			public AimLaunch(byte distance, sbyte right, sbyte up)
			{
				Distance = distance;
				Right = right;
				Up = up;
			}

			/// <summary>True when the owner stated a launch line; false for an NPC, and for a tick the owner did not expect to spawn on.</summary>
			public bool IsGiven => Distance != 0;
		}

		/// <summary>
		/// The farthest from the eye a launch line may start, metres.
		/// </summary>
		/// <remarks>
		/// The line starts where the third-person camera's ray passes the eye: the follow point's offset from the eye plus
		/// however far the drawn body trails the simulated one. Over the shoulder the shoulder's own displacement is taken
		/// out first (KCCPlayer.ResolveOwnerAim), so the projectile leaves the character, not the view's side. A metre
		/// covers every camera this game has, and is the whole of what a
		/// modified client can gain by lying: its projectile starts within a metre of where its eye is, and the path
		/// from the eye to the line is still checked for cover (AbilityObject.ResolveSpawnPose).
		/// </remarks>
		public const float MaxOriginOffset = 1f;

		/// <summary>Wire units per metre of an origin offset: centimetres.</summary>
		private const float OriginOffsetUnitsPerMetre = 100f;

		/// <summary>
		/// The nearest the point aimed at may be, ahead of the launch line's start, for the line to be taken through it.
		/// Nearer, the line could point sideways or back, and the aim itself is used.
		/// </summary>
		private const float MinLaunchConvergence = 1f;

		/// <summary>The two directions across an aim: to its right (level), and up across it. The same on every peer for the same aim.</summary>
		public static void AimFrame(Vector3 aimDirection, out Vector3 right, out Vector3 up)
		{
			right = Vector3.Cross(Vector3.up, aimDirection);
			// Straight up or down has no "right"; any fixed one will do, as long as every peer picks the same.
			right = right.sqrMagnitude > 1e-8f ? right.normalized : Vector3.right;
			up = Vector3.Cross(aimDirection, right).normalized;
		}

		/// <summary>
		/// The launch line the owner states for a camera whose crosshair line passes the eye at
		/// <paramref name="launchPoint"/>, with the point aimed at <paramref name="apexDistance"/> along the aim.
		/// </summary>
		public static AimLaunch EncodeLaunch(Vector3 eye, Vector3 launchPoint, Vector3 aimDirection, float apexDistance)
		{
			if (apexDistance <= 0f)
			{
				return default;
			}
			AimFrame(aimDirection, out Vector3 right, out Vector3 up);
			Vector3 offset = Vector3.ClampMagnitude(launchPoint - eye, MaxOriginOffset);
			return new AimLaunch(
				EncodeDistance(apexDistance),
				(sbyte)Mathf.Clamp(Mathf.RoundToInt(Vector3.Dot(offset, right) * OriginOffsetUnitsPerMetre), -127, 127),
				(sbyte)Mathf.Clamp(Mathf.RoundToInt(Vector3.Dot(offset, up) * OriginOffsetUnitsPerMetre), -127, 127));
		}

		/// <summary>
		/// The line a projectile is simulated on: where it starts and which way it runs.
		/// </summary>
		/// <remarks>
		/// <para>
		/// With a launch line given, from the point beside the eye it names to the point its distance along the aim: the
		/// camera's ray through the crosshair, rebuilt. Without one, from the eye along the aim.
		/// </para>
		/// <para>
		/// A pure function of the replicated input and the eye every peer derives, so the owner and the server simulate
		/// the same projectile to the last bit, and no trace is involved. The offset is held to
		/// <see cref="MaxOriginOffset"/> whatever was sent.
		/// </para>
		/// </remarks>
		public static void LaunchLine(Vector3 aimOrigin, Vector3 aimDirection, AimLaunch launch, out Vector3 origin, out Vector3 direction)
		{
			Vector3 aim = aimDirection.sqrMagnitude > 1e-8f ? aimDirection.normalized : Vector3.forward;
			origin = aimOrigin;
			direction = aim;
			if (!launch.IsGiven)
			{
				return;
			}

			AimFrame(aim, out Vector3 right, out Vector3 up);
			Vector3 offset = right * (launch.Right / OriginOffsetUnitsPerMetre) + up * (launch.Up / OriginOffsetUnitsPerMetre);
			Vector3 start = aimOrigin + Vector3.ClampMagnitude(offset, MaxOriginOffset);
			Vector3 toAimedAt = aimOrigin + aim * DecodeDistance(launch.Distance) - start;
			origin = start;
			if (Vector3.Dot(toAimedAt, aim) >= MinLaunchConvergence)
			{
				direction = toAimedAt.normalized;
			}
		}

		/// <summary>
		/// The yaw, in degrees about world up, that turns the aim's heading onto the body's. Zero when either looks
		/// straight up or down, where neither has a heading.
		/// </summary>
		public static float HeadingOffsetDegrees(Vector3 aimDirection, Vector3 bodyHeading)
		{
			Vector3 aim = Vector3.ProjectOnPlane(aimDirection, Vector3.up);
			Vector3 body = Vector3.ProjectOnPlane(bodyHeading, Vector3.up);
			if (aim.sqrMagnitude < 1e-8f || body.sqrMagnitude < 1e-8f)
			{
				return 0f;
			}
			return Vector3.SignedAngle(aim, body, Vector3.up);
		}

		/// <summary>The wire value of a heading offset: quarter degrees, held to the bound.</summary>
		public static short EncodeHeadingOffset(float degrees)
		{
			if (float.IsNaN(degrees))
			{
				return 0;
			}
			return (short)Mathf.RoundToInt(Mathf.Clamp(degrees, -MaxHeadingOffsetDegrees, MaxHeadingOffsetDegrees) * HeadingOffsetUnitsPerDegree);
		}

		/// <summary>
		/// The heading offset a wire value stands for, held to the bound: the server reads whatever a client sent.
		/// </summary>
		public static float DecodeHeadingOffset(short encoded)
		{
			return Mathf.Clamp(encoded / HeadingOffsetUnitsPerDegree, -MaxHeadingOffsetDegrees, MaxHeadingOffsetDegrees);
		}

		/// <summary>
		/// The rotation movement and facing are taken from: the aim, turned about world up by the heading offset onto the
		/// body's heading (<see cref="BodyHeading"/>). Exactly the aim when the offset is zero.
		/// </summary>
		/// <remarks>
		/// Turning about world up moves a direction's heading by exactly the angle and leaves its pitch alone, so the
		/// movement basis (the heading) is the body's, and swimming's dive (the pitch) stays the aim's.
		/// </remarks>
		public static Quaternion MovementRotation(Quaternion aimRotation, short headingOffset)
		{
			if (headingOffset == 0)
			{
				return aimRotation;
			}
			return Quaternion.AngleAxis(DecodeHeadingOffset(headingOffset), Vector3.up) * aimRotation;
		}
	}
}
