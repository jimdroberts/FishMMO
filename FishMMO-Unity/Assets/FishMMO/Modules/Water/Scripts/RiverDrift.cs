using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Weather;
using FishMMO.Shared.Celestial;

namespace FishMMO.Water
{
	/// <summary>
	/// Something floating on a lake or river (a log, a leaf, debris): carried by the current, riding the surface, turning
	/// slowly as it goes, and leaving a wake on a river (<see cref="InlandWaterRenderer"/>). Client-side presentation:
	/// nothing about it is simulated on the server.
	/// </summary>
	/// <remarks>
	/// The current is the scene's (<see cref="SurfaceWater.CurrentAt"/>): the blended flow of every river and the jets
	/// into lakes and the sea. A body out of the water (stranded on a bank, or past where any water is) stays where it is.
	/// </remarks>
	public sealed class RiverDrift : MonoBehaviour
	{
		[Tooltip("How much of the current it takes up: 1 rides it, less drags behind (a heavy log), more is blown (a leaf).")]
		[Range(0f, 1.5f)] public float Carry = 0.9f;

		[Tooltip("How high above the water its pivot rides, metres (negative: floating low).")]
		public float Freeboard = 0f;

		[Tooltip("How big it is across, metres: the wake it leaves and how far it bobs.")]
		public float Radius = 0.4f;

		[Tooltip("Degrees per second it turns at the most, swung round by the current.")]
		public float TurnRate = 25f;

		/// <summary>Every drifting body now enabled, for the wakes.</summary>
		public static readonly List<RiverDrift> Active = new List<RiverDrift>();

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics() => Active.Clear();

		/// <summary>Seconds of one bob: 1.7 radians a second.</summary>
		private const double BobPeriod = 2.0 * System.Math.PI / 1.7;

		private float seed;

		private void OnEnable()
		{
			Active.Add(this);
			/* From where it was placed, which every player has the same, not its instance id, which is
			 * this process's: two players see the same log bob the same way. */
			Vector3 home = transform.position;
			seed = Mathf.Repeat(Mathf.Floor(home.x * 4f) * 0.7548777f + Mathf.Floor(home.z * 4f) * 0.5698403f, 1f) * 2f * Mathf.PI;
		}

		private void OnDisable() => Active.Remove(this);

		private void Update()
		{
			Vector3 p = transform.position;
			if (!SurfaceWater.TryGetInlandSurfaceAt(p.x, p.z, out float level) || p.y < level - 2f * Radius - 1f)
			{
				return;
			}
			// Integrated on this client's frames: where it has drifted to is its own history, not a function of
			// the shared clock, and nothing on the server knows of it to correct it (presentation only). At the
			// world's pace, so it stops with the river it rides when the world is held.
			float dt = WorldMotion.Scale(Time.deltaTime);
			Vector2 current = SurfaceWater.CurrentAt(p.x, p.z) * Carry;
			p.x += current.x * dt;
			p.z += current.y * dt;
			// Riding the surface, with a little bob of its own.
			// On the shared world-motion clock, wrapped at one bob so the float keeps its precision and the wrap is seamless.
			float bob = Mathf.Sin((float)(WorldMotion.Repeat(WorldMotion.Seconds, BobPeriod) / BobPeriod) * 2f * Mathf.PI + seed) * 0.03f * Radius;
			p.y = Mathf.Lerp(p.y, level + Freeboard + bob, 1f - Mathf.Exp(-6f * dt));
			transform.position = p;
			if (current.sqrMagnitude > 1e-4f)
			{
				// Swung round by the water, lengthwise to the flow, at its own pace.
				Quaternion want = Quaternion.LookRotation(new Vector3(current.x, 0f, current.y), Vector3.up);
				transform.rotation = Quaternion.RotateTowards(transform.rotation, want, TurnRate * dt);
			}
		}
	}
}
