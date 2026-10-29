using System;
using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FishMMO.Client
{
	/// <summary>
	/// FALLING STONE (2026-10-25, phase I2u; decisions 79, 95, 97): the stones falling down the scene's stored chutes (its biome map's
	/// <see cref="SceneBiomeMap.Chutes"/>, written by the cut), the Strombolian bombs of its vents rolling down its sciara, the fresh
	/// scars each fall leaves and the breathing taluses' summer rime, mist and winter steam. Every fall is a pure function of the shared
	/// clock (<see cref="Rockfall"/>: found here on the CPU in double for the near chutes, a few numbers each), its stones' paths hashed
	/// on the GPU (FishRockfall.hlsl): one draw of a pre-built field of stones and two of marks and puffs. Presentation only: nothing on
	/// the wire, no collider, no damage.
	/// </summary>
	/// <remarks>
	/// Play mode, client only, self-starting like the site embers. The weather the falls follow (freeze-thaw, rain) is read at the
	/// scene's origin (at its chutes' mean height) on whole hours of the world clock and kept, as the glows read theirs; a scene whose
	/// map stores no chute, no talus cone and no Strombolian vent draws nothing and costs one check a frame.
	/// </remarks>
	public static class RockfallField
	{
		public const string StonesShaderName = "FishMMO/World/Rockfall Stones";
		public const string MarksShaderName = "FishMMO/World/Rockfall Marks";

		/// <summary>FishRockfall.hlsl's sizes.</summary>
		public const int MaxPoints = 512, MaxChutes = 16, MaxFalls = 64, StonesPerFall = 12, MaxBombs = 256, MaxBursts = 8, MaxCones = 16;
		/// <summary>FishRockfallMarks.shader's sizes.</summary>
		public const int MaxDecals = 160, BreathPerCone = 16;

		/// <summary>How far from the camera a scree chute's falls and a talus cone's breath are drawn, a sciara's and a vent's bombs, m.</summary>
		public const float ScreeReach = 700f, SciaraReach = 3500f, VentReach = 3000f, ConeReach = 600f;
		/// <summary>The share of a scree chute down which its scar is drawn (from here to its toe), and how wide it spreads there (× its half width).</summary>
		public const float ScarFrom = 0.35f, ScarSpread = 2.5f;
		private const float Refind = 20f;

		private static readonly int PointsId = Shader.PropertyToID("_RockPoints");
		private static readonly int ChutesId = Shader.PropertyToID("_RockChutes");
		private static readonly int ChutesBId = Shader.PropertyToID("_RockChutesB");
		private static readonly int FallsId = Shader.PropertyToID("_RockFalls");
		private static readonly int BombsId = Shader.PropertyToID("_RockBombs");
		private static readonly int BombsBId = Shader.PropertyToID("_RockBombsB");
		private static readonly int BombsCId = Shader.PropertyToID("_RockBombsC");
		private static readonly int BurstsId = Shader.PropertyToID("_RockBursts");
		private static readonly int BurstsBId = Shader.PropertyToID("_RockBurstsB");
		private static readonly int InfoId = Shader.PropertyToID("_RockInfo");
		private static readonly int ConesId = Shader.PropertyToID("_RockCones");
		private static readonly int ConesBId = Shader.PropertyToID("_RockConesB");
		private static readonly int ConesCId = Shader.PropertyToID("_RockConesC");
		private static readonly int ConeInfoId = Shader.PropertyToID("_RockConeInfo");
		private static readonly int DecalsId = Shader.PropertyToID("_RockDecals");
		private static readonly int DecalsBId = Shader.PropertyToID("_RockDecalsB");
		private static readonly int DecalsCId = Shader.PropertyToID("_RockDecalsC");
		private static readonly int DecalInfoId = Shader.PropertyToID("_RockDecalInfo");
		private static readonly int WeatherWindId = Shader.PropertyToID("_FishWeatherWind");
		private static readonly int ModeId = Shader.PropertyToID("_Mode");
		private static readonly int CullId = Shader.PropertyToID("_Cull");
		private static readonly int ZTestId = Shader.PropertyToID("_ZTest");

		private static readonly Vector4[] points = new Vector4[MaxPoints];
		private static readonly Vector4[] chutesA = new Vector4[MaxChutes];
		private static readonly Vector4[] chutesB = new Vector4[MaxChutes];
		private static readonly Vector4[] falls = new Vector4[MaxFalls];
		private static readonly Vector4[] bombsA = new Vector4[MaxBombs];
		private static readonly Vector4[] bombsB = new Vector4[MaxBombs];
		private static readonly Vector4[] bombsC = new Vector4[MaxBombs];
		private static readonly Vector4[] burstsA = new Vector4[MaxBursts];
		private static readonly Vector4[] burstsB = new Vector4[MaxBursts];
		private static readonly Vector4[] conesA = new Vector4[MaxCones];
		private static readonly Vector4[] conesB = new Vector4[MaxCones];
		private static readonly Vector4[] conesC = new Vector4[MaxCones];
		private static readonly Vector4[] decalsA = new Vector4[MaxDecals];
		private static readonly Vector4[] decalsB = new Vector4[MaxDecals];
		private static readonly Vector4[] decalsC = new Vector4[MaxDecals];

		/// <summary>One near chute: its index in the map, its index on the GPU, its distance and fade.</summary>
		private struct Near
		{
			public int Index;
			public float Distance;
		}

		/// <summary>One bomb of a burst as its burst threw it (found once a burst: the ground decides where it lands).</summary>
		private struct Bomb
		{
			public Vector3 Velocity;
			public float Delay, Size, Flight;
			public int Chute;
			public float Along, Across, RollMetres;
			public uint RollSeed;
		}

		private sealed class Burst
		{
			public Vector3 Vent;
			public float Start, Strength;
			public readonly List<Bomb> Bombs = new List<Bomb>();
		}

		private static readonly List<Near> nearChutes = new List<Near>();
		private static readonly List<int> nearCones = new List<int>();
		private static readonly List<VentSite> nearVents = new List<VentSite>();
		private static readonly List<Rockfall.Fall> found = new List<Rockfall.Fall>();
		private static readonly Dictionary<long, Rockfall.Knot> knots = new Dictionary<long, Rockfall.Knot>();
		private static readonly Dictionary<int, double> lastFalls = new Dictionary<int, double>();
		private static readonly Dictionary<long, Burst> bursts = new Dictionary<long, Burst>();
		private static readonly List<long> staleBursts = new List<long>();
		private static readonly Dictionary<int, int> gpuChute = new Dictionary<int, int>();
		private static Vector4[] chuteBounds = new Vector4[0];
		private static float chuteAltitude;
		private static SceneBiomeMap knownMap;
		private static uint seenSnaps = uint.MaxValue;
		private static Vector3 foundAt = new Vector3(float.MaxValue, 0f, 0f);
		private static int frame = -1;
		private static bool any;
		private static Bounds drawBounds;
		private static WeatherContext context;

		private static Material stonesMaterial, marksMaterial, puffsMaterial;
		private static Mesh stonesMesh, boxesMesh, puffsMesh;
		private static bool hooked;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			Release();
			hooked = false;
			frame = -1;
			knownMap = null;
			seenSnaps = uint.MaxValue;
			foundAt = new Vector3(float.MaxValue, 0f, 0f);
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Hook()
		{
			if (hooked || !Application.isPlaying)
			{
				return;
			}
			hooked = true;
			RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
			SceneManager.sceneUnloaded += _ => Forget();
			Application.quitting += Shutdown;
		}

		private static void Shutdown()
		{
			RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
			hooked = false;
			Release();
		}

		private static void Forget()
		{
			knownMap = null;
			knots.Clear();
			lastFalls.Clear();
			bursts.Clear();
			nearChutes.Clear();
			nearCones.Clear();
			foundAt = new Vector3(float.MaxValue, 0f, 0f);
		}

		private static void Release()
		{
			DestroyAll(ref stonesMaterial);
			DestroyAll(ref marksMaterial);
			DestroyAll(ref puffsMaterial);
			DestroyAll(ref stonesMesh);
			DestroyAll(ref boxesMesh);
			DestroyAll(ref puffsMesh);
			Forget();
		}

		private static void DestroyAll<T>(ref T thing) where T : Object
		{
			if (thing != null)
			{
				Object.Destroy(thing);
				thing = null;
			}
		}

		/// <summary>This hook's own time in the profiler (render-pipeline callbacks otherwise show only as
		/// RenderPipeline.BeginCameraRendering's self time). In a class of its own so the marker, an engine object, is made
		/// when the hook first runs, not when the type is first touched (tests outside Unity use these types' pure parts).</summary>
		private static class HookProfiling
		{
			public static readonly Unity.Profiling.ProfilerMarker Marker = new Unity.Profiling.ProfilerMarker("RockfallField.BeginCamera");
		}

		private static void OnBeginCameraRendering(ScriptableRenderContext ctx, Camera camera)
		{
			using var hookMarkerScope = HookProfiling.Marker.Auto();
			if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView)
			{
				return;
			}
			if (frame != Time.frameCount)
			{
				frame = Time.frameCount;
				Camera main = Camera.main;
				any = Frame(main != null ? main.transform.position : camera.transform.position);
			}
			if (any)
			{
				Draw(camera);
			}
		}

		// ── The frame ────────────────────────────────────────────

		/// <summary>Works out the frame's falls, bombs, scars and breath and publishes them; false when the scene has none near.</summary>
		private static bool Frame(Vector3 eye)
		{
			context = WeatherClient.LastContext;
			SceneBiomeMap map = context.Settings != null ? context.Settings.BiomeMap : null;
			if (map == null)
			{
				return false;
			}
			bool chutes = map.Chutes != null && map.Chutes.Count > 0 && map.ChutePoints != null;
			bool cones = map.TalusCones != null && map.TalusCones.Count > 0;
			List<VentSite> vents = VolcanicVents.Of(context.Settings);
			bool strombolian = false;
			foreach (VentSite v in vents)
			{
				strombolian |= v.Timing == BiomeVentTiming.Strombolian;
			}
			if (!chutes && !cones && !strombolian)
			{
				return false;
			}
			if (!ReferenceEquals(map, knownMap))
			{
				Forget();
				knownMap = map;
				MeasureChutes(map);
			}
			// The clock set to another moment (or another scene's weather): every fall found again from it.
			if (WeatherClient.Snaps != seenSnaps)
			{
				seenSnaps = WeatherClient.Snaps;
				lastFalls.Clear();
				bursts.Clear();
			}
			if ((eye - foundAt).sqrMagnitude > Refind * Refind)
			{
				foundAt = eye;
				FindNear(map, eye, vents);
			}

			double seconds = WorldMotion.Seconds;
			float wrapped = WorldMotion.Wrapped(WorldMotion.ShaderWrapSeconds);
			PlanetAir planet = context.Sample.Planet;
			float gravity = planet.Gravity > 0f ? planet.Gravity : SurfacePhysics.EarthGravity;
			float air = !planet.HasAir ? 0f : VolcanicPlume.IsThin(planet) ? 0.35f : 1f;
			drawBounds = new Bounds(eye, Vector3.one * 50f);

			int pointCount = UploadChutes(map, eye, seconds, gravity);
			int fallCount = GatherFalls(map, seconds, gravity);
			int bombCount = GatherBombs(map, wrapped, gravity, out int burstCount);
			int coneCount = air > 0f ? GatherCones(map, eye) : ClearCones();
			int decalCount = GatherScars(map, eye, seconds, coneCount);
			for (int i = fallCount; i < MaxFalls; i++)
			{
				falls[i] = Vector4.zero;
			}
			for (int i = bombCount; i < MaxBombs; i++)
			{
				bombsA[i] = bombsB[i] = bombsC[i] = Vector4.zero;
			}
			for (int i = burstCount; i < MaxBursts; i++)
			{
				burstsA[i] = burstsB[i] = Vector4.zero;
			}
			Vector4 wind = Shader.GetGlobalVector(WeatherWindId);
			float speed = wind.z * 30f;
			Shader.SetGlobalVectorArray(PointsId, points);
			Shader.SetGlobalVectorArray(ChutesId, chutesA);
			Shader.SetGlobalVectorArray(ChutesBId, chutesB);
			Shader.SetGlobalVectorArray(FallsId, falls);
			Shader.SetGlobalVectorArray(BombsId, bombsA);
			Shader.SetGlobalVectorArray(BombsBId, bombsB);
			Shader.SetGlobalVectorArray(BombsCId, bombsC);
			Shader.SetGlobalVectorArray(BurstsId, burstsA);
			Shader.SetGlobalVectorArray(BurstsBId, burstsB);
			Shader.SetGlobalVector(InfoId, new Vector4(fallCount, bombCount, air, gravity));
			Shader.SetGlobalVectorArray(ConesId, conesA);
			Shader.SetGlobalVectorArray(ConesBId, conesB);
			Shader.SetGlobalVectorArray(ConesCId, conesC);
			Shader.SetGlobalVector(ConeInfoId, new Vector4(coneCount, wrapped, wind.x * speed * 0.3f, wind.y * speed * 0.3f));
			Shader.SetGlobalVectorArray(DecalsId, decalsA);
			Shader.SetGlobalVectorArray(DecalsBId, decalsB);
			Shader.SetGlobalVectorArray(DecalsCId, decalsC);
			Shader.SetGlobalVector(DecalInfoId, new Vector4(decalCount, 0f, 0f, 0f));
			return pointCount > 0 || bombCount > 0 || coneCount > 0;
		}

		/// <summary>Each chute's box (centre xz, radius) and the chutes' mean height (where the weather they follow is read).</summary>
		private static void MeasureChutes(SceneBiomeMap map)
		{
			int n = map.Chutes != null ? map.Chutes.Count : 0;
			chuteBounds = new Vector4[n];
			double heights = 0.0;
			int counted = 0;
			for (int c = 0; c < n; c++)
			{
				RockChute chute = map.Chutes[c];
				Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = -min;
				for (int p = chute.First; p < chute.First + chute.Count && p < map.ChutePoints.Count; p++)
				{
					Vector4 q = map.ChutePoints[p];
					min = Vector2.Min(min, new Vector2(q.x, q.z));
					max = Vector2.Max(max, new Vector2(q.x, q.z));
					heights += q.y;
					counted++;
				}
				Vector2 mid = 0.5f * (min + max);
				chuteBounds[c] = new Vector4(mid.x, mid.y, 0.5f * (max - min).magnitude + chute.Half, 0f);
			}
			chuteAltitude = counted > 0 ? (float)(heights / counted) : 0f;
			if (counted == 0 && map.TalusCones != null && map.TalusCones.Count > 0)
			{
				foreach (TalusCone cone in map.TalusCones)
				{
					heights += cone.Apex.y;
				}
				chuteAltitude = (float)(heights / map.TalusCones.Count);
			}
		}

		private static void FindNear(SceneBiomeMap map, Vector3 eye, List<VentSite> vents)
		{
			nearChutes.Clear();
			var eye2 = new Vector2(eye.x, eye.z);
			for (int c = 0; c < chuteBounds.Length && c < map.Chutes.Count; c++)
			{
				Vector4 b = chuteBounds[c];
				float d = Mathf.Max(0f, Vector2.Distance(eye2, new Vector2(b.x, b.y)) - b.z);
				float reach = map.Chutes[c].Kind == RockChuteKind.Sciara ? SciaraReach : ScreeReach;
				if (d <= reach + Refind)
				{
					nearChutes.Add(new Near { Index = c, Distance = d });
				}
			}
			// The sciaras first (the bombs roll down them, whatever is nearest), then the nearest scree chutes.
			nearChutes.Sort((a, b) =>
			{
				bool sa = map.Chutes[a.Index].Kind == RockChuteKind.Sciara, sb = map.Chutes[b.Index].Kind == RockChuteKind.Sciara;
				return sa != sb ? (sa ? -1 : 1) : a.Distance.CompareTo(b.Distance);
			});
			nearCones.Clear();
			for (int c = 0; map.TalusCones != null && c < map.TalusCones.Count; c++)
			{
				TalusCone cone = map.TalusCones[c];
				if (Vector2.Distance(eye2, new Vector2(cone.Toe.x, cone.Toe.z)) <= ConeReach + Refind + (cone.Toe - cone.Apex).magnitude)
				{
					nearCones.Add(c);
				}
			}
			nearCones.Sort((a, b) => Vector3.SqrMagnitude(map.TalusCones[a].Toe - eye).CompareTo(Vector3.SqrMagnitude(map.TalusCones[b].Toe - eye)));
			nearVents.Clear();
			foreach (VentSite v in vents)
			{
				if (v.Timing == BiomeVentTiming.Strombolian && Vector2.Distance(eye2, v.Position) <= VentReach + Refind)
				{
					nearVents.Add(v);
				}
			}
		}

		private static float Fade(float distance, float reach) => 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(reach * 0.75f, reach, distance));

		/// <summary>The near chutes' points and numbers onto the GPU (as many as fit), their scars' freshness with them.</summary>
		private static int UploadChutes(SceneBiomeMap map, Vector3 eye, double seconds, float gravity)
		{
			gpuChute.Clear();
			int used = 0, slot = 0;
			var eye2 = new Vector2(eye.x, eye.z);
			foreach (Near near in nearChutes)
			{
				if (slot >= MaxChutes)
				{
					break;
				}
				RockChute chute = map.Chutes[near.Index];
				if (used + chute.Count > MaxPoints || chute.First + chute.Count > map.ChutePoints.Count)
				{
					continue;
				}
				Vector4 b = chuteBounds[near.Index];
				float distance = Mathf.Max(0f, Vector2.Distance(eye2, new Vector2(b.x, b.y)) - b.z);
				float fade = Fade(distance, chute.Kind == RockChuteKind.Sciara ? SciaraReach : ScreeReach);
				for (int p = 0; p < chute.Count; p++)
				{
					points[used + p] = map.ChutePoints[chute.First + p];
				}
				chutesA[slot] = new Vector4(used, chute.Count, chute.Length, chute.Half);
				chutesB[slot] = new Vector4((float)chute.Kind, Freshness(near.Index, chute, seconds), fade, 0f);
				gpuChute[near.Index] = slot;
				drawBounds.Encapsulate(new Bounds(new Vector3(b.x, chuteAltitude, b.y), new Vector3(2f * b.z, 600f, 2f * b.z)));
				used += chute.Count;
				slot++;
			}
			for (int i = slot; i < MaxChutes; i++)
			{
				chutesA[i] = chutesB[i] = Vector4.zero;
			}
			for (int i = used; i < MaxPoints; i++)
			{
				points[i] = Vector4.zero;
			}
			return used;
		}

		/// <summary><see cref="MultiplierAt"/> as a delegate, made once: a method group converts to a new delegate on every use.</summary>
		private static Func<double, float> multiplierAt;

		private static int GatherFalls(SceneBiomeMap map, double seconds, float gravity)
		{
			int count = 0;
			Func<double, float> multiplier = multiplierAt ??= MultiplierAt;
			foreach (Near near in nearChutes)
			{
				if (!gpuChute.TryGetValue(near.Index, out int slot))
				{
					continue;
				}
				RockChute chute = map.Chutes[near.Index];
				found.Clear();
				Rockfall.FallsAt(seconds, chute.Seed, chute.Boost, Rockfall.LifetimeOf(chute.Length, gravity), multiplier, found);
				foreach (Rockfall.Fall f in found)
				{
					if (count >= MaxFalls)
					{
						return count;
					}
					falls[count++] = new Vector4(slot, (float)(seconds - f.Start), f.Seed, f.Stones);
					// A fall newer than the one the scar knew of refreshes it.
					if (!lastFalls.TryGetValue(near.Index, out double last) || f.Start > last)
					{
						lastFalls[near.Index] = f.Start;
					}
				}
			}
			return count;
		}

		/// <summary>How fresh a chute's scar is now: from its last fall, found by a look back the first time the chute is near, then kept up by the falls found each frame.</summary>
		private static float Freshness(int index, in RockChute chute, double seconds)
		{
			// Only a scree chute draws a scar (a sciara is one already).
			if (chute.Kind != RockChuteKind.Scree)
			{
				return 0f;
			}
			if (!lastFalls.TryGetValue(index, out double last))
			{
				last = Rockfall.LastFall(seconds, chute.Seed, chute.Boost, multiplierAt ??= MultiplierAt);
				lastFalls[index] = last;
			}
			return Rockfall.ScarFreshness(seconds - last);
		}

		// ── The weather the falls follow ─────────────────────────

		/// <summary><see cref="KnotAt"/> as a delegate, made once (asked for every fall of every near chute, every frame).</summary>
		private static Func<long, Rockfall.Knot> knotAt;

		private static float MultiplierAt(double seconds) => Rockfall.Multiplier(seconds, knotAt ??= KnotAt);

		/// <summary>The weather at the scene's origin, at its chutes' mean height, on one hour of the world clock (kept once read).</summary>
		private static Rockfall.Knot KnotAt(long knot)
		{
			if (knots.TryGetValue(knot, out Rockfall.Knot k))
			{
				return k;
			}
			k = new Rockfall.Knot { Celsius = 10f, Rain = 0f };
			if (context.Timeline != null && context.Settings != null)
			{
				WeatherSample s = WeatherField.SampleAtSeconds(context.Timeline, context.Settings, context.Scene, new Vector3(0f, chuteAltitude, 0f), knot * Rockfall.KnotSeconds);
				k.Celsius = s.Kelvin - 273.15f;
				k.Rain = Mathf.Clamp01(s.Frame[WeatherChannel.Precipitation]) * Mathf.Clamp01(s.Frame[WeatherChannel.RainWeight]);
			}
			if (knots.Count > 256)
			{
				knots.Clear();
			}
			knots[knot] = k;
			return k;
		}

		// ── The bombs ────────────────────────────────────────────

		private static int GatherBombs(SceneBiomeMap map, float wrapped, float gravity, out int burstCount)
		{
			burstCount = 0;
			int bombs = 0;
			staleBursts.Clear();
			foreach (long key in bursts.Keys)
			{
				staleBursts.Add(key);
			}
			foreach (VentSite vent in nearVents)
			{
				for (int offset = 0; offset >= -1; offset--)
				{
					if (!StromboliBombs.BurstOf(wrapped, vent.TimingSeed, offset, out float start, out float strength, out int k))
					{
						continue;
					}
					float age = wrapped - start;
					// The slot before's burst can be up to two periods old; each bomb goes on its own (rest, then sink) before that.
					if (age < 0f || age > 2f * VentBursts.PeriodOf(vent.TimingSeed))
					{
						continue;
					}
					long key = ((long)vent.TimingSeed << 20) ^ ((long)k << 1) ^ (start < 0f ? 1L : 0L);
					staleBursts.Remove(key);
					if (!bursts.TryGetValue(key, out Burst burst))
					{
						burst = Throw(map, vent, k, start, strength, gravity);
						if (burst == null)
						{
							continue;
						}
						bursts[key] = burst;
					}
					if (burstCount >= MaxBursts)
					{
						continue;
					}
					burstsA[burstCount] = new Vector4(burst.Vent.x, burst.Vent.y, burst.Vent.z, age);
					burstsB[burstCount] = new Vector4(burst.Strength, 0f, 0f, 0f);
					drawBounds.Encapsulate(new Bounds(burst.Vent, new Vector3(500f, 300f, 500f)));
					foreach (Bomb b in burst.Bombs)
					{
						if (bombs >= MaxBombs)
						{
							break;
						}
						int chute = b.Chute >= 0 && gpuChute.TryGetValue(b.Chute, out int s) ? s : -1;
						bombsA[bombs] = new Vector4(b.Velocity.x, b.Velocity.y, b.Velocity.z, b.Delay);
						bombsB[bombs] = new Vector4(burstCount + Mathf.Clamp(b.Size, 0.02f, 0.49f), chute, b.Along, b.Across);
						bombsC[bombs] = new Vector4(b.Flight, b.RollSeed, chute >= 0 ? b.RollMetres : 0f, 0f);
						bombs++;
					}
					burstCount++;
				}
			}
			foreach (long key in staleBursts)
			{
				bursts.Remove(key);
			}
			return bombs;
		}

		/// <summary>A burst's bombs, thrown once: where each lands on the ground, and whether it lands in a sciara's band to roll down it.</summary>
		private static Burst Throw(SceneBiomeMap map, in VentSite vent, int slot, float start, float strength, float gravity)
		{
			float ground = GroundAt(vent.Position.x, vent.Position.y);
			if (float.IsNaN(ground))
			{
				return null;
			}
			var burst = new Burst { Vent = new Vector3(vent.Position.x, ground + 1f, vent.Position.y), Start = start, Strength = strength };
			int count = StromboliBombs.CountOf(vent.TimingSeed, slot, strength);
			Func<float, float, float> groundAt = GroundAt;
			for (int i = 0; i < count; i++)
			{
				Vector3 v = StromboliBombs.Launch(vent.TimingSeed, slot, i, gravity, out float delay, out float size);
				float flight = StromboliBombs.Landing(burst.Vent, v, gravity, groundAt, out Vector3 at);
				var bomb = new Bomb { Velocity = v, Delay = delay, Size = size, Flight = flight, Chute = -1, RollSeed = StromboliBombs.RollSeed(vent.TimingSeed, slot, i) };
				if (OnSciara(map, at, out int chute, out float along, out float across))
				{
					RockChute c = map.Chutes[chute];
					Rockfall.Stone roll = Rockfall.StoneOf(bomb.RollSeed, 0, RockChuteKind.Sciara, c.Length, c.Half, gravity);
					bomb.Chute = chute;
					bomb.Along = along;
					bomb.Across = across;
					bomb.RollMetres = Mathf.Max(0f, roll.Stop - along);
				}
				burst.Bombs.Add(bomb);
			}
			return burst;
		}

		/// <summary>True when a point lies in a stored sciara's band: the chute, how far down it and how far to its right the point is.</summary>
		private static bool OnSciara(SceneBiomeMap map, Vector3 at, out int chute, out float along, out float across)
		{
			chute = -1;
			along = across = 0f;
			float best = float.MaxValue;
			var p = new Vector2(at.x, at.z);
			for (int c = 0; c < map.Chutes.Count; c++)
			{
				RockChute rc = map.Chutes[c];
				if (rc.Kind != RockChuteKind.Sciara)
				{
					continue;
				}
				for (int i = rc.First; i + 1 < rc.First + rc.Count && i + 1 < map.ChutePoints.Count; i++)
				{
					Vector4 a = map.ChutePoints[i], b = map.ChutePoints[i + 1];
					var ab = new Vector2(b.x - a.x, b.z - a.z);
					float len2 = ab.sqrMagnitude;
					float t = len2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - new Vector2(a.x, a.z), ab) / len2) : 0f;
					Vector2 q = new Vector2(a.x, a.z) + ab * t;
					float d = (p - q).magnitude;
					if (d < best && d <= rc.Half)
					{
						best = d;
						chute = c;
						along = Mathf.Lerp(a.w, b.w, t);
						Vector2 dir = len2 > 1e-6f ? ab / Mathf.Sqrt(len2) : Vector2.right;
						// FishRockfall.hlsl's right: (dir.y, −dir.x).
						across = Vector2.Dot(p - q, new Vector2(dir.y, -dir.x));
					}
				}
			}
			return chute >= 0;
		}

		/// <summary>Reused for <see cref="Terrain.GetActiveTerrains"/>: <c>Terrain.activeTerrains</c> builds a new array on
		/// every read, and this runs every frame.</summary>
		private static readonly System.Collections.Generic.List<Terrain> activeTerrainsScratch = new System.Collections.Generic.List<Terrain>();

		/// <summary>The ground at a point of the scene's terrains (NaN off them).</summary>
		private static float GroundAt(float x, float z)
		{
			Scene scene = context.Scene;
			Terrain.GetActiveTerrains(activeTerrainsScratch);
			foreach (Terrain t in activeTerrainsScratch)
			{
				if (t == null || t.terrainData == null || (scene.IsValid() && t.gameObject.scene != scene))
				{
					continue;
				}
				Vector3 o = t.GetPosition();
				Vector3 s = t.terrainData.size;
				if (x >= o.x && x <= o.x + s.x && z >= o.z && z <= o.z + s.z)
				{
					return t.SampleHeight(new Vector3(x, 0f, z)) + o.y;
				}
			}
			return float.NaN;
		}

		// ── The breathing talus and the scars ────────────────────

		private static int ClearCones()
		{
			for (int i = 0; i < MaxCones; i++)
			{
				conesA[i] = conesB[i] = conesC[i] = Vector4.zero;
			}
			return 0;
		}

		/// <summary>The near talus cones' breath (with air only: an airless talus neither rimes nor steams), by the season of the world clock.</summary>
		private static int GatherCones(SceneBiomeMap map, Vector3 eye)
		{
			int count = 0;
			bool season = SceneClimate.Foliage(context.Settings, context.WorldHours, out SceneClimate.FoliageState state, context.Timeline);
			foreach (int c in nearCones)
			{
				if (count >= MaxCones || !season)
				{
					break;
				}
				TalusCone cone = map.TalusCones[c];
				Vector2 breath = TalusBreath.Of(state.Phase01, state.ColdestCelsius - Mathf.Max(0f, cone.Apex.y) * state.CoolingPerMetre, state.CelsiusAt(cone.Apex.y));
				float fade = Fade(Vector3.Distance(eye, cone.Toe), ConeReach);
				if (breath.x <= 0f && breath.y <= 0f || fade <= 0f)
				{
					continue;
				}
				conesA[count] = new Vector4(cone.Apex.x, cone.Apex.y, cone.Apex.z, breath.x);
				conesB[count] = new Vector4(cone.Toe.x, cone.Toe.y, cone.Toe.z, breath.y);
				conesC[count] = new Vector4(cone.Spread, cone.Seed & 0xFFFFFFu, fade, 0f);
				drawBounds.Encapsulate(new Bounds(0.5f * (cone.Apex + cone.Toe), Vector3.one * (2f * (cone.Toe - cone.Apex).magnitude + 40f)));
				count++;
			}
			for (int i = count; i < MaxCones; i++)
			{
				conesA[i] = conesB[i] = conesC[i] = Vector4.zero;
			}
			return count;
		}

		/// <summary>The scree chutes' scars down their runouts (every third point a box) and the cones' summer rime round their toes.</summary>
		private static int GatherScars(SceneBiomeMap map, Vector3 eye, double seconds, int coneCount)
		{
			int count = 0;
			foreach (Near near in nearChutes)
			{
				if (!gpuChute.TryGetValue(near.Index, out int slot))
				{
					continue;
				}
				RockChute chute = map.Chutes[near.Index];
				float strength = chutesB[slot].y * chutesB[slot].z;
				if (chute.Kind != RockChuteKind.Scree || strength <= 0.01f)
				{
					continue;
				}
				int from = chute.First + Mathf.FloorToInt(ScarFrom * (chute.Count - 1));
				int end = chute.First + chute.Count - 1;
				for (int p = from; p < end && count < MaxDecals - MaxCones; p += 3)
				{
					int q = Mathf.Min(p + 3, end);
					Vector4 a = map.ChutePoints[p], b = map.ChutePoints[q];
					float share = chute.Length > 0f ? a.w / chute.Length : 0f;
					float half = chute.Half * Mathf.Lerp(1f, ScarSpread, Mathf.InverseLerp(ScarFrom, 1f, share));
					decalsA[count] = new Vector4(a.x, a.y, a.z, half);
					decalsB[count] = new Vector4(b.x, b.y, b.z, 0f);
					decalsC[count] = new Vector4(strength, (chute.Seed ^ (uint)p) & 0xFFFFFFu, share, chute.Length > 0f ? b.w / chute.Length : 1f);
					count++;
				}
			}
			for (int c = 0; c < coneCount && count < MaxDecals; c++)
			{
				float ice = conesA[c].w * conesC[c].z;
				if (ice <= 0.01f)
				{
					continue;
				}
				var toe = new Vector3(conesB[c].x, conesB[c].y, conesB[c].z);
				var apex = new Vector3(conesA[c].x, conesA[c].y, conesA[c].z);
				float radius = Mathf.Max(4f, 0.35f * Vector3.Distance(toe, apex) + 0.3f * conesC[c].x);
				decalsA[count] = new Vector4(toe.x, toe.y, toe.z, radius);
				decalsB[count] = new Vector4(toe.x, toe.y, toe.z, 1f);
				decalsC[count] = new Vector4(ice, conesC[c].y, 1f, 1f);
				count++;
			}
			for (int i = count; i < MaxDecals; i++)
			{
				decalsA[i] = decalsB[i] = decalsC[i] = Vector4.zero;
			}
			return count;
		}

		// ── The draws ────────────────────────────────────────────

		private static void Draw(Camera camera)
		{
			if (!Resolve())
			{
				return;
			}
			var stones = new RenderParams(stonesMaterial)
			{
				camera = camera,
				shadowCastingMode = ShadowCastingMode.Off,
				receiveShadows = true,
				layer = camera.gameObject.layer,
				worldBounds = drawBounds,
			};
			Graphics.RenderMesh(stones, stonesMesh, 0, Matrix4x4.identity);
			var marks = new RenderParams(marksMaterial)
			{
				camera = camera,
				shadowCastingMode = ShadowCastingMode.Off,
				receiveShadows = true,
				layer = camera.gameObject.layer,
				worldBounds = drawBounds,
			};
			Graphics.RenderMesh(marks, boxesMesh, 0, Matrix4x4.identity);
			var puffs = new RenderParams(puffsMaterial)
			{
				camera = camera,
				shadowCastingMode = ShadowCastingMode.Off,
				receiveShadows = true,
				layer = camera.gameObject.layer,
				worldBounds = drawBounds,
			};
			Graphics.RenderMesh(puffs, puffsMesh, 0, Matrix4x4.identity);
		}

		private static bool Resolve()
		{
			WeatherRenderProfile profile = WeatherRenderProfile.Active;
			Shader stonesShader = profile != null && profile.RockfallStonesShader != null ? profile.RockfallStonesShader : Shader.Find(StonesShaderName);
			Shader marksShader = profile != null && profile.RockfallMarksShader != null ? profile.RockfallMarksShader : Shader.Find(MarksShaderName);
			if (stonesShader == null || marksShader == null)
			{
				return false;
			}
			if (stonesMaterial == null || stonesMaterial.shader != stonesShader)
			{
				DestroyAll(ref stonesMaterial);
				stonesMaterial = new Material(stonesShader) { name = "Rockfall Stones", hideFlags = HideFlags.HideAndDontSave };
			}
			if (marksMaterial == null || marksMaterial.shader != marksShader)
			{
				DestroyAll(ref marksMaterial);
				DestroyAll(ref puffsMaterial);
				/* The marks: boxes drawn by their far faces (BuildBoxes turns every face's front inward, so culling the back keeps the
				 * far side, which a camera inside the box still sees) over whatever depth they hold; the puffs: quads, depth-tested. */
				marksMaterial = new Material(marksShader) { name = "Rockfall Marks", hideFlags = HideFlags.HideAndDontSave };
				marksMaterial.SetFloat(ModeId, 0f);
				marksMaterial.SetFloat(CullId, (float)CullMode.Back);
				marksMaterial.SetFloat(ZTestId, (float)CompareFunction.Always);
				puffsMaterial = new Material(marksShader) { name = "Rockfall Puffs", hideFlags = HideFlags.HideAndDontSave };
				puffsMaterial.SetFloat(ModeId, 1f);
				puffsMaterial.SetFloat(CullId, (float)CullMode.Off);
				puffsMaterial.SetFloat(ZTestId, (float)CompareFunction.LessEqual);
			}
			if (stonesMesh == null)
			{
				stonesMesh = BuildStones(MaxFalls * StonesPerFall + MaxBombs);
			}
			if (boxesMesh == null)
			{
				boxesMesh = BuildBoxes(MaxDecals);
			}
			if (puffsMesh == null)
			{
				puffsMesh = BuildQuads(MaxFalls * StonesPerFall + MaxCones * BreathPerCone);
			}
			return true;
		}

		// ── The meshes ───────────────────────────────────────────

		/// <summary>
		/// The field of stones: per slot one low-poly stone (an icosahedron, flat-faced, its corners pushed in and out by one of four
		/// hashed shapes), one unit across about the origin; TEXCOORD0.x its slot. Built once.
		/// </summary>
		public static Mesh BuildStones(int slots)
		{
			float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
			var corners = new[]
			{
				new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
				new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
				new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
			};
			int[] faces =
			{
				0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
				3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
			};
			const int Shapes = 4;
			var shapes = new Vector3[Shapes][];
			var random = new System.Random(48611);
			for (int s = 0; s < Shapes; s++)
			{
				shapes[s] = new Vector3[corners.Length];
				// Blocky: flattened on one axis, each corner in or out a little.
				float flat = 0.6f + 0.35f * (float)random.NextDouble();
				for (int c = 0; c < corners.Length; c++)
				{
					Vector3 p = corners[c].normalized * (0.42f + 0.16f * (float)random.NextDouble());
					p.y *= flat;
					shapes[s][c] = p;
				}
			}
			int perStone = faces.Length;
			var positions = new Vector3[slots * perStone];
			var normals = new Vector3[slots * perStone];
			var uvs = new Vector2[slots * perStone];
			var indices = new int[slots * perStone];
			for (int i = 0; i < slots; i++)
			{
				Vector3[] shape = shapes[i % Shapes];
				int v0 = i * perStone;
				for (int f = 0; f < faces.Length; f += 3)
				{
					Vector3 a = shape[faces[f]], b = shape[faces[f + 1]], c = shape[faces[f + 2]];
					Vector3 n = Vector3.Cross(b - a, c - a).normalized;
					int v = v0 + f;
					positions[v] = a;
					positions[v + 1] = b;
					positions[v + 2] = c;
					normals[v] = normals[v + 1] = normals[v + 2] = n;
					uvs[v] = uvs[v + 1] = uvs[v + 2] = new Vector2(i, 0f);
					indices[v] = v;
					indices[v + 1] = v + 1;
					indices[v + 2] = v + 2;
				}
			}
			var mesh = new Mesh { name = "Rockfall Stones", hideFlags = HideFlags.HideAndDontSave };
			mesh.indexFormat = positions.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
			mesh.vertices = positions;
			mesh.normals = normals;
			mesh.uv = uvs;
			mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
			mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f);
			return mesh;
		}

		/// <summary>The marks' boxes: per slot a cube from −1 to 1, every face's front turned inward (TEXCOORD0.x its slot). Built once.</summary>
		public static Mesh BuildBoxes(int slots)
		{
			var positions = new Vector3[slots * 8];
			var uvs = new Vector2[slots * 8];
			var indices = new int[slots * 36];
			int[] cube = { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 2, 3, 7, 2, 7, 6, 1, 2, 6, 1, 6, 5, 3, 0, 4, 3, 4, 7 };
			for (int i = 0; i < slots; i++)
			{
				int v = i * 8;
				for (int c = 0; c < 8; c++)
				{
					positions[v + c] = new Vector3((c & 1) != (c >> 1 & 1) ? 1f : -1f, (c & 4) != 0 ? 1f : -1f, (c & 2) != 0 ? 1f : -1f);
					uvs[v + c] = new Vector2(i, 0f);
				}
				for (int k = 0; k < cube.Length; k++)
				{
					indices[i * 36 + k] = v + cube[k];
				}
			}
			var mesh = new Mesh { name = "Rockfall Marks", hideFlags = HideFlags.HideAndDontSave };
			mesh.indexFormat = IndexFormat.UInt16;
			mesh.vertices = positions;
			mesh.uv = uvs;
			mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
			mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f);
			return mesh;
		}

		/// <summary>The puffs' quads: per slot one quad, corners −1..1 in POSITION.xy (TEXCOORD0.x its slot). Built once.</summary>
		public static Mesh BuildQuads(int slots)
		{
			var positions = new Vector3[slots * 4];
			var uvs = new Vector2[slots * 4];
			var indices = new int[slots * 6];
			for (int i = 0; i < slots; i++)
			{
				int v = i * 4;
				positions[v] = new Vector3(-1f, -1f, 0f);
				positions[v + 1] = new Vector3(1f, -1f, 0f);
				positions[v + 2] = new Vector3(1f, 1f, 0f);
				positions[v + 3] = new Vector3(-1f, 1f, 0f);
				for (int c = 0; c < 4; c++)
				{
					uvs[v + c] = new Vector2(i, 0f);
				}
				int t = i * 6;
				indices[t] = v;
				indices[t + 1] = v + 2;
				indices[t + 2] = v + 1;
				indices[t + 3] = v;
				indices[t + 4] = v + 3;
				indices[t + 5] = v + 2;
			}
			var mesh = new Mesh { name = "Rockfall Puffs", hideFlags = HideFlags.HideAndDontSave };
			mesh.indexFormat = IndexFormat.UInt16;
			mesh.vertices = positions;
			mesh.uv = uvs;
			mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
			mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f);
			return mesh;
		}
	}
}
