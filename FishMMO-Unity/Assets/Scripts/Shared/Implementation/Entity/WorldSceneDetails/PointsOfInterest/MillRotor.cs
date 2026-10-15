using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Turns a mill's sails or water wheel (2026-10-14: "Windmills, watermills, lumbermills"): spins this object about its
	/// own z axis, which the cut lays along the windshaft or the axle, at <see cref="Rpm"/> with a slow surge either way of
	/// it, as wind and water push unevenly. Client-only, under a <see cref="ClientOnlyObject"/>: nothing collides with it
	/// and nothing about it is sent.
	/// </summary>
	/// <remarks>
	/// The angle is a pure function of the clock (the surge integrated, not stepped), so a frame skipped while it was out of
	/// sight costs nothing and it is never out of step with itself. Not turned while none of its renderers is visible.
	/// </remarks>
	[DisallowMultipleComponent]
	public sealed class MillRotor : MonoBehaviour
	{
		/// <summary>Turns a minute; 0 stands still.</summary>
		public float Rpm = 10f;
		/// <summary>A per-mill offset, degrees, so neighbouring mills are not in step.</summary>
		public float Phase;
		/// <summary>How far the rate surges either way of <see cref="Rpm"/>, as a share of it.</summary>
		public float Surge = 0.15f;

		/// <summary>The surge's angular frequency, radians a second (a period of about 48 s).</summary>
		private const float SurgeRate = 0.13f;

		private Renderer[] renderers;

		private void Awake()
		{
			renderers = GetComponentsInChildren<Renderer>(true);
		}

		/// <summary>The turn at a time, degrees: 6·rpm·(t + s/ω·sin(ωt + φ)), the integral of a rate surging by s.</summary>
		public static float AngleAt(double seconds, float rpm, float surge, float phase)
		{
			double angle = 6.0 * rpm * (seconds + surge / SurgeRate * System.Math.Sin(SurgeRate * seconds + phase * Mathf.Deg2Rad)) + phase;
			return (float)(angle % 360.0);
		}

		private void Update()
		{
			if (Rpm == 0f || !Seen())
			{
				return;
			}
			transform.localRotation = Quaternion.AngleAxis(AngleAt(Time.timeAsDouble, Rpm, Surge, Phase), Vector3.forward);
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
