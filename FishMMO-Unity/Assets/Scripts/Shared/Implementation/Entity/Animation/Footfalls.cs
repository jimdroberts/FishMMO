using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>What touched the ground (or the water, or a rung).</summary>
	public enum FootfallKind
	{
		/// <summary>A foot set down in a stride.</summary>
		Step = 0,
		/// <summary>Both feet coming down after a jump or a fall.</summary>
		Land = 1,
		/// <summary>An arm's stroke breaking the surface while swimming.</summary>
		Stroke = 2,
		/// <summary>The body going into water from the air.</summary>
		Plunge = 3,
		/// <summary>A hand or a foot on a ladder's rung or a rope.</summary>
		Climb = 4,
	}

	/// <summary>One footfall: where, on what, and how hard.</summary>
	public struct Footfall
	{
		public FootfallKind Kind;
		/// <summary>Where it touched, on the ground (or the water's surface).</summary>
		public Vector3 Point;
		/// <summary>The ground's normal there; up when nothing was hit.</summary>
		public Vector3 Normal;
		/// <summary>What was stood on; null when the ray found nothing (the water, the air, a culled body).</summary>
		public Collider Ground;
		/// <summary>Metres a second the body was going: along the ground for a step, down for a landing or a plunge.</summary>
		public float Speed;
		/// <summary>How tall the body is, metres: a bigger body treads heavier.</summary>
		public float Height;
		/// <summary>0 for the left foot, 1 for the right.</summary>
		public int Foot;
		public bool Crouching;
		/// <summary>The body whose footfall it is (its model's root).</summary>
		public Transform Body;
	}

	/// <summary>
	/// Where the bodies' footfalls are told: every <see cref="CharacterBody"/> raises one each time a foot comes down, on
	/// every client, for the character this client owns and for those it only watches alike. The client's sound listens.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>From the feet themselves.</b> A body on screen raises a step when one of its foot bones, swinging, comes back down
	/// onto the ground under it (<see cref="FootfallTracker"/>): the sound lands with the foot the player sees land, for
	/// any clip and any gait. A body off screen has no bones to watch (its animator is culled), so it raises a step every
	/// stride of the distance it goes — still heard behind the camera, a stride apart.
	/// </para>
	/// <para>
	/// <b>Never from the movement.</b> The owner's controller replays its ticks when it reconciles; a sound raised there
	/// would be heard twice. A body is posed once a frame from what it is seen to do, so each footfall is raised once.
	/// </para>
	/// </remarks>
	public static class CharacterFootfalls
	{
		/// <summary>Raised on the main thread, once for each footfall of every body.</summary>
		public static event Action<Footfall> Fell;

		/// <summary>Whether anything is listening (a dedicated server, a batch run: nothing is, and no ray is cast).</summary>
		public static bool Heard => Fell != null;

		/// <summary>Where the footfalls are heard from: set by the listener each frame.</summary>
		public static Vector3 Ear;

		/// <summary>How far from <see cref="Ear"/> any footfall is heard, metres; a body further off raises none.</summary>
		public static float Reach = float.MaxValue;

		/// <summary>Whether a footfall at <paramref name="point"/> could be heard.</summary>
		public static bool InReach(Vector3 point) => Reach >= float.MaxValue || (point - Ear).sqrMagnitude <= Reach * Reach;

		public static void Raise(in Footfall footfall)
		{
			Fell?.Invoke(footfall);
		}
	}

	/// <summary>
	/// Finds one body's footfalls, frame by frame. Owned by its <see cref="CharacterBody"/>; pure state and arithmetic, so
	/// its rules can be tested without a model.
	/// </summary>
	public sealed class FootfallTracker
	{
		/// <summary>A foot this far above the ground (metres, sole to ground) is in the air.</summary>
		public const float LiftMetres = 0.06f;

		/// <summary>A lifted foot back within this of the ground has come down.</summary>
		public const float PlantMetres = 0.025f;

		/// <summary>Below this ground speed (m/s) a foot moving is a shuffle, not a step.</summary>
		public const float MinStepSpeed = 0.3f;

		/// <summary>The least time between two footfalls of the same foot, seconds: a foot jittering at the plant line is one step.</summary>
		public const float MinStepSeconds = 0.18f;

		/// <summary>A fall slower than this (m/s, downward) lands as a step, not a landing.</summary>
		public const float MinLandSpeed = 2.5f;

		/// <summary>Metres swum at the surface for each stroke.</summary>
		public const float StrokeMetres = 1.1f;

		private readonly bool[] lifted = new bool[2];
		private readonly float[] lastStep = { float.NegativeInfinity, float.NegativeInfinity };
		private float travelled;
		private int nextFoot;
		private float fallSpeed;
		private bool wasAirborne;
		private bool wasSwimming;
		private float swum;
		private float climbed;

		/// <summary>
		/// The length of one step (half a stride), metres, at a ground speed, for a body 1.8 m tall: about 0.75 m at a
		/// walk (1.4 m/s, 1.9 steps a second), 1.4 m at a run (4 m/s), 1.9 m at a sprint (6.5 m/s). A taller body steps
		/// further in proportion.
		/// </summary>
		public static float StepMetres(float speed, float height)
		{
			float scale = height > 0.1f ? height / 1.8f : 1f;
			return (0.45f + 0.22f * Mathf.Clamp(speed, 0f, 9f)) * scale;
		}

		/// <summary>Forgets every foot's state: a teleport, a respawn, a new model.</summary>
		public void Reset()
		{
			lifted[0] = lifted[1] = false;
			lastStep[0] = lastStep[1] = float.NegativeInfinity;
			travelled = 0f;
			fallSpeed = 0f;
			wasAirborne = false;
			wasSwimming = false;
			swum = 0f;
			climbed = 0f;
		}

		/// <summary>
		/// One foot seen this frame, <paramref name="gap"/> metres above the ground under it: whether it has just come
		/// down (lifted, now within <see cref="PlantMetres"/>, moving, and not within <see cref="MinStepSeconds"/> of its
		/// last). A foot that was never seen lifted does not step, so a body that stands still is silent.
		/// </summary>
		public bool FootDown(int foot, float gap, float speed, float now)
		{
			if (gap > LiftMetres)
			{
				lifted[foot] = true;
				return false;
			}
			if (!lifted[foot] || gap > PlantMetres || speed < MinStepSpeed || now - lastStep[foot] < MinStepSeconds)
			{
				return false;
			}
			lifted[foot] = false;
			lastStep[foot] = now;
			// The distance a stride is reckoned by starts again at every seen step, so a body that walks off screen
			// carries on in step rather than doubling one.
			travelled = 0f;
			nextFoot = 1 - foot;
			return true;
		}

		/// <summary>
		/// A body whose feet cannot be seen, gone <paramref name="moved"/> metres along the ground: which foot steps now,
		/// or -1 for none yet.
		/// </summary>
		public int Stride(float moved, float speed, float height)
		{
			if (speed < MinStepSpeed)
			{
				// Standing: the next stride starts from here.
				travelled = 0f;
				return -1;
			}
			travelled += moved;
			if (travelled < StepMetres(speed, height))
			{
				return -1;
			}
			travelled = 0f;
			int foot = nextFoot;
			nextFoot = 1 - nextFoot;
			// Neither foot was seen to lift, so the next seen step must see one lift first.
			lifted[0] = lifted[1] = false;
			return foot;
		}

		/// <summary>
		/// The air, frame by frame: how fast the body comes down while it is off the ground, and on the frame it is back
		/// whether that was a landing (at least <see cref="MinLandSpeed"/>; its speed out).
		/// </summary>
		public bool Landed(bool airborne, bool grounded, float verticalSpeed, out float speed)
		{
			speed = 0f;
			if (airborne)
			{
				fallSpeed = Mathf.Max(fallSpeed, -verticalSpeed);
				wasAirborne = true;
				return false;
			}
			bool landed = wasAirborne && grounded && fallSpeed >= MinLandSpeed;
			speed = fallSpeed;
			wasAirborne = false;
			fallSpeed = 0f;
			if (landed)
			{
				// Both feet are down.
				lifted[0] = lifted[1] = false;
				travelled = 0f;
			}
			return landed;
		}

		/// <summary>
		/// The water, frame by frame: on the frame the body goes in, whether it went in falling (a plunge); while it swims
		/// at the surface, whether a stroke breaks the water now. The fall speed is the one <see cref="Landed"/> kept.
		/// </summary>
		public FootfallKind? Swim(bool swimming, float atSurface, float moved, float verticalSpeed, out float speed)
		{
			speed = 0f;
			FootfallKind? result = null;
			if (swimming && !wasSwimming)
			{
				speed = Mathf.Max(fallSpeed, -verticalSpeed);
				if (speed >= MinLandSpeed * 0.6f)
				{
					result = FootfallKind.Plunge;
				}
				swum = 0f;
			}
			else if (swimming && atSurface > 0.5f)
			{
				swum += moved;
				if (swum >= StrokeMetres)
				{
					swum = 0f;
					speed = moved;
					result = FootfallKind.Stroke;
				}
			}
			if (swimming)
			{
				fallSpeed = 0f;
				wasAirborne = false;
				travelled = 0f;
			}
			wasSwimming = swimming;
			return result;
		}

		/// <summary>Climbing <paramref name="moved"/> metres up or down a ladder: whether a hand or foot takes the next rung.</summary>
		public bool Rung(bool climbing, float moved, float rungMetres)
		{
			if (!climbing)
			{
				climbed = 0f;
				return false;
			}
			climbed += Mathf.Abs(moved);
			if (climbed < Mathf.Max(0.1f, rungMetres))
			{
				return false;
			}
			climbed = 0f;
			nextFoot = 1 - nextFoot;
			return true;
		}

		/// <summary>The foot <see cref="Rung"/> last used.</summary>
		public int NextFoot => nextFoot;
	}
}
