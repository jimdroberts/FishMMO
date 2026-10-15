using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared
{
	/// <summary>
	/// Turns a mill's sails or water wheel (2026-10-14: "Windmills, watermills, lumbermills"): spins this object about its
	/// own z axis, which the cut lays along the windshaft or the axle. Client-only, under a <see cref="ClientOnlyObject"/>:
	/// nothing collides with it and nothing about it is sent.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>One mill for every player (2026-10-15: "It all needs to be deterministic so the simulation is shared across all
	/// clients and they all see the same thing at the same time").</b> Every angle here is a pure function of the shared
	/// world clock (<see cref="WorldMotion.Seconds"/>) and the scene's weather, which every machine computes alike: no
	/// sum of frame times, no state carried from frame to frame, so two players side by side see the same sail at the
	/// same angle and a rejoin puts it back where everyone has it.
	/// </para>
	/// <para>
	/// <b>A water wheel</b> turns at <see cref="Rpm"/> with a slow surge either way of it, as the race pushes unevenly: the
	/// surge integrated, not stepped.
	/// </para>
	/// <para>
	/// <b>Sails</b> (<see cref="WindDriven"/>, 2026-10-15: "they don't properly turn in varying wind") have gone round as
	/// many times as the scene's wind has turned them (<see cref="AirDrift.SailTurns"/>): still below a cut-in breeze,
	/// faster with the wind up to the miller's working pace (<see cref="Rpm"/>), braked in a gale; with a surge on the
	/// weather's gustiness. The <see cref="Head"/> (a tower mill's cap) faces the wind at the mill as it was a minute
	/// ago, eased over a minute from the minute before: the miller luffs it round after the wind, and every player sees
	/// it come round together.
	/// </para>
	/// <para>Transforms are written only while one of its renderers is visible; nothing is lost by skipping, as nothing is carried.</para>
	/// </remarks>
	[DisallowMultipleComponent]
	public sealed class MillRotor : MonoBehaviour
	{
		/// <summary>Turns a minute; 0 stands still. For sails, the most the miller lets them turn.</summary>
		public float Rpm = 10f;
		/// <summary>A per-mill offset, degrees, so neighbouring mills are not in step.</summary>
		public float Phase;
		/// <summary>How far the rate surges either way of its pace, as a share of it.</summary>
		public float Surge = 0.15f;
		/// <summary>Sails: the pace follows the scene's wind instead of <see cref="Rpm"/>'s steady turn.</summary>
		public bool WindDriven;
		/// <summary>What turns to face the wind with the sails (a tower mill's cap, its yaw frame); null when nothing does.</summary>
		public Transform Head;

		/// <summary>The surge's angular frequency, radians a second (a period of about 48 s).</summary>
		private const float SurgeRate = 0.13f;
		/// <summary>World seconds between the wind headings the cap is luffed to.</summary>
		private const double LuffSeconds = 60.0;

		private Renderer[] renderers;
		/// <summary>The headings at the last two luff moments asked about, and which they were, for the timeline revision they were read under.</summary>
		private long headingKnot = long.MinValue;
		private float headingFrom, headingTo;
		private uint headingRevision;

		private void Awake()
		{
			// The head holds the rotor, so its renderers are the cap's and the sails' both.
			renderers = (Head != null ? Head : transform).GetComponentsInChildren<Renderer>(true);
		}

		/// <summary>The turn at a time, degrees: 6·rpm·(t + s/ω·sin(ωt + φ)), the integral of a rate surging by s.</summary>
		public static float AngleAt(double seconds, float rpm, float surge, float phase)
		{
			double angle = 6.0 * rpm * (seconds + surge / SurgeRate * System.Math.Sin(SurgeRate * seconds + phase * Mathf.Deg2Rad)) + phase;
			return (float)(angle % 360.0);
		}

		/// <summary>The yaw, degrees about world up, that turns a head's +z the way the wind blows, so its sails at −z face into it.</summary>
		public static float YawInto(Vector2 wind) => Mathf.Atan2(wind.x, wind.y) * Mathf.Rad2Deg;

		private void Update()
		{
			if (!Seen())
			{
				return;
			}
			double seconds = WorldMotion.Seconds;
			if (!WindDriven)
			{
				if (Rpm != 0f)
				{
					transform.localRotation = Quaternion.AngleAxis(AngleAt(seconds, Rpm, Surge, Phase), Vector3.forward);
				}
				return;
			}

			Scene scene = gameObject.scene;
			WeatherQuery.TryGetTimeline(scene, out WeatherTimeline timeline);
			WorldSceneSettings.TryGetForScene(scene, out WorldSceneSettings settings);
			AirDrift.Air air = WeatherField.SceneAir(scene, seconds);

			// The turns the wind has given the sails by now, and a gust's surge on the pace it is at.
			double turns = AirDrift.SailTurns(air, Rpm, seconds);
			float pace = AirDrift.SailRpm(AirDrift.SpeedAt(air, seconds) / AirDrift.SteeringGain, Rpm);
			double surge = 6.0 * pace * Surge / SurgeRate * System.Math.Sin(SurgeRate * seconds + Phase * Mathf.Deg2Rad);
			double angle = turns * 360.0 + surge + Phase;
			transform.localRotation = Quaternion.AngleAxis((float)(angle - System.Math.Floor(angle / 360.0) * 360.0), Vector3.forward);

			if (Head != null)
			{
				Head.rotation = Quaternion.Euler(0f, CapYaw(scene, timeline, settings, seconds), 0f);
			}
		}

		/// <summary>
		/// The cap's yaw at a moment: eased (smoothstep) from the wind's heading two luff moments back to the last one's,
		/// so it trails the wind by a minute or two and turns smoothly. The headings are read at whole luff moments of the
		/// world clock, so every machine reads the same two.
		/// </summary>
		private float CapYaw(Scene scene, WeatherTimeline timeline, WorldSceneSettings settings, double seconds)
		{
			long knot = (long)System.Math.Floor(seconds / LuffSeconds);
			uint revision = timeline != null ? timeline.Revision : 0u;
			if (knot != headingKnot || revision != headingRevision)
			{
				headingFrom = knot - 1 == headingKnot && revision == headingRevision ? headingTo : HeadingAt(scene, timeline, settings, (knot - 1) * LuffSeconds);
				headingTo = HeadingAt(scene, timeline, settings, knot * LuffSeconds);
				headingKnot = knot;
				headingRevision = revision;
			}
			float t = Mathf.Clamp01((float)((seconds - knot * LuffSeconds) / LuffSeconds));
			t = t * t * (3f - 2f * t);
			return headingFrom + Mathf.DeltaAngle(headingFrom, headingTo) * t;
		}

		/// <summary>The yaw into the wind at the mill at a moment of world time; the prevailing wind's in a calm, where no heading holds.</summary>
		private float HeadingAt(Scene scene, WeatherTimeline timeline, WorldSceneSettings settings, double worldSeconds)
		{
			Vector2 wind = timeline != null ? WeatherField.SampleAtSeconds(timeline, settings, scene, transform.position, worldSeconds).Air.Wind : Vector2.zero;
			if (wind.sqrMagnitude < 0.25f)
			{
				float latitude = timeline != null ? timeline.LatitudeDegrees : 0f;
				wind = WeatherDriver.PrevailingWind(latitude, WeatherField.BeltsAt(timeline, settings, worldSeconds));
			}
			return YawInto(wind);
		}

		private bool Seen()
		{
			if (renderers == null)
			{
				return false;
			}
			for (int i = 0; i < renderers.Length; i++)
			{
				if (renderers[i] != null && renderers[i].isVisible)
				{
					return true;
				}
			}
			return false;
		}
	}
}
