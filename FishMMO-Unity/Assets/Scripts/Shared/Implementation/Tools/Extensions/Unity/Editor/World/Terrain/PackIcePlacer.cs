#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Water;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>What one pack ice placement did.</summary>
	public sealed class PackIceReport
	{
		/// <summary>The pack's root, or null when none was laid.</summary>
		public GameObject Root;
		public int Floes;
		public int Chunks;
		/// <summary>The share of the open sea the floes cover, 0 … 1.</summary>
		public float Cover;
		/// <summary>The climate's concentration over the sea, mean and greatest, 0 … 1.</summary>
		public float MeanConcentration, MostConcentration;
		public readonly List<string> Notes = new List<string>();
		public readonly List<string> Wrote = new List<string>();

		public override string ToString() =>
			$"Pack ice: {Floes:N0} floes in {Chunks} chunks covering {Cover:P0} of the open sea (concentration {MeanConcentration:0.00} mean, {MostConcentration:0.00} at most).";
	}

	/// <summary>
	/// Lays a cold sea's pack ice (<see cref="PackIce"/>): floes at the concentration the climate gives each part of the sea,
	/// with leads kept open shore to shore and along every way ships sail, merged into walkable chunks that ride the tide.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The concentration is the climate's, place by place</b> — <see cref="IceOccurrence.SeaIceConcentrationAt"/>, the year's
	/// share of months the sea-level air holds the sea frozen, from the body's climate table: the same figure the globe shades
	/// its sea by, so the white the globe shows over a scene is the white the scene has.
	/// </para>
	/// <para>
	/// <b>Floes, not a sheet.</b> Each cell of the sea gets a floe or none: big enough to touch its neighbours in close pack,
	/// smaller and further apart in loose, with whether it is there decided by a slow noise so the pack lies in fields and
	/// bays rather than a sprinkle. Irregular polygons, a freeboard of <see cref="Freeboard"/> above the still water and a
	/// keel of <see cref="Draught"/> below it.
	/// </para>
	/// <para>
	/// <b>Always crossable.</b> Every lane a ship sails (<see cref="FloatingBoats.Lanes"/>) keeps a lead
	/// <see cref="LaneHalfWidth"/> either side of it, and <see cref="LeadCount"/> straight leads run right across the scene,
	/// as the swell and the wind open them in a real pack: a boat can always get through.
	/// </para>
	/// <para>Deterministic from the scene's seed, like the rest of the cut.</para>
	/// </remarks>
	public static class PackIcePlacer
	{
		public const string RootName = "Pack Ice";

		/// <summary>The floes' height above still water, m: enough that a player standing on one is not wading.</summary>
		public const float Freeboard = 0.3f;

		/// <summary>The floes' depth below still water, m.</summary>
		public const float Draught = 1.5f;

		/// <summary>The spacing of the cells floes are laid in, m.</summary>
		public const float CellMetres = 30f;

		/// <summary>The side of the chunks the floes are merged into, m.</summary>
		public const float ChunkMetres = 64f;

		/// <summary>Open water kept either side of a ship's lane, m.</summary>
		public const float LaneHalfWidth = 30f;

		/// <summary>Leads run right across the scene, and the open water either side of each, m.</summary>
		public const int LeadCount = 3;
		public const float LeadHalfWidth = 14f;

		/// <summary>Open water kept from the shore, m, and from the scene's edge.</summary>
		public const float ShoreGapMetres = 4f;
		public const float EdgeMarginMetres = 40f;

		/// <summary>Below this concentration the sea has only loose pieces (the edge of the ice), not a pack.</summary>
		public const float MinimumConcentration = 0.15f;

		/// <summary>The spacing of the grid the climate's concentration is read on, across the scene.</summary>
		public const int ConcentrationGrid = 17;

		/// <summary>The size of the fields the pack lies in, m: the noise deciding which cells hold a floe.</summary>
		public const float FieldMetres = 260f;

		/// <summary>Removes every pack this placer laid in a scene.</summary>
		public static int Clear(Scene scene)
		{
			int removed = 0;
			if (!scene.IsValid())
			{
				return removed;
			}
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				if (root != null && root.GetComponent<PackIce>() != null)
				{
					UnityEngine.Object.DestroyImmediate(root);
					removed++;
				}
			}
			return removed;
		}

		/// <summary>The pack of a generated scene: its body's climate over its sea, its lanes, its seed.</summary>
		/// <param name="assetPath">Where the chunk meshes are written, one asset; null keeps them in memory (tests).</param>
		public static PackIceReport Place(Scene scene, SceneGenerationRequest request, in IceSite site, WaterShoreField.Snapshot field, uint seed,
			string assetPath)
		{
			var report = new PackIceReport();
			Clear(scene);
			if (request == null || request.Body == null || field == null || field.Resolution < 2)
			{
				report.Notes.Add("Pack ice: no body or no depth field to lay it on.");
				return report;
			}
			SolarSystemProfile system = SolarSystemProfile.Resolve(request.Body);
			PlanetClimateField climate = PlanetClimateField.For(system, request.Body);
			float[] concentration = ConcentrationGridOf(climate, request, site.Area, out float mean, out float most);
			report.MeanConcentration = mean;
			report.MostConcentration = most;
			if (most < MinimumConcentration)
			{
				return report;
			}
			var lanes = new List<FloatingBoats.Lane>();
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				foreach (FloatingBoats boats in root.GetComponentsInChildren<FloatingBoats>(true))
				{
					foreach (FloatingBoats.Lane lane in boats.Lanes)
					{
						if (lane != null && lane.Points != null && lane.Points.Length > 1)
						{
							lanes.Add(lane);
						}
					}
				}
			}
			return Lay(scene, site, field, concentration, lanes, seed, assetPath, report);
		}

		/// <summary>The climate's sea ice concentration over the scene, on a <see cref="ConcentrationGrid"/>-square grid.</summary>
		private static float[] ConcentrationGridOf(in PlanetClimateField climate, SceneGenerationRequest request, Rect area, out float mean, out float most)
		{
			var grid = new float[ConcentrationGrid * ConcentrationGrid];
			mean = 0f;
			most = 0f;
			for (int j = 0; j < ConcentrationGrid; j++)
			{
				for (int i = 0; i < ConcentrationGrid; i++)
				{
					float x = Mathf.Lerp(area.xMin, area.xMax, i / (ConcentrationGrid - 1f));
					float z = Mathf.Lerp(area.yMin, area.yMax, j / (ConcentrationGrid - 1f));
					Vector3 direction = AtlasGeometry.SceneToUnit(request.Footprint, x / 1000.0, z / 1000.0, request.ResolvedRadiusKm).ToVector3().normalized;
					float c = IceOccurrence.SeaIceConcentrationAt(climate, PlanetClimateField.LatitudeOf(direction), direction);
					grid[j * ConcentrationGrid + i] = c;
					mean += c;
					most = Mathf.Max(most, c);
				}
			}
			mean /= grid.Length;
			return grid;
		}

		/// <summary>The testable core: floes on a depth field at a concentration grid, leads kept open, chunks built.</summary>
		public static PackIceReport Lay(Scene scene, in IceSite site, WaterShoreField.Snapshot field, float[] concentration,
			IReadOnlyList<FloatingBoats.Lane> lanes, uint seed, string assetPath, PackIceReport report = null)
		{
			report ??= new PackIceReport();
			Rect area = site.Area;
			List<(Vector2 A, Vector2 B, float Half)> leads = Leads(area, lanes, seed);

			var floes = new List<PackIce.Floe>();
			var polygons = new List<Vector2[]>();
			int cellsX = Mathf.FloorToInt(area.width / CellMetres), cellsZ = Mathf.FloorToInt(area.height / CellMetres);
			int seaCells = 0;
			float seaArea = 0f, iceArea = 0f;
			for (int j = 0; j < cellsZ; j++)
			{
				for (int i = 0; i < cellsX; i++)
				{
					uint h = Hash(seed, i, j);
					var centre = new Vector2(area.xMin + (i + 0.5f + (Unit(h) - 0.5f) * 0.4f) * CellMetres,
						area.yMin + (j + 0.5f + (Unit(h * 747796405u + 1u) - 0.5f) * 0.4f) * CellMetres);
					float c = ConcentrationAt(concentration, area, centre);
					// Open sea the pack may lie on: deep enough at the lowest tide for its keel, clear of the shore and the edge.
					if (!field.TrySample(centre, out float depth, out float edge) || depth - site.LowTideMetres < Draught + 0.3f)
					{
						continue;
					}
					seaCells++;
					seaArea += CellMetres * CellMetres;
					if (c < MinimumConcentration)
					{
						continue;
					}
					// The floe's share of its cell, and the share of cells holding one, multiply to the concentration.
					float share = Mathf.Clamp(0.55f + 0.35f * c, 0.55f, 0.88f);
					float present = Mathf.Clamp01(c / share);
					if (FieldNoise(seed, centre) >= present)
					{
						continue;
					}
					float radius = CellMetres * Mathf.Sqrt(share / Mathf.PI) * Mathf.Lerp(0.85f, 1.05f, Unit(h ^ 0x9E3779B9u));
					if (edge < ShoreGapMetres + radius
						|| centre.x < area.xMin + EdgeMarginMetres + radius || centre.x > area.xMax - EdgeMarginMetres - radius
						|| centre.y < area.yMin + EdgeMarginMetres + radius || centre.y > area.yMax - EdgeMarginMetres - radius
						|| InLead(leads, centre, radius))
					{
						continue;
					}
					Vector2[] polygon = Polygon(h, centre, radius);
					floes.Add(new PackIce.Floe { Centre = centre, Radius = radius });
					polygons.Add(polygon);
					iceArea += PolygonArea(polygon);
				}
			}
			report.Floes = floes.Count;
			report.Cover = seaArea > 0f ? iceArea / seaArea : 0f;
			if (floes.Count == 0)
			{
				return report;
			}

			// The chunks, each about its own middle on the still water line.
			var host = new GameObject(RootName);
			SceneManager.MoveGameObjectToScene(host, scene);
			PackIce pack = host.AddComponent<PackIce>();
			report.Root = host;
			Material material = MaterialOrNote(report);
			var byChunk = new Dictionary<long, List<int>>();
			for (int f = 0; f < floes.Count; f++)
			{
				long key = ChunkKey(floes[f].Centre);
				if (!byChunk.TryGetValue(key, out List<int> list))
				{
					byChunk[key] = list = new List<int>();
				}
				list.Add(f);
			}
			var keys = new List<long>(byChunk.Keys);
			keys.Sort();
			var chunks = new List<Transform>();
			var meshes = new List<Mesh>();
			float xMin = float.MaxValue, zMin = float.MaxValue, xMax = float.MinValue, zMax = float.MinValue;
			foreach (long key in keys)
			{
				int cx = (int)(key >> 32), cz = (int)(key & 0xffffffffL);
				var middle = new Vector2((cx + 0.5f) * ChunkMetres, (cz + 0.5f) * ChunkMetres);
				Mesh mesh = BuildChunk(byChunk[key], polygons, middle);
				mesh.name = $"{scene.name} Pack Ice {cx} {cz}";
				meshes.Add(mesh);
				var chunk = new GameObject($"Chunk {cx} {cz}");
				chunk.transform.SetParent(host.transform, false);
				chunk.transform.position = new Vector3(middle.x, site.SeaLevelY, middle.y);
				chunk.AddComponent<MeshCollider>().sharedMesh = mesh;
				var visual = new GameObject("Visual");
				visual.transform.SetParent(chunk.transform, false);
				visual.AddComponent<ClientOnlyObject>();
				visual.AddComponent<MeshFilter>().sharedMesh = mesh;
				var renderer = visual.AddComponent<MeshRenderer>();
				renderer.sharedMaterial = material;
				renderer.shadowCastingMode = ShadowCastingMode.On;
				chunks.Add(chunk.transform);
				foreach (int f in byChunk[key])
				{
					xMin = Mathf.Min(xMin, floes[f].Centre.x - floes[f].Radius);
					zMin = Mathf.Min(zMin, floes[f].Centre.y - floes[f].Radius);
					xMax = Mathf.Max(xMax, floes[f].Centre.x + floes[f].Radius);
					zMax = Mathf.Max(zMax, floes[f].Centre.y + floes[f].Radius);
				}
			}
			pack.Floes = floes.ToArray();
			pack.Chunks = chunks.ToArray();
			pack.SeaLevelY = site.SeaLevelY;
			pack.Area = Rect.MinMaxRect(xMin, zMin, xMax, zMax);
			report.Chunks = chunks.Count;

			for (int i = 0; assetPath != null && i < meshes.Count; i++)
			{
				if (i == 0)
				{
					AssetDatabase.CreateAsset(meshes[i], assetPath);
				}
				else
				{
					AssetDatabase.AddObjectToAsset(meshes[i], assetPath);
				}
			}
			if (assetPath != null)
			{
				report.Wrote.Add(assetPath);
			}
			report.Notes.Add(report.ToString());
			return report;
		}

		// ── Leads ─────────────────────────────────────────────────────

		private static List<(Vector2 A, Vector2 B, float Half)> Leads(Rect area, IReadOnlyList<FloatingBoats.Lane> lanes, uint seed)
		{
			var leads = new List<(Vector2, Vector2, float)>();
			if (lanes != null)
			{
				foreach (FloatingBoats.Lane lane in lanes)
				{
					for (int p = 1; p < lane.Points.Length; p++)
					{
						Vector3 a = lane.Points[p - 1], b = lane.Points[p];
						leads.Add((new Vector2(a.x, a.z), new Vector2(b.x, b.z), LaneHalfWidth));
					}
				}
			}
			// Right across the scene, edge to edge, through a point and at an angle the seed picks.
			float reach = area.width + area.height;
			for (int k = 0; k < LeadCount; k++)
			{
				uint h = Hash(seed ^ 0x1EADu, k, 7);
				var through = new Vector2(Mathf.Lerp(area.xMin, area.xMax, 0.2f + 0.6f * Unit(h)),
					Mathf.Lerp(area.yMin, area.yMax, 0.2f + 0.6f * Unit(h * 2654435761u)));
				float angle = (k / (float)LeadCount + 0.25f * (Unit(h ^ 0x5bd1e995u) - 0.5f)) * Mathf.PI;
				var way = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * reach;
				leads.Add((through - way, through + way, LeadHalfWidth));
			}
			return leads;
		}

		private static bool InLead(List<(Vector2 A, Vector2 B, float Half)> leads, Vector2 p, float radius)
		{
			foreach ((Vector2 a, Vector2 b, float half) in leads)
			{
				Vector2 ab = b - a;
				float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
				if ((a + ab * t - p).magnitude < half + radius)
				{
					return true;
				}
			}
			return false;
		}

		// ── Floes ─────────────────────────────────────────────────────

		/// <summary>An irregular convex-ish floe: eight corners about its centre, each at its own reach and angle.</summary>
		private static Vector2[] Polygon(uint h, Vector2 centre, float radius)
		{
			const int Corners = 8;
			var corners = new Vector2[Corners];
			float turn = Unit(h ^ 0xABCDu) * Mathf.PI * 2f;
			for (int k = 0; k < Corners; k++)
			{
				uint hk = Hash(h, k, 3);
				float angle = turn + (k + (Unit(hk) - 0.5f) * 0.5f) * Mathf.PI * 2f / Corners;
				float reach = radius * Mathf.Lerp(0.8f, 1.05f, Unit(hk * 2246822519u));
				corners[k] = centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * reach;
			}
			return corners;
		}

		/// <summary>The floes of one chunk as one mesh about its middle: a flat top at the freeboard, sides down to the keel.</summary>
		private static Mesh BuildChunk(List<int> indices, List<Vector2[]> polygons, Vector2 middle)
		{
			var vertices = new List<Vector3>();
			var normals = new List<Vector3>();
			var uvs = new List<Vector2>();
			var triangles = new List<int>();
			foreach (int f in indices)
			{
				Vector2[] poly = polygons[f];
				int n = poly.Length;
				// The top, a fan from its centroid, facing up.
				Vector2 centroid = Vector2.zero;
				foreach (Vector2 p in poly)
				{
					centroid += p;
				}
				centroid /= n;
				int top = vertices.Count;
				vertices.Add(new Vector3(centroid.x - middle.x, Freeboard, centroid.y - middle.y));
				normals.Add(Vector3.up);
				uvs.Add(centroid / 6f);
				for (int k = 0; k < n; k++)
				{
					vertices.Add(new Vector3(poly[k].x - middle.x, Freeboard, poly[k].y - middle.y));
					normals.Add(Vector3.up);
					uvs.Add(poly[k] / 6f);
				}
				for (int k = 0; k < n; k++)
				{
					// Corners run anticlockwise seen from above; a fan wound clockwise from above faces up in Unity.
					triangles.Add(top);
					triangles.Add(top + 1 + (k + 1) % n);
					triangles.Add(top + 1 + k);
				}
				// The sides, one flat quad a corner pair, facing out.
				for (int k = 0; k < n; k++)
				{
					Vector2 a = poly[k], b = poly[(k + 1) % n];
					Vector2 edge = b - a;
					var outward = new Vector3(edge.y, 0f, -edge.x).normalized;
					int s = vertices.Count;
					vertices.Add(new Vector3(a.x - middle.x, Freeboard, a.y - middle.y));
					vertices.Add(new Vector3(b.x - middle.x, Freeboard, b.y - middle.y));
					vertices.Add(new Vector3(b.x - middle.x, -Draught, b.y - middle.y));
					vertices.Add(new Vector3(a.x - middle.x, -Draught, a.y - middle.y));
					for (int q = 0; q < 4; q++)
					{
						normals.Add(outward);
					}
					float length = edge.magnitude / 6f;
					uvs.Add(new Vector2(0f, Freeboard / 6f));
					uvs.Add(new Vector2(length, Freeboard / 6f));
					uvs.Add(new Vector2(length, -Draught / 6f));
					uvs.Add(new Vector2(0f, -Draught / 6f));
					triangles.Add(s);
					triangles.Add(s + 1);
					triangles.Add(s + 2);
					triangles.Add(s);
					triangles.Add(s + 2);
					triangles.Add(s + 3);
				}
			}
			var mesh = new Mesh { indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
			mesh.SetVertices(vertices);
			mesh.SetNormals(normals);
			mesh.SetUVs(0, uvs);
			mesh.SetTriangles(triangles, 0);
			mesh.RecalculateBounds();
			mesh.RecalculateTangents();
			return mesh;
		}

		private static float PolygonArea(Vector2[] poly)
		{
			float a = 0f;
			for (int k = 0; k < poly.Length; k++)
			{
				Vector2 p = poly[k], q = poly[(k + 1) % poly.Length];
				a += p.x * q.y - q.x * p.y;
			}
			return Mathf.Abs(a) * 0.5f;
		}

		private static Material MaterialOrNote(PackIceReport report)
		{
			string name = RockArtNames.IceMaterial(ProceduralArtCatalogue.SeaIceSurface);
			string path = ProceduralArtCatalogue.MaterialPath(name);
			Material material = path != null ? AssetDatabase.LoadAssetAtPath<Material>(path) : null;
			if (material == null)
			{
				report.Notes.Add($"Pack ice: no sea ice material at '{path}'; run Generate Biome Art, then re-cut. The pack renders with the default material until then.");
			}
			return material;
		}

		// ── Arithmetic ────────────────────────────────────────────────

		private static float ConcentrationAt(float[] grid, Rect area, Vector2 p)
		{
			float u = Mathf.Clamp01((p.x - area.xMin) / Mathf.Max(1e-3f, area.width)) * (ConcentrationGrid - 1);
			float v = Mathf.Clamp01((p.y - area.yMin) / Mathf.Max(1e-3f, area.height)) * (ConcentrationGrid - 1);
			int i = Mathf.Min((int)u, ConcentrationGrid - 2), j = Mathf.Min((int)v, ConcentrationGrid - 2);
			float fu = u - i, fv = v - j;
			int n = j * ConcentrationGrid + i;
			return Mathf.Lerp(Mathf.Lerp(grid[n], grid[n + 1], fu), Mathf.Lerp(grid[n + ConcentrationGrid], grid[n + ConcentrationGrid + 1], fu), fv);
		}

		private static long ChunkKey(Vector2 p)
		{
			int cx = Mathf.FloorToInt(p.x / ChunkMetres), cz = Mathf.FloorToInt(p.y / ChunkMetres);
			return ((long)cx << 32) | (uint)cz;
		}

		/// <summary>A slow, smooth noise 0 … 1 over the sea: which fields hold floes.</summary>
		private static float FieldNoise(uint seed, Vector2 p)
		{
			float a = ValueNoise(seed, p / FieldMetres);
			float b = ValueNoise(seed ^ 0x7F4A7C15u, p / (FieldMetres * 0.37f));
			// Two octaves pile toward the middle; stretched back out so a concentration reads as that share of the sea.
			return Mathf.Clamp01((a * 0.7f + b * 0.3f - 0.5f) * 1.8f + 0.5f);
		}

		private static float ValueNoise(uint seed, Vector2 p)
		{
			int x0 = Mathf.FloorToInt(p.x), y0 = Mathf.FloorToInt(p.y);
			float fx = p.x - x0, fy = p.y - y0;
			fx = fx * fx * (3f - 2f * fx);
			fy = fy * fy * (3f - 2f * fy);
			float v00 = Unit(Hash(seed, x0, y0)), v10 = Unit(Hash(seed, x0 + 1, y0));
			float v01 = Unit(Hash(seed, x0, y0 + 1)), v11 = Unit(Hash(seed, x0 + 1, y0 + 1));
			return Mathf.Lerp(Mathf.Lerp(v00, v10, fx), Mathf.Lerp(v01, v11, fx), fy);
		}

		private static uint Hash(uint seed, int x, int y)
		{
			unchecked
			{
				uint h = seed ^ (uint)x * 0x27D4EB2Du ^ (uint)y * 0x165667B1u;
				h ^= h >> 15;
				h *= 0x85EBCA6Bu;
				h ^= h >> 13;
				h *= 0xC2B2AE35u;
				h ^= h >> 16;
				return h;
			}
		}

		private static float Unit(uint h) => (h & 0xFFFFFF) / 16777216f;
	}
}
#endif
