using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Weather;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// How the scene's cold glows move, biome by biome (2026-10-25, I6i; <see cref="BiomeTemplate.GlowModes"/>): a small table the
	/// GPU reads under each glowing part and crystal (FishGlowModes.hlsl), and the site lights read under each glow's light
	/// (<see cref="TryAt"/>), from the same flora grid (FloraPaletteMap's: each texel its biome's row) so a light and the stone that
	/// casts it read the same entry.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Global arrays, no texture (the indirect pass is near the texture units WebGPU and GL allow): <c>_FishGlowRows</c>
	/// (<see cref="Rows"/> float4, one per flora row: x the way glows' entry, y the crystals', each mode + 32·warm + 64·every; z, w
	/// their episode schedule's index, −1 none), <c>_FishGlowEpisodes</c> (<see cref="Schedules"/> float4: slot A, slot B, slot C, on
	/// share) and <c>_FishGlowRamps</c> (2 float4: the schedules' ramps, seconds). <c>_FishGlowInfo</c> = (on, rows, grid w, grid h):
	/// x is 0 when no biome on the scene (nor its cloud world's body) names a mode, and every glow draws exactly as before.
	/// </para>
	/// <para>Built with the flora map (its rows, its grid), by FloraPaletteMap.Build; client only.</para>
	/// <para>
	/// Its stored places (2026-10-25, I6i-b; <see cref="SceneGlowPlaces"/>, <see cref="Place"/>): the scene's landform lines a TRAIL
	/// or KNEADING glow reads (<c>_FishGlowLines</c>, <c>_FishGlowLineData</c>), its fountain vents an EPISODE glow plays
	/// (<c>_FishGlowVents</c>: x, z, seed's high half + 65536·schedule, seed's low half) and the discs that keep a light and its stone
	/// on one biome's row across a grid edge (<c>_FishGlowAnchors</c>: x, z, radius, row; rebuilt as the scene's lights come and go,
	/// <see cref="Refresh"/>); <c>_FishGlowPlaceInfo</c> = (lines, vents, anchors, 0), all 0 where nothing is stored.
	/// </para>
	/// </remarks>
	public static class GlowModeTable
	{
		/// <summary>The rows the GPU arrays hold: every flora row (its <see cref="FloraPaletteMap.MaxBiomes"/> biomes and the scene's own).</summary>
		public const int Rows = FloraPaletteMap.MaxBiomes + 1;

		/// <summary>The distinct episode schedules a scene's entries and fountain vents may name; more share the last.</summary>
		public const int Schedules = 16;

		/// <summary>How an entry is packed in a float: mode + <see cref="WarmBit"/>·warm + <see cref="EveryUnit"/>·every (exact in a float).</summary>
		public const float WarmBit = 32f, EveryUnit = 64f;

		private static readonly int RowsId = Shader.PropertyToID("_FishGlowRows");
		private static readonly int EpisodesId = Shader.PropertyToID("_FishGlowEpisodes");
		private static readonly int RampsId = Shader.PropertyToID("_FishGlowRamps");
		private static readonly int InfoId = Shader.PropertyToID("_FishGlowInfo");
		private static readonly int LinesId = Shader.PropertyToID("_FishGlowLines");
		private static readonly int LineDataId = Shader.PropertyToID("_FishGlowLineData");
		private static readonly int VentsId = Shader.PropertyToID("_FishGlowVents");
		private static readonly int AnchorsId = Shader.PropertyToID("_FishGlowAnchors");
		private static readonly int PlaceInfoId = Shader.PropertyToID("_FishGlowPlaceInfo");

		private static readonly Vector4[] rows = new Vector4[Rows];
		private static readonly Vector4[] episodes = new Vector4[Schedules];
		private static readonly Vector4[] ramps = new Vector4[Schedules / 4];
		private static readonly Vector4[] lineEnds = new Vector4[SceneGlowPlaces.MaxLines];
		private static readonly Vector4[] lineData = new Vector4[SceneGlowPlaces.MaxLines];
		private static readonly Vector4[] ventData = new Vector4[SceneGlowPlaces.MaxVents];
		private static readonly Vector4[] anchorData = new Vector4[SceneGlowPlaces.MaxAnchors];
		private static readonly List<string> scheduleKeys = new List<string>();
		private static readonly List<SceneGlowLine> lines = new List<SceneGlowLine>();
		private static readonly List<SceneGlowVent> vents = new List<SceneGlowVent>();
		private static List<SceneGlowAnchor> anchors = new List<SceneGlowAnchor>();
		private static readonly List<SceneEmitter> glows = new List<SceneEmitter>();
		private static int[] rowIDs;
		private static int builds, anchoredBuild = -1, anchoredVersion = -1;
		private static Vector4 placeInfo;
		private static Vector4 info;
		private static BiomeGlowMode[,] entries;
		private static bool[,] named;
		private static byte[] grid;
		private static int gridW, gridH;
		private static Vector4 rect;

		/// <summary>Whether any biome on the scene names a mode (the table is on).</summary>
		public static bool On => info.x > 0.5f;

		/// <summary>An entry as the GPU reads it: mode + 32·warm + 64·every (every the mode's own count when it names none).</summary>
		public static float Pack(in BiomeGlowMode entry) => (float)entry.Mode + (entry.Warm ? WarmBit : 0f) + EveryUnit * SceneGlowModes.EveryOf(entry.Mode, entry.Every);

		/// <summary>
		/// Builds the table from the flora map's rows (each row's template; null for none) and grid (each texel its row, as published),
		/// with the cloud world's body-level entries (<see cref="Shared.Celestial.CloudWorldStructures.GlowModes"/>) for every biome
		/// naming none.
		/// </summary>
		public static void Build(IReadOnlyList<BiomeTemplate> rowBiomes, byte[] indices, int width, int height, Vector4 gridRect, IReadOnlyList<BiomeGlowMode> world)
		{
			int count = Mathf.Min(rowBiomes != null ? rowBiomes.Count : 0, Rows);
			entries = new BiomeGlowMode[Mathf.Max(1, count), 2];
			named = new bool[Mathf.Max(1, count), 2];
			grid = indices;
			gridW = width;
			gridH = height;
			rect = gridRect;
			System.Array.Clear(rows, 0, rows.Length);
			System.Array.Clear(episodes, 0, episodes.Length);
			System.Array.Clear(ramps, 0, ramps.Length);
			scheduleKeys.Clear();
			lines.Clear();
			vents.Clear();
			anchors = new List<SceneGlowAnchor>();
			builds++;
			rowIDs = new int[Mathf.Max(1, count)];
			bool any = false;
			for (int r = 0; r < count; r++)
			{
				BiomeTemplate biome = rowBiomes[r];
				rowIDs[r] = biome != null ? biome.ID : 0;
				Vector4 row = new Vector4(0f, 0f, -1f, -1f);
				for (int source = 0; source < 2; source++)
				{
					if (!BiomeGlowModes.Resolve(biome != null ? biome.GlowModes : null, world, biome != null ? biome.FountainEpisodes : default,
						(SceneGlowSource)source, out BiomeGlowMode entry))
					{
						continue;
					}
					entries[r, source] = entry;
					named[r, source] = true;
					any = true;
					row[source] = Pack(in entry);
					if (!entry.Episodes.IsEmpty)
					{
						row[2 + source] = ScheduleOf(in entry.Episodes);
					}
				}
				rows[r] = row;
			}
			info = any && count > 0 ? new Vector4(1f, count, width, height) : Vector4.zero;
			PackPlaces();
		}

		/// <summary>A schedule's index in <c>_FishGlowEpisodes</c>, added on first use; past <see cref="Schedules"/>, the last.</summary>
		private static int ScheduleOf(in BiomeFountainEpisodes ep)
		{
			string key = ep.ToString();
			int index = scheduleKeys.IndexOf(key);
			if (index < 0 && scheduleKeys.Count < Schedules)
			{
				index = scheduleKeys.Count;
				scheduleKeys.Add(key);
				episodes[index] = new Vector4(ep.SlotA, ep.SlotB, ep.SlotC, ep.OnShare);
				Vector4 ramp = ramps[index / 4];
				ramp[index % 4] = ep.RampSeconds;
				ramps[index / 4] = ramp;
			}
			return index < 0 ? Schedules - 1 : index;
		}

		/// <summary>
		/// The scene's stored places the moved glows read (2026-10-25, I6i-b; after <see cref="Build"/>): its landform lines (the map's
		/// <see cref="SceneBiomeMap.GlowLines"/>, else its vent lines) of the biomes whose glows TRAIL or KNEAD, and its fountain vents
		/// (VolcanicVents' Episode vents) where a biome's glows follow the EPISODE. Nothing where no biome names those modes, or nothing
		/// is stored: those glows read their fixed bearing, smooth field and 500 m cell, as before.
		/// </summary>
		public static void Place(WorldSceneSettings settings)
		{
			lines.Clear();
			vents.Clear();
			if (On && entries != null && rowIDs != null)
			{
				var lineBiomes = new HashSet<int>();
				bool episode = false;
				for (int r = 0; r < entries.GetLength(0); r++)
				{
					for (int source = 0; source < 2; source++)
					{
						if (!named[r, source])
						{
							continue;
						}
						SceneGlowMode mode = entries[r, source].Mode;
						if ((mode == SceneGlowMode.Trail || mode == SceneGlowMode.Kneading) && r < rowIDs.Length && rowIDs[r] != 0)
						{
							lineBiomes.Add(rowIDs[r]);
						}
						episode |= mode == SceneGlowMode.Episode;
					}
				}
				SceneBiomeMap map = settings != null ? settings.BiomeMap : null;
				if (map != null && lineBiomes.Count > 0)
				{
					IReadOnlyList<VentLine> stored = map.GlowLines != null && map.GlowLines.Count > 0 ? map.GlowLines : IceStripes.Of(map);
					lines.AddRange(SceneGlowPlaces.Choose(stored, lineBiomes.Contains));
				}
				if (settings != null && episode)
				{
					vents.AddRange(SceneGlowPlaces.VentsOf(VolcanicVents.Of(settings)));
				}
			}
			PackPlaces();
			PublishPlaces();
		}

		/// <summary>What a glow at a position reads of the stored lines and vents (<see cref="SceneGlowPlace.None"/> when the table is off).</summary>
		public static SceneGlowPlace PlaceAt(Vector3 position) => On ? SceneGlowPlaces.At(lines, vents, position.x, position.z) : SceneGlowPlace.None;

		/// <summary>
		/// Rebuilds the discs that keep a light and its stone on one row across a biome-grid edge when the scene's lights come or go
		/// (SceneSiteEmitters.Version) or the table is rebuilt; cheap otherwise. Once a frame (GlowSky.Publish).
		/// </summary>
		public static void Refresh()
		{
			if (!On || entries == null || grid == null)
			{
				return;
			}
			int version = SceneSiteEmitters.Version;
			if (version == anchoredVersion && builds == anchoredBuild)
			{
				return;
			}
			anchoredVersion = version;
			anchoredBuild = builds;
			// Every glow's light (the sites' and the cloud world's), and every site's disc (x, z, radius).
			SceneSiteEmitters.Near(Vector3.zero, EverywhereMetres, k => k == SceneEmitterKind.Glow || k == SceneEmitterKind.Crystal, int.MaxValue, glows);
			var lights = new List<Vector2>(glows.Count);
			foreach (SceneEmitter e in glows)
			{
				lights.Add(new Vector2(e.Position.x, e.Position.z));
			}
			var sites = new List<Vector3>();
			foreach (SceneSiteEmitters set in SceneSiteEmitters.Active)
			{
				if (set == null || set.Points == null || set.Points.Points == null)
				{
					continue;
				}
				foreach (PointOfInterestRecord point in set.Points.Points)
				{
					if (point != null)
					{
						sites.Add(new Vector3(point.Position.x, point.Position.z, point.Radius));
					}
				}
			}
			anchors = SceneGlowPlaces.Anchors(Grid, sites, lights, (a, b) => rows[a] == rows[b]);
			PackPlaces();
			PublishPlaces();
		}

		/// <summary>A reach covering any scene, metres (every light).</summary>
		private const float EverywhereMetres = 1e7f;

		/// <summary>The grid as the glows read it.</summary>
		private static SceneGlowGrid Grid => new SceneGlowGrid { Cells = grid, Width = gridW, Height = gridH, Rect = rect, Rows = entries != null ? entries.GetLength(0) : 0 };

		/// <summary>Packs the lines, vents and anchors into the GPU's arrays.</summary>
		private static void PackPlaces()
		{
			System.Array.Clear(lineEnds, 0, lineEnds.Length);
			System.Array.Clear(lineData, 0, lineData.Length);
			System.Array.Clear(ventData, 0, ventData.Length);
			System.Array.Clear(anchorData, 0, anchorData.Length);
			int lineCount = Mathf.Min(lines.Count, SceneGlowPlaces.MaxLines);
			for (int i = 0; i < lineCount; i++)
			{
				SceneGlowLine l = lines[i];
				lineEnds[i] = new Vector4(l.A.x, l.A.y, l.B.x, l.B.y);
				lineData[i] = new Vector4(l.Along, l.Span, l.Cos2, l.Sin2);
			}
			int ventCount = Mathf.Min(vents.Count, SceneGlowPlaces.MaxVents);
			for (int i = 0; i < ventCount; i++)
			{
				SceneGlowVent v = vents[i];
				// The seed in two 16-bit halves (a 32-bit seed in one float rounds), the high half carrying the schedule.
				int schedule = ScheduleOf(in v.Episodes);
				ventData[i] = new Vector4(v.Position.x, v.Position.y, (v.Seed >> 16) + 65536f * schedule, v.Seed & 0xFFFFu);
			}
			int anchorCount = Mathf.Min(anchors.Count, SceneGlowPlaces.MaxAnchors);
			for (int i = 0; i < anchorCount; i++)
			{
				SceneGlowAnchor a = anchors[i];
				anchorData[i] = new Vector4(a.Centre.x, a.Centre.y, a.Radius, a.Row);
			}
			placeInfo = new Vector4(lineCount, ventCount, anchorCount, 0f);
		}

		/// <summary>Hands the table to the shaders (off unless built with a named mode and <paramref name="published"/>).</summary>
		public static void Publish(bool published)
		{
			Shader.SetGlobalVectorArray(RowsId, rows);
			PublishPlaces();
			Shader.SetGlobalVector(InfoId, published ? info : Vector4.zero);
		}

		/// <summary>Hands the stored places (and the schedules their vents add) to the shaders.</summary>
		private static void PublishPlaces()
		{
			Shader.SetGlobalVectorArray(EpisodesId, episodes);
			Shader.SetGlobalVectorArray(RampsId, ramps);
			Shader.SetGlobalVectorArray(LinesId, lineEnds);
			Shader.SetGlobalVectorArray(LineDataId, lineData);
			Shader.SetGlobalVectorArray(VentsId, ventData);
			Shader.SetGlobalVectorArray(AnchorsId, anchorData);
			Shader.SetGlobalVector(PlaceInfoId, placeInfo);
		}

		/// <summary>Takes the table down.</summary>
		public static void Release()
		{
			info = Vector4.zero;
			entries = null;
			named = null;
			grid = null;
			rowIDs = null;
			lines.Clear();
			vents.Clear();
			anchors = new List<SceneGlowAnchor>();
			placeInfo = Vector4.zero;
			anchoredBuild = anchoredVersion = -1;
			Shader.SetGlobalVector(InfoId, Vector4.zero);
			Shader.SetGlobalVector(PlaceInfoId, Vector4.zero);
		}

		/// <summary>
		/// The row (biome) a glow at a world position reads, as FishGlowModes.hlsl reads it: the first disc holding it (a site or a light
		/// by a biome edge, <see cref="Refresh"/>), else the flora grid's texel, unjittered.
		/// </summary>
		public static int RowAt(Vector3 position)
		{
			if (grid == null || entries == null || rect.z <= 0f || gridW < 1 || gridH < 1)
			{
				return 0;
			}
			return SceneGlowPlaces.RowAt(anchors, position.x, position.z, Grid.RowAt(position.x, position.z));
		}

		/// <summary>The entry moving a source's glow at a position; false when none does there (it glows as before).</summary>
		public static bool TryAt(Vector3 position, SceneGlowSource source, out BiomeGlowMode entry)
		{
			entry = default;
			if (!On || entries == null)
			{
				return false;
			}
			int row = RowAt(position);
			if (row < 0 || row >= entries.GetLength(0) || !named[row, (int)source])
			{
				return false;
			}
			entry = entries[row, (int)source];
			return true;
		}
	}
}
