using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>
	/// Presents the waterfalls the water plugin publishes (<see cref="Waterfalls"/>) to everything round a fall
	/// that is not the water itself: the wet, mossy rock its spray keeps soaked (the _FishFallWet* shader
	/// globals FishSurface.hlsl reads) and, through <see cref="WaterfallAudioPresenter"/>, its roar.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why here, and not in the water plugin.</b> The wet rock is drawn by the terrain, rock, tree and grass
	/// shaders, which all take their weather from FishSurface.hlsl; the fall's spray belongs with the rain's
	/// wetness there so that both darken a surface alike and the wetter of the two speaks. The client does not
	/// reference the water plugin, so the falls come through the shared registry, as the sea does through
	/// <see cref="SurfaceWater"/>. Owned and ticked by <see cref="WeatherPresentation"/>, but ahead of its
	/// profile test: a fall is wet and loud whether or not the weather has anything to show.
	/// </para>
	/// <para>
	/// <b>Rebuilt rarely.</b> The zones change only when the falls do (<see cref="Waterfalls.Version"/>) or the
	/// camera has moved far enough that a different sixteen are the nearest, so a frame normally costs one
	/// version compare and one distance check.
	/// </para>
	/// </remarks>
	public sealed class WaterfallPresenter : IDisposable
	{
		/// <summary>How many wet zones the shaders hold; the length of the _FishFallWetA/B arrays in FishSurface.hlsl.</summary>
		public const int MaxWetZones = 16;

		/// <summary>How far the camera moves before the nearest zones are chosen again, metres.</summary>
		public const float RebuildDistance = 64f;

		/// <summary>
		/// How far past a zone's edge a fall is still sent to the shaders, metres. Wet rock reads from a long way
		/// off, but every pixel inside the box round the chosen zones loops over all of them, so a fall over the
		/// horizon is not worth the cost it puts on the ground between.
		/// </summary>
		public const float WetRange = 1500f;

		/// <summary>_FishFallWetCount: how many zones are in use.</summary>
		public static readonly int WetCountId = Shader.PropertyToID("_FishFallWetCount");
		/// <summary>_FishFallWetA[16]: xyz the lip's middle, w the zone's radius there.</summary>
		public static readonly int WetAId = Shader.PropertyToID("_FishFallWetA");
		/// <summary>_FishFallWetB[16]: xyz where the water lands, w the zone's radius there.</summary>
		public static readonly int WetBId = Shader.PropertyToID("_FishFallWetB");
		/// <summary>_FishFallWetMin: the low corner of a box round every zone in use.</summary>
		public static readonly int WetMinId = Shader.PropertyToID("_FishFallWetMin");
		/// <summary>_FishFallWetMax: the high corner of that box.</summary>
		public static readonly int WetMaxId = Shader.PropertyToID("_FishFallWetMax");

		private readonly Vector4[] zoneA = new Vector4[MaxWetZones];
		private readonly Vector4[] zoneB = new Vector4[MaxWetZones];
		private readonly List<KeyValuePair<float, int>> ranked = new List<KeyValuePair<float, int>>();
		private readonly WaterfallAudioPresenter audio;
		private int builtVersion;
		private Vector3 builtAt;
		private bool built;

		/// <param name="parent">What the audio sources are parented under: the weather presentation's object.</param>
		public WaterfallPresenter(Transform parent)
		{
			audio = new WaterfallAudioPresenter(parent);
		}

		/// <summary>How many wet zones the shaders were last given.</summary>
		public int WetZoneCount { get; private set; }

		/// <summary>The roar of the falls.</summary>
		public WaterfallAudioPresenter Audio => audio;

		/// <summary>
		/// The wet zone a fall's spray keeps soaked, as the shaders take it: a capsule from the lip (radius in
		/// <paramref name="lip"/>.w) to where the water lands (radius in <paramref name="landing"/>.w).
		/// </summary>
		/// <remarks>
		/// The landing end is <see cref="Waterfalls.WetRadius"/>, the reach of the spray the impact throws. The lip
		/// end is half the curtain's width, so the rock beside the sheet is inside it, and a quarter of that reach
		/// besides: the spray up there is the curtain's own, thin and close, until the water breaks up lower down.
		/// </remarks>
		public static void ZoneOf(in Waterfalls.Fall fall, out Vector4 lip, out Vector4 landing)
		{
			float wet = Waterfalls.WetRadius(fall);
			float lipRadius = 0.5f * Mathf.Max(0f, fall.Width) + 0.25f * wet;
			lip = new Vector4(fall.Lip.x, fall.Lip.y, fall.Lip.z, lipRadius);
			landing = new Vector4(fall.Landing.x, fall.Landing.y, fall.Landing.z, wet);
		}

		/// <summary>Distance from <paramref name="point"/> to the nearest point of the segment a–b.</summary>
		private static float DistanceToSegment(Vector3 point, Vector3 a, Vector3 b)
		{
			Vector3 ab = b - a;
			float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
			return Vector3.Distance(point, a + ab * t);
		}

		/// <summary>Brings the wet zones and the roar up to date for this frame.</summary>
		/// <param name="camera">The camera presented for; null holds the zones and silences nothing.</param>
		/// <param name="deltaTime">Seconds since the last update (0 when flushed), for the audio's fades.</param>
		public void Update(Camera camera, float deltaTime)
		{
			// Read the falls before their version: the getter is where a destroyed owner's falls are dropped,
			// which itself moves the version on.
			IReadOnlyList<Waterfalls.Fall> falls = Waterfalls.All;
			int version = Waterfalls.Version;
			if (camera == null)
			{
				return;
			}
			Vector3 eye = camera.transform.position;
			if (!built || version != builtVersion || (eye - builtAt).sqrMagnitude > RebuildDistance * RebuildDistance)
			{
				Rebuild(falls, eye);
				builtVersion = version;
				builtAt = eye;
				built = true;
			}
			audio.Update(falls, version, eye, deltaTime);
		}

		/// <summary>Chooses the zones nearest <paramref name="eye"/> and sends them to the shaders.</summary>
		private void Rebuild(IReadOnlyList<Waterfalls.Fall> falls, Vector3 eye)
		{
			ranked.Clear();
			for (int i = 0; i < falls.Count; i++)
			{
				Waterfalls.Fall fall = falls[i];
				ZoneOf(fall, out Vector4 a, out Vector4 b);
				float gap = DistanceToSegment(eye, fall.Lip, fall.Landing) - Mathf.Max(a.w, b.w);
				if (gap <= WetRange)
				{
					ranked.Add(new KeyValuePair<float, int>(gap, i));
				}
			}
			ranked.Sort((x, y) => x.Key.CompareTo(y.Key));

			int count = Mathf.Min(ranked.Count, MaxWetZones);
			Vector3 low = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
			Vector3 high = new Vector3(float.MinValue, float.MinValue, float.MinValue);
			for (int i = 0; i < MaxWetZones; i++)
			{
				if (i < count)
				{
					ZoneOf(falls[ranked[i].Value], out Vector4 a, out Vector4 b);
					zoneA[i] = a;
					zoneB[i] = b;
					// The box round the capsule: the boxes round its two end spheres together.
					Vector3 lip = a, landing = b;
					low = Vector3.Min(low, Vector3.Min(lip - Vector3.one * a.w, landing - Vector3.one * b.w));
					high = Vector3.Max(high, Vector3.Max(lip + Vector3.one * a.w, landing + Vector3.one * b.w));
				}
				else
				{
					zoneA[i] = Vector4.zero;
					zoneB[i] = Vector4.zero;
				}
			}
			if (count == 0)
			{
				// An empty box no point is inside, should a shader read it without the count.
				low = Vector3.one;
				high = -Vector3.one;
			}
			WetZoneCount = count;
			// Always the full sixteen: Unity fixes a global array's length the first time it is set, and a
			// shorter first array would cap every later one.
			Shader.SetGlobalVectorArray(WetAId, zoneA);
			Shader.SetGlobalVectorArray(WetBId, zoneB);
			Shader.SetGlobalVector(WetMinId, low);
			Shader.SetGlobalVector(WetMaxId, high);
			Shader.SetGlobalFloat(WetCountId, count);
		}

		/// <summary>Dries every surface and stops the roar.</summary>
		public void Dispose()
		{
			Shader.SetGlobalFloat(WetCountId, 0f);
			WetZoneCount = 0;
			built = false;
			audio.Dispose();
		}
	}
}
