#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Water;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The International Ice Patrol's iceberg shape classes, plus the melt-rounded lump the patrol
	/// gives no class to (growlers and bergy bits).
	/// </summary>
	public enum IcebergClass
	{
		/// <summary>Flat top, sheer sides, length to height above water greater than 5:1.</summary>
		Tabular,
		/// <summary>A large, smooth, rounded top.</summary>
		Dome,
		/// <summary>A large central spire or pyramid, with one or more smaller spires.</summary>
		Pinnacle,
		/// <summary>A flat top sloping down from one steep, sheer end to the other.</summary>
		Wedge,
		/// <summary>Eroded into a U-shaped slot between twin columns; the slot reaches to near the waterline.</summary>
		Drydock,
		/// <summary>Flat top and steep vertical sides, but shorter for its height than a tabular berg.</summary>
		Blocky,
		/// <summary>A melt-rounded lump with no standing shape: what growlers and bergy bits are.</summary>
		Irregular,
	}

	/// <summary>The International Ice Patrol's size classes, by height above water and waterline length.</summary>
	public enum IcebergSize
	{
		Growler,
		BergyBit,
		Small,
		Medium,
		Large,
	}

	/// <summary>One IIP size class: the height above the waterline and the length, in metres.</summary>
	public struct IcebergSizeRange
	{
		public float MinHeight, MaxHeight, MinLength, MaxLength;

		public bool Contains(float height, float length)
		{
			return height >= MinHeight && height <= MaxHeight && length >= MinLength && length <= MaxLength;
		}
	}

	/// <summary>What one iceberg is: its class, its size and its proportions above the waterline.</summary>
	public struct IcebergShape
	{
		/// <summary>Stable name; mesh files and every seed derive from it.</summary>
		public string Name;
		public IcebergClass Class;
		public IcebergSize Size;
		/// <summary>Longest horizontal extent above the waterline, metres (local x).</summary>
		public float Length;
		/// <summary>Horizontal extent across it, metres (local z).</summary>
		public float Width;
		/// <summary>Freeboard: the highest point above the waterline, metres.</summary>
		public float Height;
	}

	/// <summary>A block of ice stranded on land: a melt-rounded glacial erratic of ice.</summary>
	public struct IceBoulderShape
	{
		public string Name;
		/// <summary>Overall size in metres, as <see cref="RockShape.Size"/>.</summary>
		public float Size;
		public Vector3 Proportions;
		/// <summary>Broad melt undulation, 0..1.</summary>
		public float Lumpiness;
		/// <summary>Ablation scallops across the boulder's width; 0 for none.</summary>
		public float Scallops;
		/// <summary>Scallop dish depth as a fraction of the radius.</summary>
		public float ScallopDepth;
		/// <summary>Fracture planes left from calving, and how far they cut in (0..1).</summary>
		public int Facets;
		public float FacetDepth;
		/// <summary>How far melting has rounded those fracture edges: 0.02 fresh, 0.25 old.</summary>
		public float Softness;
	}

	/// <summary>A serac: a tower of glacier ice isolated by intersecting crevasses.</summary>
	public struct SeracShape
	{
		public string Name;
		public float Width;
		public float Depth;
		public float Height;
		/// <summary>Downslope lean: horizontal metres per metre of height.</summary>
		public float Lean;
		/// <summary>Crevasse planes cutting the flanks.</summary>
		public int Crevasses;
		/// <summary>Tilt of the broken top, degrees.</summary>
		public float TopTilt;
	}

	public enum SeaIceKind
	{
		/// <summary>Rounded discs, 0.3–3 m across, with rims raised where they jostle.</summary>
		Pancake,
		/// <summary>A flat slab of level ice with rafted, ridged edges.</summary>
		Floe,
	}

	/// <summary>A piece of sea ice: frozen sea water, floating with its own thin freeboard.</summary>
	public struct SeaIceShape
	{
		public string Name;
		public SeaIceKind Kind;
		public float Length;
		public float Width;
		/// <summary>Total thickness of the level ice, metres.</summary>
		public float Thickness;
		/// <summary>Raised rim (pancakes) or edge ridge (floes) above the level surface, metres.</summary>
		public float RimHeight;
	}

	/// <summary>A pressure ridge: level ice broken and heaped into a sail above and a keel below.</summary>
	public struct PressureRidgeShape
	{
		public string Name;
		/// <summary>Along the ridge, metres (local x).</summary>
		public float Length;
		/// <summary>Of the level ice the ridge sits in, metres (local z).</summary>
		public float Width;
		public float SailHeight;
		/// <summary>Keel depth over sail height; 3.5–5 in field surveys.</summary>
		public float KeelRatio;
		/// <summary>Thickness of the parent ice the blocks broke from, metres.</summary>
		public float BlockThickness;
		/// <summary>Rubble blocks at the finest level of detail.</summary>
		public int Blocks;
	}

	/// <summary>A closed mesh's volume, split at its waterline (y = 0), and what floats it.</summary>
	public struct IceHydrostatics
	{
		public float Volume, VolumeBelow, VolumeAbove;
		/// <summary>Centre of mass of the whole body (uniform density).</summary>
		public Vector3 Centroid;
		/// <summary>Centroid of the part below the waterline.</summary>
		public Vector3 CentreOfBuoyancy;
		public float WaterplaneArea;
		/// <summary>Centroid of the waterplane (x, z).</summary>
		public Vector2 WaterplaneCentroid;
		/// <summary>∫(z − z̄)² dA over the waterplane: what resists roll (about x).</summary>
		public float WaterplaneInertiaRoll;
		/// <summary>∫(x − x̄)² dA over the waterplane: what resists pitch (about z).</summary>
		public float WaterplaneInertiaPitch;
		/// <summary>Radii of gyration about the x, y and z axes through the centroid, metres.</summary>
		public Vector3 Gyration;

		public float SubmergedFraction => Volume > 0f ? VolumeBelow / Volume : 0f;
	}

	/// <summary>A floating ice mesh, its pivot on its waterline, with what it measured.</summary>
	public sealed class FloatingIce
	{
		public MeshBuilder Mesh;
		/// <summary>Height of the waterline above the pivot. Always 0: the pivot is on it.</summary>
		public float Waterline;
		/// <summary>Highest point above the waterline, metres.</summary>
		public float Freeboard;
		/// <summary>Deepest point below the waterline, metres (positive).</summary>
		public float Draught;
		/// <summary>Extent along local x of the part above water, metres.</summary>
		public float Length;
		/// <summary>Extent along local z of the part above water, metres.</summary>
		public float Width;
		/// <summary>Bulk density, mass over volume, kg/m³: below 900 for a berg with a firn cap.</summary>
		public float Density;
		/// <summary>Mass, kg, integrated over the density profile.</summary>
		public float Mass;
		/// <summary>Centre of mass, metres from the pivot.</summary>
		public Vector3 CentreOfMass;
		/// <summary>
		/// How much broader the body is under water than above it: the larger of the ratios of the
		/// underwater part's x and z extents to the above-water part's, measured from the mesh.
		/// </summary>
		public float Breadth = 1f;
		public IceHydrostatics Hydrostatics;
		/// <summary>Everything <see cref="WaterFloater"/> needs to ride the sea as this body would.</summary>
		public FloatingBody Body;
	}

	/// <summary>
	/// Ice: boulders stranded on land, icebergs by the International Ice Patrol's classes, sea ice
	/// (pancakes, floes and pressure ridges) and seracs, every one a closed mesh at the boulders'
	/// levels of detail.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>One chart per cube face, as the rocks are built.</b> Every closed shape here is the six
	/// faces of a cube, each a (res+1)² grid with its own seam-free UV chart, mapped onto the surface
	/// by a function of the cube point alone. A point on a shared cube edge is computed from the
	/// same cube point by every chart that owns it, so it lands on the same position and the mesh is
	/// closed — crack-free by construction, not by a weld. Grid coordinates are formed as
	/// (2i − res)/res, an exact integer numerator over the same divisor, so the copies of an edge
	/// point are bit-identical rather than merely close (RockMeshes' 2i/res − 1 can differ in the
	/// last bit across the bottom face). The triangle count is 12·res², so the levels of detail
	/// match <see cref="ProceduralArtCatalogue.BoulderResolution"/>: 1200, 300 and 48.
	/// </para>
	/// <para>
	/// <b>Two maps.</b> Boulders, growlers and seracs use the rocks' radius function — a direction
	/// times a radius — so they are star-shaped and cannot fold. Icebergs and sea ice cannot be
	/// star-shaped (a drydock's slot, a wave-cut notch and an overhanging rim are not), so they
	/// use a <i>column</i> map instead: the cube's top face becomes the top surface, a height field
	/// over the berg's plan; its four sides become the walls, rows at chosen heights and columns at
	/// fixed directions round the plan; its bottom face becomes the rounded keel. The side rows are
	/// placed where the shape needs them — the keel, both sides of the wave-cut notch, the wall and
	/// the bevel of the rim — so even the 48-triangle level keeps a waterline and a rim. Winding
	/// comes from the cube, not from the deformed shape: each face's grid orientation is known, so
	/// a triangle is wound outward even where an overhang turns it to face down.
	/// </para>
	/// <para>
	/// <b>Floating by the numbers.</b> A floating mesh's pivot is its waterline (y = 0). Glacial ice
	/// is about 917 kg/m³ solid and nearer 900 in bulk with its air bubbles, but a calved berg carries
	/// a cap of its parent ice sheet's firn: about 600 at the top, approaching 900 tens of metres down
	/// (<see cref="FirnDensity"/>). The mass and the centre of mass are integrated over that profile
	/// (<see cref="MassOf"/>), and the keel's depth is solved until the water the closed mesh displaces
	/// below y = 0 — measured exactly from the polyhedron (<see cref="Measure"/>) — weighs what the
	/// berg does. So the draught follows from the shape and the firn, not from the textbook "one ninth
	/// above": a small tabular berg, mostly firn, floats about twice as deep as it is high, a medium
	/// one nearer four times, and a pinnacle, whose spires are mostly air, under twice. Growlers and
	/// bergy bits are fragments of glacier ice, uniform at 900; sea ice is uniform at 915.
	/// </para>
	/// <para>
	/// <b>What melting and waves do.</b> Under water ice melts fastest and evenly, so the keel is
	/// smooth, rounded, and broader than the waterline (the "ram" a ship's master fears). At the
	/// waterline waves cut a notch with an overhang above it. Above water the ice keeps the planes it
	/// calved along, so the walls are faceted and sheer and the rim is only lightly bevelled. The
	/// wave-washed band and everything below it is a second submesh: washing strips the white,
	/// weathered crust and leaves the clear blue ice under it, which is what a berg's waterline looks
	/// like in every photograph.
	/// </para>
	/// <para>
	/// <b>The IIP's sizes force some shapes.</b> Most of a berg is under water, so it floats upright
	/// only with a wide waterplane: GM = I_wp/V − (z_G − z_B) must be positive. The firn cap lowers G,
	/// walls that lean in above water lighten the top, and the hull may flare gently with depth to lift
	/// B — but no more than about 1.35 times its footprint, and never as a shelf at the waterline, which
	/// reads as a halo through clear water (<see cref="SolveBreadth"/>, <see cref="MaximumRam"/>).
	/// Within that, flat-topped bergs need the waterline about four times wider than they are high.
	/// The patrol's "large" class starts at 46 m high and ends at 213 m long, so large tabular (over
	/// 5:1), blocky and dome bergs cannot float upright inside it and are not in the catalogue; every
	/// catalogue berg's metacentric height is asserted positive in roll and pitch.
	/// </para>
	/// </remarks>
	public static class IceMeshes
	{
		/// <summary>Bulk density of glacial ice with its air bubbles, kg/m³ (pure ice is 917).</summary>
		public const float GlacialIceDensity = 900f;
		/// <summary>Bulk density of first-year sea ice with its brine, kg/m³.</summary>
		public const float SeaIceDensity = 915f;
		public const float SeaWaterDensity = WaterFloater.SeaWaterDensity;

		/// <summary>Metres of surface one texture tile covers on boulders, seracs and sea ice.</summary>
		public const float TextureMetres = 2f;
		/// <summary>Metres per tile on icebergs, which are seen from hundreds of metres.</summary>
		public const float BergTextureMetres = 8f;

		/// <summary>Grid cells per cube face at each level: 1200, 300 and 48 triangles, as the boulders.</summary>
		public static readonly int[] Resolution = { 10, 5, 2 };

		/// <summary>The submesh the wave-washed band and everything under water is drawn with.</summary>
		public const int WashedSubmesh = 1;

		// ── The catalogue ─────────────────────────────────────────────

		/// <summary>The International Ice Patrol's size classes (height above water, waterline length).</summary>
		public static IcebergSizeRange SizeRange(IcebergSize size)
		{
			switch (size)
			{
				case IcebergSize.Growler: return new IcebergSizeRange { MinHeight = 0f, MaxHeight = 1f, MinLength = 0f, MaxLength = 5f };
				case IcebergSize.BergyBit: return new IcebergSizeRange { MinHeight = 1f, MaxHeight = 5f, MinLength = 5f, MaxLength = 15f };
				case IcebergSize.Small: return new IcebergSizeRange { MinHeight = 5f, MaxHeight = 15f, MinLength = 15f, MaxLength = 60f };
				case IcebergSize.Medium: return new IcebergSizeRange { MinHeight = 16f, MaxHeight = 45f, MinLength = 61f, MaxLength = 122f };
				default: return new IcebergSizeRange { MinHeight = 46f, MaxHeight = 75f, MinLength = 123f, MaxLength = 213f };
			}
		}

		private static IcebergShape Berg(IcebergClass c, IcebergSize s, float length, float width, float height)
		{
			return new IcebergShape { Name = c.ToString() + s, Class = c, Size = s, Length = length, Width = width, Height = height };
		}

		/// <summary>
		/// Every iceberg the generator makes. Sizes sit inside their IIP class; growlers and bergy
		/// bits are irregular; tabular and blocky bergs stop at medium (see the class remarks).
		/// </summary>
		/// <remarks>
		/// Proportions are the ones that float upright with the hull no more than about 1.35 times its
		/// footprint under water (see the class remarks). Blocky bergs need the waterline about 4.5 times
		/// their height, which with the patrol's under-5:1 puts a medium one at 88 × 82 × 18 m; pinnacles
		/// sit at the lower middle of their classes (36 and 60 m). The patrol's "large" class starts at
		/// 46 m high and ends at 213 m long, a length to height of at most 4.6: a large tabular berg
		/// (over 5:1) cannot exist inside it, and large blocky and dome bergs are not stable within the
		/// hull limit at the class's lowest height, so none of the three is in the catalogue.
		/// </remarks>
		public static readonly IcebergShape[] Icebergs =
		{
			Berg(IcebergClass.Irregular, IcebergSize.Growler, 4f, 3.4f, 0.7f),
			Berg(IcebergClass.Irregular, IcebergSize.BergyBit, 12f, 10f, 2.6f),
			Berg(IcebergClass.Tabular, IcebergSize.Small, 58f, 52f, 7f),
			Berg(IcebergClass.Tabular, IcebergSize.Medium, 120f, 112f, 17f),
			Berg(IcebergClass.Blocky, IcebergSize.Small, 55f, 50f, 11.5f),
			Berg(IcebergClass.Blocky, IcebergSize.Medium, 88f, 82f, 18f),
			Berg(IcebergClass.Dome, IcebergSize.Small, 50f, 44f, 10f),
			Berg(IcebergClass.Dome, IcebergSize.Medium, 110f, 100f, 22f),
			Berg(IcebergClass.Pinnacle, IcebergSize.Small, 36f, 30f, 14f),
			Berg(IcebergClass.Pinnacle, IcebergSize.Medium, 85f, 70f, 36f),
			Berg(IcebergClass.Pinnacle, IcebergSize.Large, 160f, 130f, 60f),
			Berg(IcebergClass.Wedge, IcebergSize.Small, 55f, 42f, 11f),
			Berg(IcebergClass.Wedge, IcebergSize.Medium, 115f, 90f, 24f),
			Berg(IcebergClass.Wedge, IcebergSize.Large, 210f, 195f, 48f),
			Berg(IcebergClass.Drydock, IcebergSize.Small, 55f, 40f, 12f),
			Berg(IcebergClass.Drydock, IcebergSize.Medium, 115f, 85f, 24f),
			Berg(IcebergClass.Drydock, IcebergSize.Large, 210f, 170f, 48f),
		};

		/// <summary>Ice boulders: blocks stranded on beaches and outwash plains, rounded by melting.</summary>
		public static readonly IceBoulderShape[] Boulders =
		{
			new IceBoulderShape { Name = "Rounded", Size = 1.6f, Proportions = new Vector3(1f, 0.72f, 0.88f), Lumpiness = 0.35f, Scallops = 2.4f, ScallopDepth = 0.07f, Facets = 2, FacetDepth = 0.3f, Softness = 0.25f },
			new IceBoulderShape { Name = "Slab", Size = 2.2f, Proportions = new Vector3(1.2f, 0.45f, 0.9f), Lumpiness = 0.3f, Scallops = 2.8f, ScallopDepth = 0.05f, Facets = 3, FacetDepth = 0.4f, Softness = 0.18f },
			new IceBoulderShape { Name = "Calved", Size = 1.4f, Proportions = new Vector3(0.95f, 0.85f, 0.8f), Lumpiness = 0.2f, Scallops = 2f, ScallopDepth = 0.03f, Facets = 6, FacetDepth = 0.5f, Softness = 0.05f },
		};

		/// <summary>Seracs: towers and blocks of an icefall.</summary>
		public static readonly SeracShape[] Seracs =
		{
			new SeracShape { Name = "Tower", Width = 7f, Depth = 6f, Height = 16f, Lean = 0.06f, Crevasses = 5, TopTilt = 25f },
			new SeracShape { Name = "Block", Width = 11f, Depth = 8f, Height = 8f, Lean = 0.02f, Crevasses = 6, TopTilt = 12f },
			new SeracShape { Name = "Leaning", Width = 6f, Depth = 5.5f, Height = 13f, Lean = 0.22f, Crevasses = 4, TopTilt = 35f },
		};

		/// <summary>Sea ice: pancakes and floes.</summary>
		public static readonly SeaIceShape[] SeaIce =
		{
			new SeaIceShape { Name = "Pancake", Kind = SeaIceKind.Pancake, Length = 1.8f, Width = 1.6f, Thickness = 0.1f, RimHeight = 0.05f },
			new SeaIceShape { Name = "PancakeLarge", Kind = SeaIceKind.Pancake, Length = 3f, Width = 2.6f, Thickness = 0.14f, RimHeight = 0.07f },
			new SeaIceShape { Name = "FloeSmall", Kind = SeaIceKind.Floe, Length = 8f, Width = 6f, Thickness = 0.6f, RimHeight = 0.3f },
			new SeaIceShape { Name = "Floe", Kind = SeaIceKind.Floe, Length = 18f, Width = 13f, Thickness = 1.2f, RimHeight = 0.45f },
		};

		/// <summary>Pressure ridges.</summary>
		public static readonly PressureRidgeShape[] Ridges =
		{
			new PressureRidgeShape { Name = "Ridge", Length = 16f, Width = 30f, SailHeight = 1.8f, KeelRatio = 4.5f, BlockThickness = 0.5f, Blocks = 97 },
		};

		// ── Names, for the art generator ─────────────────────────────

		public static string IcebergMesh(in IcebergShape shape, int lod) => $"Iceberg_{shape.Name}_LOD{lod}";
		public static string IceBoulderMesh(in IceBoulderShape shape, int lod) => $"IceBoulder_{shape.Name}_LOD{lod}";
		public static string SeracMesh(in SeracShape shape, int lod) => $"Serac_{shape.Name}_LOD{lod}";
		public static string SeaIceMesh(in SeaIceShape shape, int lod) => $"SeaIce_{shape.Name}_LOD{lod}";
		public static string RidgeMesh(in PressureRidgeShape shape, int lod) => $"SeaIce_{shape.Name}_LOD{lod}";

		// ── Cube charts ───────────────────────────────────────────────

		private static readonly float[] FaceSign = new float[6];

		static IceMeshes()
		{
			// Which way each face's (i, j) grid winds against its outward normal, worked out once
			// from the cube itself rather than tabulated by hand.
			for (int f = 0; f < 6; f++)
			{
				Vector3 o = CubePoint(f, 0f, 0f);
				Vector3 n = Vector3.Cross(CubePoint(f, 1f, 0f) - o, CubePoint(f, 0f, 1f) - o);
				FaceSign[f] = Vector3.Dot(n, o) >= 0f ? 1f : -1f;
			}
		}

		private static Vector3 CubePoint(int face, float a, float b)
		{
			// The rocks' layout, written per component so a shared edge is the same point from both charts.
			switch (face)
			{
				case 0: return new Vector3(1f, b, a);
				case 1: return new Vector3(-1f, b, -a);
				case 2: return new Vector3(a, 1f, b);
				case 3: return new Vector3(a, -1f, -b);
				case 4: return new Vector3(-a, b, 1f);
				default: return new Vector3(a, b, -1f);
			}
		}

		/// <summary>
		/// Six charts mapped through <paramref name="map"/>, wound outward by the cube's orientation.
		/// Triangles whose centroid is below <paramref name="washLine"/> go to submesh 1 when there are two.
		/// </summary>
		private static MeshBuilder BuildCharts(int res, Func<Vector3, Vector3> map, Vector2[] chart, int submeshes, float washLine)
		{
			var mesh = new MeshBuilder(submeshes);
			var white = new Color32(255, 255, 255, 0);
			for (int f = 0; f < 6; f++)
			{
				int first = mesh.VertexCount;
				for (int j = 0; j <= res; j++)
				{
					for (int i = 0; i <= res; i++)
					{
						float a = (float)(2 * i - res) / res;
						float b = (float)(2 * j - res) / res;
						Vector3 p = map(CubePoint(f, a, b));
						mesh.AddVertex(p, Vector3.zero, new Vector2((float)i / res * chart[f].x, (float)j / res * chart[f].y), white);
					}
				}
				for (int j = 0; j < res; j++)
				{
					for (int i = 0; i < res; i++)
					{
						int v00 = first + j * (res + 1) + i;
						int v10 = v00 + 1, v01 = v00 + res + 1, v11 = v01 + 1;
						// Each cell is split along whichever diagonal leaves both triangles facing the way
						// the whole cell does. The fixed diagonal fails in two places: a face's corner cells,
						// whose three edge vertices a cap lays flat across the chord of its rim, and cells
						// that straddle a sharp corner of the plan, where the middle vertex is pushed past
						// the chord. Either way a sliver faces backwards.
						if (SplitAcross(mesh, v00, v10, v11, v01))
						{
							AddOriented(mesh, v00, v10, v01, FaceSign[f], submeshes, washLine);
							AddOriented(mesh, v10, v11, v01, FaceSign[f], submeshes, washLine);
						}
						else
						{
							AddOriented(mesh, v00, v10, v11, FaceSign[f], submeshes, washLine);
							AddOriented(mesh, v00, v11, v01, FaceSign[f], submeshes, washLine);
						}
					}
				}
			}
			return mesh;
		}

		/// <summary>True when the v10–v01 diagonal makes a better pair of triangles than v00–v11.</summary>
		private static bool SplitAcross(MeshBuilder mesh, int v00, int v10, int v11, int v01)
		{
			Vector3 p00 = mesh.Positions[v00], p10 = mesh.Positions[v10], p11 = mesh.Positions[v11], p01 = mesh.Positions[v01];
			// The cell's vector area, in the grid's own orientation, and how well each triangle agrees with it.
			Vector3 cell = Vector3.Cross(p11 - p00, p01 - p10);
			float along = Mathf.Min(Agreement(p00, p10, p11, cell), Agreement(p00, p11, p01, cell));
			float across = Mathf.Min(Agreement(p00, p10, p01, cell), Agreement(p10, p11, p01, cell));
			return across > along + 1e-4f;
		}

		private static float Agreement(Vector3 a, Vector3 b, Vector3 c, Vector3 cell)
		{
			Vector3 n = Vector3.Cross(b - a, c - a);
			float m = n.magnitude * cell.magnitude;
			return m > 1e-20f ? Vector3.Dot(n, cell) / m : -1f;
		}

		private static void AddOriented(MeshBuilder mesh, int a, int b, int c, float sign, int submeshes, float washLine)
		{
			Vector3 pa = mesh.Positions[a], pb = mesh.Positions[b], pc = mesh.Positions[c];
			Vector3 n = Vector3.Cross(pb - pa, pc - pa);
			int sub = submeshes > 1 && (pa.y + pb.y + pc.y) / 3f < washLine ? WashedSubmesh : 0;
			// MeshBuilder keeps the order when the normal agrees with the facing: hand it the
			// triangle's own normal, flipped where the cube says this grid winds inward.
			mesh.AddTriangle(sub, a, b, c, sign >= 0f ? n : -n);
		}

		private static Vector2[] UniformCharts(float metresU, float metresV, float textureMetres)
		{
			var chart = new Vector2[6];
			for (int f = 0; f < 6; f++)
			{
				chart[f] = new Vector2(metresU / textureMetres, metresV / textureMetres);
			}
			return chart;
		}

		private static void Finish(MeshBuilder mesh)
		{
			mesh.RecalculateNormals(true);
			mesh.RecalculateTangents();
		}

		// ── Small maths ───────────────────────────────────────────────

		/// <summary>HLSL's smoothstep (Mathf.SmoothStep interpolates between its first two arguments instead).</summary>
		private static float Smoothstep(float edge0, float edge1, float x)
		{
			float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
			return t * t * (3f - 2f * t);
		}

		private static float SoftMin(float a, float b, float k)
		{
			float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
			return Mathf.Lerp(b, a, h) - k * h * (1f - h);
		}

		private static float SoftMax(float a, float b, float k) => -SoftMin(-a, -b, k);

		/// <summary>
		/// Ablation scallops on a direction: 0 on the sharp rims between dishes, 1 at a dish's
		/// deepest point, a paraboloid in between. 3D cellular noise, so it wraps the whole body.
		/// </summary>
		private static float Scallop(Vector3 p, int seed)
		{
			int cx = Mathf.FloorToInt(p.x), cy = Mathf.FloorToInt(p.y), cz = Mathf.FloorToInt(p.z);
			float f1 = float.MaxValue, f2 = float.MaxValue;
			for (int z = -1; z <= 1; z++)
			{
				for (int y = -1; y <= 1; y++)
				{
					for (int x = -1; x <= 1; x++)
					{
						uint h = ProceduralNoise.Hash(cx + x, cy + y, cz + z, seed);
						float fx = cx + x + 0.15f + 0.7f * ProceduralNoise.ToUnit(h);
						float fy = cy + y + 0.15f + 0.7f * ProceduralNoise.ToUnit(ProceduralNoise.Mix(h ^ 0x68e31da4u));
						float fz = cz + z + 0.15f + 0.7f * ProceduralNoise.ToUnit(ProceduralNoise.Mix(h ^ 0x1b56c4e9u));
						float dx = fx - p.x, dy = fy - p.y, dz = fz - p.z;
						float d = dx * dx + dy * dy + dz * dz;
						if (d < f1)
						{
							f2 = f1;
							f1 = d;
						}
						else if (d < f2)
						{
							f2 = d;
						}
					}
				}
			}
			float d1 = Mathf.Sqrt(f1), d2 = Mathf.Sqrt(f2);
			// 0 at the feature point, 1 on the bisector with the next one: the rim between two dishes.
			float s = Mathf.Clamp01(2f * d1 / Mathf.Max(1e-5f, d1 + d2));
			return 1f - s * s;
		}

		// ── Lumps: boulders, growlers, seracs ─────────────────────────

		private sealed class Lump
		{
			public Vector3 Semi;          // semi-axes, metres
			public float BoxExponent = 2f; // 2 an ellipsoid, 6 a rounded box
			public float Lumpiness;
			public float ScallopCells;
			public float ScallopDepth;
			public Vector4[] Planes = new Vector4[0];
			public float PlaneSoftness = 0.06f;
			public float Lean;
			public int Seed;
		}

		private static Vector3 LumpPoint(Lump l, Vector3 cube)
		{
			Vector3 dir = cube.normalized;
			float p = l.BoxExponent;
			float r = p == 2f ? 1f : Mathf.Pow(Mathf.Pow(Mathf.Abs(dir.x), p) + Mathf.Pow(Mathf.Abs(dir.y), p) + Mathf.Pow(Mathf.Abs(dir.z), p), -1f / p);
			// Melt is broad and smooth: one low octave of undulation, nothing ridged.
			r *= 1f + ProceduralNoise.Fbm3(dir * 1.3f + new Vector3(3.1f, 7.7f, 1.9f), 3, 0.45f, l.Seed) * 0.16f * l.Lumpiness;
			if (l.ScallopCells > 0f && l.ScallopDepth > 0f)
			{
				r *= 1f - l.ScallopDepth * Scallop(dir * l.ScallopCells + new Vector3(11.3f, 5.9f, 2.3f), l.Seed + 29);
			}
			for (int i = 0; i < l.Planes.Length; i++)
			{
				var n = new Vector3(l.Planes[i].x, l.Planes[i].y, l.Planes[i].z);
				float along = Vector3.Dot(dir, n);
				if (along > 1e-3f)
				{
					r = SoftMin(r, l.Planes[i].w / along, l.PlaneSoftness);
				}
			}
			Vector3 pos = Vector3.Scale(dir * r, l.Semi);
			pos.x += l.Lean * (pos.y + l.Semi.y);
			return pos;
		}

		private static MeshBuilder BuildLump(Lump l, int res, float textureMetres, int submeshes, float washLine)
		{
			float u = 2f * Mathf.Max(l.Semi.x, l.Semi.z), v = 2f * l.Semi.y;
			var chart = new Vector2[6];
			for (int f = 0; f < 6; f++)
			{
				// Side charts run round the body and up it; the top and bottom across it.
				chart[f] = f == 2 || f == 3
					? new Vector2(2f * l.Semi.x, 2f * l.Semi.z) / textureMetres
					: new Vector2(u, v) / textureMetres;
			}
			return BuildCharts(Mathf.Max(1, res), q => LumpPoint(l, q), chart, submeshes, washLine);
		}

		/// <summary>Flattens the underside and drops the pivot into the ground, as rocks are bedded.</summary>
		private static void Bed(MeshBuilder mesh, float floorShare, float pivotShare)
		{
			Bounds raw = mesh.Bounds;
			float floor = raw.min.y + raw.size.y * floorShare;
			float pivot = raw.min.y + raw.size.y * pivotShare;
			for (int i = 0; i < mesh.VertexCount; i++)
			{
				Vector3 p = mesh.Positions[i];
				if (p.y < floor)
				{
					p.y = floor - (floor - p.y) * 0.25f;
				}
				p.y -= pivot;
				mesh.Positions[i] = p;
			}
		}

		/// <summary>
		/// An ice boulder: a melt-rounded erratic of ice, dished all over with ablation scallops,
		/// bedded into the ground like a rock.
		/// </summary>
		/// <remarks>
		/// Scallops are the hollows turbulent melt water or air carves into ice: smooth dishes meeting
		/// in sharp rims, a hammered-metal look, the size of each set by the flow (centimetres in a
		/// stream, tens of centimetres in still air). At boulder scale the mesh carries the large ones;
		/// the fine ones belong in the surface's normal map.
		/// </remarks>
		public static MeshBuilder BuildBoulder(in IceBoulderShape shape, int resolution, int seed)
		{
			int s = ProceduralNoise.SeedFor("Ice/" + shape.Name, seed);
			var lump = new Lump
			{
				Semi = shape.Proportions * (0.5f * shape.Size),
				Lumpiness = shape.Lumpiness,
				ScallopCells = shape.Scallops,
				ScallopDepth = shape.ScallopDepth,
				Planes = FracturePlanes(shape.Facets, shape.FacetDepth, s),
				PlaneSoftness = Mathf.Max(0.02f, shape.Softness),
				Seed = s,
			};
			MeshBuilder mesh = BuildLump(lump, resolution, TextureMetres, 1, float.NegativeInfinity);
			Bed(mesh, 0.12f, 0.2f);
			Finish(mesh);
			return mesh;
		}

		private static Vector4[] FracturePlanes(int count, float depth, int seed)
		{
			var rng = new DeterministicRNG(seed ^ 0x5bd1e995);
			var planes = new Vector4[Mathf.Max(0, count)];
			for (int i = 0; i < planes.Length; i++)
			{
				float y = rng.Range(-0.2f, 0.9f);
				float a = rng.NextFloat() * Mathf.PI * 2f;
				float h = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
				var n = new Vector3(Mathf.Cos(a) * h, y, Mathf.Sin(a) * h);
				float d = Mathf.Lerp(0.95f, 0.6f, Mathf.Clamp01(depth)) * rng.Range(0.9f, 1.05f);
				planes[i] = new Vector4(n.x, n.y, n.z, d);
			}
			return planes;
		}

		/// <summary>
		/// A serac: a rounded-box tower cut by two families of near-vertical crevasse planes (the
		/// transverse and the longitudinal crevasses that isolated it) and a tilted, broken top.
		/// </summary>
		/// <remarks>
		/// Seracs stand where a glacier falls steeply and its crevasses cross: the two sets cut the ice
		/// into blocks, and each block leans and tilts as the ice under it flows. So the flanks are
		/// planes in two directions roughly at right angles, the top is a tilted plane, and the edges
		/// between are sharp — ice that has not had time to melt round. Bedded like a rock, but only
		/// slightly: it is part of the glacier it stands on.
		/// </remarks>
		public static MeshBuilder BuildSerac(in SeracShape shape, int resolution, int seed)
		{
			int s = ProceduralNoise.SeedFor("Serac/" + shape.Name, seed);
			var rng = new DeterministicRNG(s ^ 0x2545f491);
			var planes = new List<Vector4>();
			for (int i = 0; i < shape.Crevasses; i++)
			{
				// Alternate the two families: across (±x) and along (±z) the flow, a few degrees off true.
				float baseAngle = (i % 2 == 0 ? 0f : 90f) + (i / 2 % 2 == 0 ? 0f : 180f);
				float a = (baseAngle + rng.Range(-14f, 14f)) * Mathf.Deg2Rad;
				float y = rng.Range(-0.12f, 0.12f);
				float h = Mathf.Sqrt(1f - y * y);
				planes.Add(new Vector4(Mathf.Cos(a) * h, y, Mathf.Sin(a) * h, rng.Range(0.72f, 0.9f)));
			}
			// The broken top: one plane tilted the top tilt from level, one smaller notch off a corner.
			float tilt = shape.TopTilt * Mathf.Deg2Rad;
			float az = rng.NextFloat() * Mathf.PI * 2f;
			planes.Add(new Vector4(Mathf.Sin(tilt) * Mathf.Cos(az), Mathf.Cos(tilt), Mathf.Sin(tilt) * Mathf.Sin(az), rng.Range(0.82f, 0.9f)));
			float az2 = az + rng.Range(2f, 4.2f);
			planes.Add(new Vector4(0.62f * Mathf.Cos(az2), 0.48f, 0.62f * Mathf.Sin(az2), rng.Range(0.8f, 0.9f)));
			var lump = new Lump
			{
				Semi = new Vector3(shape.Width * 0.5f, shape.Height * 0.5f, shape.Depth * 0.5f),
				BoxExponent = 5f,
				Lumpiness = 0.25f,
				ScallopCells = 1.6f,
				ScallopDepth = 0.02f,
				Planes = planes.ToArray(),
				PlaneSoftness = 0.025f,
				Lean = shape.Lean,
				Seed = s,
			};
			MeshBuilder mesh = BuildLump(lump, resolution, TextureMetres, 1, float.NegativeInfinity);
			Bed(mesh, 0.05f, 0.08f);
			Finish(mesh);
			return mesh;
		}

		// ── Columns: icebergs and sea ice ─────────────────────────────

		/// <summary>The column map's parameters: a plan, a profile up its walls, and a top height field.</summary>
		private sealed class Column
		{
			public float HalfLength, HalfWidth;
			/// <summary>Superellipse exponent of the plan: 2 an ellipse, 6 a rounded rectangle.</summary>
			public float Exponent = 2f;
			public float Irregularity;
			/// <summary>Vertical calving planes in plan, normalised (nx, nz, distance).</summary>
			public Vector3[] Facets = new Vector3[0];
			/// <summary>The facets cut the full depth (sea ice, cracked through) rather than melting out below water.</summary>
			public bool FacetsThrough;
			public float Freeboard;
			public float Draught;
			/// <summary>Share of the draught where the keel starts to round in.</summary>
			public float KeelStart = 0.5f;
			/// <summary>2 an elliptical keel, higher a flat bottom with a rounded edge.</summary>
			public float BottomExponent = 2f;
			/// <summary>How much broader than the waterline the body is under water.</summary>
			public float Ram;
			public float NotchDepth, NotchAbove, NotchBelow, NotchPeak;
			/// <summary>How much the walls narrow toward the top, share of the radius at full height.</summary>
			public float Taper;
			/// <summary>Radius of the rim's bevel, metres.</summary>
			public float Bevel;
			/// <summary>The top surface, metres above the waterline, at a plan position (x, z).</summary>
			public Func<float, float, float> Top;
			/// <summary>Least height of wall above the notch.</summary>
			public float MinWall;
			public int Seed;
		}

		/// <summary>Plan radius, normalised (1 on the nominal outline), in a plan direction.</summary>
		private static float PlanRadius(Column c, float cx, float cz, bool faceted)
		{
			float p = c.Exponent;
			float r = Mathf.Pow(Mathf.Pow(Mathf.Abs(cx), p) + Mathf.Pow(Mathf.Abs(cz), p), -1f / p);
			r *= 1f + c.Irregularity * 0.07f * ProceduralNoise.Fbm3(new Vector3(cx * 1.4f + 4.3f, 2.9f, cz * 1.4f + 1.7f), 3, 0.5f, c.Seed);
			if (faceted)
			{
				foreach (Vector3 facet in c.Facets)
				{
					float along = cx * facet.x + cz * facet.y;
					if (along > 1e-3f)
					{
						// Chipped, not razor-cut: a ring of the top that straddles a knife corner folds.
						r = SoftMin(r, facet.z / along, 0.05f);
					}
				}
			}
			return r;
		}

		private static float NotchBump(Column c, float y)
		{
			if (c.NotchDepth <= 0f || y <= -c.NotchBelow || y >= c.NotchAbove)
			{
				return 0f;
			}
			float t = y < c.NotchPeak ? (c.NotchPeak - y) / (c.NotchPeak + c.NotchBelow) : (y - c.NotchPeak) / (c.NotchAbove - c.NotchPeak);
			float k = 1f - t * t;
			return k * k;
		}

		/// <summary>Where a wall at plan direction (cx, cz) stands at height y: the horizontal offset, metres.</summary>
		private static Vector2 WallPoint(Column c, float cx, float cz, float y)
		{
			float above = PlanRadius(c, cx, cz, true);
			float below = PlanRadius(c, cx, cz, false);
			// Calving planes are sheer above water and melt away below it, over a sixth of the draught;
			// sea ice is broken through its whole thickness, so its planes go all the way down.
			float r = c.FacetsThrough ? above
				: Mathf.Lerp(below, above, Smoothstep(-Mathf.Max(2f * c.NotchBelow, 0.15f * c.Draught), c.NotchAbove, y));
			float f;
			if (y >= 0f)
			{
				f = 1f - c.Taper * Mathf.Clamp01(y / Mathf.Max(1e-3f, c.Freeboard));
			}
			else
			{
				// Under water: broader (the ram), melted smooth, wandering a little with depth.
				float u = -y / Mathf.Max(1e-3f, c.Draught);
				// Melt is not even: the ram runs out further in some directions (spurs) than others.
				float spur = 1f + 0.15f * c.Irregularity * ProceduralNoise.Fbm3(new Vector3(cx * 1.7f + 2.9f, 0.6f, cz * 1.7f + 6.4f), 2, 0.5f, c.Seed + 43);
				// The flare starts vertical at the waterline and grows smoothly to the keel's start: an
				// ice foot, never a shelf (a shelf just under the surface reads as a halo through the water).
				float ram = c.Ram * spur * Smoothstep(0f, Mathf.Max(0.2f, c.KeelStart), u);
				float wander = c.Irregularity * 0.06f * ProceduralNoise.Fbm3(new Vector3(cx * 1.4f + 8.1f, u * 2.2f, cz * 1.4f + 3.3f), 3, 0.5f, c.Seed + 41);
				f = 1f + ram + wander * Smoothstep(0f, 0.1f, u);
			}
			var v = new Vector2(cx * c.HalfLength, cz * c.HalfWidth) * (r * f);
			float bump = NotchBump(c, y);
			if (bump > 0f)
			{
				float m = v.magnitude;
				v *= Mathf.Max(0.3f, (m - c.NotchDepth * bump) / Mathf.Max(1e-4f, m));
			}
			return v;
		}

		/// <summary>Height of the rim in a plan direction: the top surface where it meets the wall.</summary>
		private static float RimHeight(Column c, float cx, float cz)
		{
			Vector2 w = WallPoint(c, cx, cz, c.NotchAbove);
			return Mathf.Max(c.Top(w.x, w.y), c.NotchAbove + c.MinWall + c.Bevel);
		}

		private static Vector3 ColumnPoint(Column c, Vector3 q, int res)
		{
			// The square's elliptical-grid map onto the unit disc (u = a√(1 − b²/2), v = b√(1 − a²/2)):
			// smooth, the square's edge onto the circle, and no cell with three corners on one ring,
			// which the plain "Chebyshev radius" map has at every corner of the top face and which
			// left flat tops full of slivers. Walls take their direction from it too, so the rim's
			// columns and the top's outer ring agree.
			float u = q.x * Mathf.Sqrt(Mathf.Max(0f, 1f - 0.5f * q.z * q.z));
			float v = q.z * Mathf.Sqrt(Mathf.Max(0f, 1f - 0.5f * q.x * q.x));
			float rho = Mathf.Sqrt(u * u + v * v);
			float cx = rho > 1e-6f ? u / rho : 1f;
			float cz = rho > 1e-6f ? v / rho : 0f;
			// Edge points (exact ±1 on the cube) always take the wall branch, whichever chart asks:
			// one code path, one position.
			if (Mathf.Max(Mathf.Abs(q.x), Mathf.Abs(q.z)) >= 1f)
			{
				return Wall(c, cx, cz, (q.y + 1f) * 0.5f, res);
			}
			return q.y > 0f ? TopCap(c, cx, cz, Mathf.Min(1f, rho), res) : Keel(c, cx, cz, Mathf.Min(1f, rho));
		}

		// The wall's rows, by the share s of the side face's height they sit at. At even resolutions
		// rows land on the keel, below the notch, its apex, above it, the wall's top and the rim. At
		// odd ones (five rows) the apex row goes instead to half the draught: a metre-high notch is
		// under a pixel at that level's distance, while one band from the keel to the waterline would
		// leave a broad body a knife edge where its base meets its keel.
		private const float RowNotchAbove = 0.6f, RowWallTop = 0.8f;

		private static float RowNotchBelow(int res) => (res & 1) == 1 ? 0.4f : 0.2f;

		private static float RowNotchPeak(int res) => (res & 1) == 1 ? 0.5f : 0.4f;

		private static Vector3 Wall(Column c, float cx, float cz, float s, int res)
		{
			float rowBelow = RowNotchBelow(res), rowPeak = RowNotchPeak(res);
			float keel = -c.KeelStart * c.Draught;
			float rim = RimHeight(c, cx, cz);
			float wallTop = rim - c.Bevel;
			float y;
			if (s <= rowBelow)
			{
				y = Mathf.Lerp(keel, -c.NotchBelow, s / rowBelow);
			}
			else if (s <= rowPeak)
			{
				y = Mathf.Lerp(-c.NotchBelow, c.NotchPeak, (s - rowBelow) / (rowPeak - rowBelow));
			}
			else if (s <= RowNotchAbove)
			{
				y = Mathf.Lerp(c.NotchPeak, c.NotchAbove, (s - rowPeak) / (RowNotchAbove - rowPeak));
			}
			else if (s <= RowWallTop)
			{
				y = Mathf.Lerp(c.NotchAbove, wallTop, (s - RowNotchAbove) / (RowWallTop - RowNotchAbove));
			}
			else
			{
				// The rim's bevel: a quarter round from the wall's top in to the edge of the top surface.
				float t = (s - RowWallTop) / (1f - RowWallTop) * Mathf.PI * 0.5f;
				Vector2 w = WallPoint(c, cx, cz, wallTop);
				float m = Mathf.Max(1e-4f, w.magnitude);
				float inset = c.Bevel * (1f - Mathf.Cos(t));
				Vector2 h = w * ((m - inset) / m);
				return new Vector3(h.x, wallTop + c.Bevel * Mathf.Sin(t), h.y);
			}
			Vector2 p = WallPoint(c, cx, cz, y);
			return new Vector3(p.x, y, p.y);
		}

		private static Vector3 TopCap(Column c, float cx, float cz, float rho, int res)
		{
			float g = rho;
			if ((res & 1) == 1)
			{
				// An odd grid has no centre vertex: pull its innermost ring nearly to the centre, so a
				// summit (a dome's crown, a pinnacle's spire) keeps a vertex near its peak at every level.
				float a = 1f / res;
				float first = Mathf.Sqrt(2f) * a * Mathf.Sqrt(1f - 0.5f * a * a);
				g = Mathf.Lerp(0.12f / res, 1f, (rho - first) / (1f - first));
			}
			float rim = RimHeight(c, cx, cz);
			Vector2 w = WallPoint(c, cx, cz, rim - c.Bevel);
			float m = Mathf.Max(1e-4f, w.magnitude);
			Vector2 h = w * ((m - c.Bevel) / m * g);
			float y = Mathf.Lerp(c.Top(h.x, h.y), rim, Smoothstep(0.78f, 1f, g));
			return new Vector3(h.x, y, h.y);
		}

		private static Vector3 Keel(Column c, float cx, float cz, float rho)
		{
			// A superellipse quarter from the keel's start down to its deepest point, its rings evenly
			// spaced in radius: spaced by angle they crowd at the rim and a wandering wall folds them.
			float pb = Mathf.Max(2f, c.BottomExponent);
			float across = rho;
			float down = Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Pow(rho, pb)), 1f / pb);
			float keel = -c.KeelStart * c.Draught;
			Vector2 w = WallPoint(c, cx, cz, keel);
			return new Vector3(w.x * across, keel - (c.Draught - c.KeelStart * c.Draught) * down, w.y * across);
		}

		private static MeshBuilder BuildColumn(Column c, int res, float textureMetres, int submeshes)
		{
			float height = c.KeelStart * c.Draught + c.Freeboard;
			var chart = new Vector2[6];
			for (int f = 0; f < 6; f++)
			{
				switch (f)
				{
					case 0:
					case 1: chart[f] = new Vector2(2f * c.HalfWidth, height) / textureMetres; break;
					case 2:
					case 3: chart[f] = new Vector2(2f * c.HalfLength, 2f * c.HalfWidth) / textureMetres; break;
					default: chart[f] = new Vector2(2f * c.HalfLength, height) / textureMetres; break;
				}
			}
			int r = Mathf.Max(1, res);
			return BuildCharts(r, q => ColumnPoint(c, q, r), chart, submeshes, c.NotchAbove);
		}

		/// <summary>
		/// Solves the draught: the keel's depth at which the water displaced weighs what the body does
		/// (Archimedes, ρ_w·V_below = M, M integrated over <paramref name="density"/>). Illinois regula
		/// falsi on a nearly linear function.
		/// </summary>
		private static void SolveDraught(Column c, int res, Func<float, float> density)
		{
			float lo = Mathf.Max(c.NotchBelow * 1.6f / Mathf.Max(0.05f, c.KeelStart), c.Freeboard * 0.2f);
			float hi = Mathf.Max(lo * 2f, c.Freeboard * 40f);
			float Excess(float d)
			{
				c.Draught = d;
				MeshBuilder m = BuildColumn(c, res, 1f, 1);
				IceHydrostatics h = Measure(m);
				return SeaWaterDensity * h.VolumeBelow - MassOf(m.Positions, m.Submeshes, density).Mass;
			}
			float flo = Excess(lo), fhi = Excess(hi);
			if (flo >= 0f)
			{
				c.Draught = lo;
				return;
			}
			int side = 0;
			for (int i = 0; i < 40; i++)
			{
				float d = (lo * fhi - hi * flo) / (fhi - flo);
				float fd = Excess(d);
				if (Mathf.Abs(fd) < 1e-5f * Mathf.Abs(fhi - flo) || hi - lo < 1e-4f * hi)
				{
					c.Draught = d;
					return;
				}
				if (fd > 0f)
				{
					hi = d;
					fhi = fd;
					if (side == 1)
					{
						flo *= 0.5f;
					}
					side = 1;
				}
				else
				{
					lo = d;
					flo = fd;
					if (side == -1)
					{
						fhi *= 0.5f;
					}
					side = -1;
				}
			}
			c.Draught = (lo * fhi - hi * flo) / (fhi - flo);
		}

		// ── Density ───────────────────────────────────────────────────

		/// <summary>Density of the firn at a berg's top, kg/m³: old, wind-packed firn, not fresh snow.</summary>
		public const float FirnSurfaceDensity = 600f;

		/// <summary>Depth over which the firn's deficit from glacial ice falls by e, metres.</summary>
		public const float FirnDepthScale = 20f;

		/// <summary>
		/// Density at a depth below the berg's top: ρ(d) = ρ_ice − (ρ_ice − ρ_s)·e^(−d/d₀), from
		/// <see cref="FirnSurfaceDensity"/> at the top to <see cref="GlacialIceDensity"/> deep down.
		/// </summary>
		/// <remarks>
		/// The exponential is the standard fit to measured firn densification (the Herron–Langway
		/// model's form; Cuffey &amp; Paterson, <i>The Physics of Glaciers</i>, 2010, ch. 2). A calved berg
		/// carries its parent glacier's or ice shelf's firn as a cap: 600 at the surface (packed firn on
		/// a berg that has drifted for a season, not 350 fresh snow), 790 at 20 m, 860 at 40 m and within
		/// 5 kg/m³ of 900 by 80 m. It is layered by depth below the berg's highest point, as it lay in
		/// the ice sheet before calving: right for tabular and blocky bergs, an approximation for those
		/// that have rolled. Growlers and bergy bits are fragments of glacier ice, uniform at 900.
		/// </remarks>
		public static float FirnDensity(float depthBelowTop)
		{
			return GlacialIceDensity - (GlacialIceDensity - FirnSurfaceDensity) * Mathf.Exp(-Mathf.Max(0f, depthBelowTop) / FirnDepthScale);
		}

		/// <summary>A body's mass and where it is centred.</summary>
		public struct IceMass
		{
			public float Mass;
			public Vector3 CentreOfMass;
		}

		/// <summary>
		/// Mass and centre of mass of a closed mesh whose density varies with height
		/// (<paramref name="density"/> of y), by slicing it into horizontal layers.
		/// </summary>
		/// <remarks>
		/// The volume under a plane and its centroid are exact (<see cref="Measure(List{Vector3}, List{List{int}}, float)"/>),
		/// so differencing them at successive heights gives each layer's volume and first moment
		/// exactly; only the density is taken at the layer's middle. Forty layers put a 200 m berg's
		/// layers 5 m apart against a firn profile that changes over 20 m: the mass is good to a few
		/// parts in ten thousand.
		/// </remarks>
		public static IceMass MassOf(List<Vector3> positions, List<List<int>> submeshes, Func<float, float> density, int layers = 40)
		{
			float minY = float.MaxValue, maxY = float.MinValue;
			foreach (Vector3 p in positions)
			{
				minY = Mathf.Min(minY, p.y);
				maxY = Mathf.Max(maxY, p.y);
			}
			double mass = 0, mx = 0, my = 0, mz = 0;
			double prevV = 0, prevX = 0, prevY = 0, prevZ = 0;
			float step = (maxY - minY) / layers;
			for (int k = 1; k <= layers; k++)
			{
				// The last plane sits above the top, so the last layer closes the whole body.
				float level = k == layers ? maxY + 1f : minY + step * k;
				IceHydrostatics h = Measure(positions, submeshes, level);
				double v = h.VolumeBelow, sx = v * h.CentreOfBuoyancy.x, sy = v * h.CentreOfBuoyancy.y, sz = v * h.CentreOfBuoyancy.z;
				double rho = density(minY + step * (k - 0.5f));
				mass += rho * (v - prevV);
				mx += rho * (sx - prevX);
				my += rho * (sy - prevY);
				mz += rho * (sz - prevZ);
				prevV = v; prevX = sx; prevY = sy; prevZ = sz;
			}
			return new IceMass
			{
				Mass = (float)mass,
				CentreOfMass = mass > 0 ? new Vector3((float)(mx / mass), (float)(my / mass), (float)(mz / mass)) : Vector3.zero,
			};
		}

		/// <summary>Least metacentric height, as a share of the waterline's width, a berg is built to keep.</summary>
		public const float StabilityMargin = 0.005f;

		/// <summary>
		/// How much broader than its waterline a berg's body may flare under water to float upright:
		/// with the spurs' ±15 % that keeps the body within about 1.35 times its footprint above water.
		/// </summary>
		public const float MaximumRam = 0.25f;

		/// <summary>
		/// Broadens the body under water, from the class's own ram up to <see cref="MaximumRam"/>, until
		/// the berg floats upright with <see cref="StabilityMargin"/> to spare in both roll and pitch; the
		/// draught is re-solved at every step, at the finest level's resolution, and every level shares both.
		/// </summary>
		/// <remarks>
		/// A berg that would not float upright rolls over until it does, so every berg one sees is
		/// stable as it lies — and with most of it under water that is a hard condition. GM = I_wp/V −
		/// (z_G − z_B): the waterplane is fixed by the class's shape above water, the firn cap lowers G,
		/// and a body flaring gently with depth lifts B. The narrowest body that is stable is kept. A
		/// catalogue berg that is not stable even at the cap is a catalogue error, and a test says so.
		/// </remarks>
		private static void SolveBreadth(Column c, Func<float, float> density)
		{
			int res = Resolution[0];
			float Margin(float ram)
			{
				c.Ram = ram;
				SolveDraught(c, res, density);
				MeshBuilder m = BuildColumn(c, res, 1f, 1);
				IceHydrostatics h = Measure(m);
				IceMass mass = MassOf(m.Positions, m.Submeshes, density);
				FloatingBody body = ToBody(in h, mass.Mass, mass.CentreOfMass);
				float width = Mathf.Max(1e-3f, 2f * Mathf.Min(body.HalfLength, body.HalfWidth));
				return Mathf.Min(body.MetacentricHeightRoll, body.MetacentricHeightPitch) / width - StabilityMargin;
			}
			float lo = Mathf.Min(c.Ram, MaximumRam);
			if (Margin(lo) >= 0f)
			{
				c.Ram = lo;
				return;
			}
			float hi = MaximumRam;
			if (Margin(hi) < 0f)
			{
				c.Ram = hi;
				return;
			}
			for (int i = 0; i < 14; i++)
			{
				float mid = 0.5f * (lo + hi);
				if (Margin(mid) >= 0f)
				{
					hi = mid;
				}
				else
				{
					lo = mid;
				}
			}
			c.Ram = hi;
		}

		private static float Noise2(float x, float z, float frequency, int octaves, int seed)
		{
			return ProceduralNoise.Fbm3(new Vector3(x * frequency + 13.1f, z * frequency + 5.7f, 2.3f), octaves, 0.5f, seed);
		}

		/// <summary>The iceberg's column: its plan, its walls' profile and its class's top surface.</summary>
		private static Column BergColumn(in IcebergShape shape, int s)
		{
			var rng = new DeterministicRNG(s ^ 0x3c6ef372);
			float H = shape.Height, L = shape.Length, W = shape.Width;
			float hl = L * 0.5f, hw = W * 0.5f;
			var c = new Column
			{
				HalfLength = hl,
				HalfWidth = hw,
				Freeboard = H,
				Irregularity = 1f,
				Seed = s,
				// The notch is the sea's work, so it is sized by the waves that cut it, not by the berg:
				// a metre or two high and a couple deep on a big berg, a hand's width on a growler.
				NotchAbove = Mathf.Clamp(0.08f * H, 0.25f, 2.5f),
				Bevel = Mathf.Clamp(0.018f * L, 0.08f, 2.5f),
				MinWall = Mathf.Clamp(0.04f * H, 0.1f, 2f),
			};
			c.NotchBelow = c.NotchAbove * 0.7f;
			c.NotchPeak = c.NotchAbove * 0.2f;
			// Never deeper than three quarters of its height: a notch is a rounded groove, not a slit.
			c.NotchDepth = Mathf.Min(Mathf.Clamp(0.025f * L, 0.1f, 3f), 0.75f * (c.NotchAbove + c.NotchBelow));
			int seed = s;
			switch (shape.Class)
			{
				case IcebergClass.Tabular:
				{
					c.Exponent = 6f;
					c.Facets = PlanFacets(rng, 4, 0.95f, 0.99f);
					c.Ram = 0.03f;
					c.KeelStart = 0.85f;
					c.Taper = 0.02f;
					c.BottomExponent = 6f;
					c.Top = (x, z) => H * (0.985f + 0.015f * Noise2(x / hl, z / hw, 2f, 3, seed));
					break;
				}
				case IcebergClass.Blocky:
				{
					c.Exponent = 4.5f;
					c.Facets = PlanFacets(rng, 5, 0.9f, 0.98f);
					c.Ram = 0.08f;
					// Blocky bergs need a broad base to float upright; it rounds in from half the draught.
					c.KeelStart = 0.5f;
					c.BottomExponent = 2f;
					c.Taper = 0.08f;
					float tilt = rng.Range(-0.04f, 0.04f);
					c.Top = (x, z) => H * (0.95f + 0.035f * Noise2(x / hl, z / hw, 1.6f, 3, seed) + tilt * (x / hl - 1f));
					break;
				}
				case IcebergClass.Dome:
				{
					c.Exponent = 2.2f;
					c.Ram = 0.14f;
					c.KeelStart = 0.45f;
					c.Taper = 0.1f;
					float rim = 0.3f * H;
					c.Top = (x, z) =>
					{
						float r = Mathf.Clamp01(Mathf.Sqrt((x / hl) * (x / hl) + (z / hw) * (z / hw)));
						return rim + (H - rim) * Mathf.Pow(1f - r * r, 0.65f) + 0.02f * H * Noise2(x / hl, z / hw, 1.5f, 2, seed) * r;
					};
					break;
				}
				case IcebergClass.Pinnacle:
				{
					c.Exponent = 2.4f;
					c.Facets = PlanFacets(rng, 3, 0.9f, 0.97f);
					c.Ram = 0.15f;
					c.KeelStart = 0.4f;
					c.Taper = 0.15f;
					float rim = 0.2f * H;
					// A great central pyramid whose concave flanks run down nearly to the rim, and one or
					// two lesser spires on its shoulders; ridged noise cuts gullies into the flanks.
					int extra = 1 + rng.Next(2);
					var spires = new Vector4[extra];
					float a0 = rng.NextFloat() * Mathf.PI * 2f;
					for (int i = 0; i < extra; i++)
					{
						float a = a0 + i * rng.Range(1.9f, 2.6f);
						spires[i] = new Vector4(Mathf.Cos(a) * 0.5f, Mathf.Sin(a) * 0.45f, H * rng.Range(0.5f, 0.66f), rng.Range(0.38f, 0.48f));
					}
					c.Top = (x, z) =>
					{
						float X = x / hl, Z = z / hw;
						float r = Mathf.Sqrt(X * X + Z * Z);
						float y = rim * (1f - 0.3f * Mathf.Clamp01(r));
						y = SoftMax(y, H * Mathf.Pow(Mathf.Max(0f, 1f - r / 1.02f), 1.6f), 0.03f * H);
						foreach (Vector4 sp in spires)
						{
							float d = Mathf.Sqrt((X - sp.x) * (X - sp.x) + (Z - sp.y) * (Z - sp.y));
							y = SoftMax(y, sp.z * Mathf.Pow(Mathf.Max(0f, 1f - d / sp.w), 1.5f), 0.03f * H);
						}
						float gullies = ProceduralNoise.Ridged3(new Vector3(X * 2.2f + 3.1f, Z * 2.2f, 1.9f), 2, 0.5f, seed + 5) - 0.5f;
						return y + H * (0.06f * gullies * Mathf.Clamp01(r * 3f) * Mathf.Clamp01(1.1f - r) + 0.012f * Noise2(X, Z, 2.5f, 2, seed));
					};
					break;
				}
				case IcebergClass.Wedge:
				{
					c.Exponent = 3.2f;
					c.Facets = PlanFacets(rng, 4, 0.92f, 0.98f);
					c.Ram = 0.12f;
					c.KeelStart = 0.55f;
					c.BottomExponent = 3f;
					float low = 0.14f * H;
					// High, sheer end at +x; the top falls away as a tilted plane to the low end.
					c.Top = (x, z) =>
					{
						float t = Mathf.Clamp01((x / hl) * 0.5f + 0.5f);
						return low + (H - low) * Mathf.Pow(t, 0.9f) * (0.97f + 0.03f * Noise2(x / hl, z / hw, 1.8f, 2, seed));
					};
					break;
				}
				case IcebergClass.Drydock:
				{
					c.Exponent = 3f;
					c.Facets = PlanFacets(rng, 3, 0.92f, 0.98f);
					c.Ram = 0.15f;
					c.KeelStart = 0.45f;
					c.BottomExponent = 3f;
					c.Taper = 0.12f;
					float floor = Mathf.Max(0.13f * H, c.NotchAbove + c.MinWall + c.Bevel + 0.02f * H);
					float second = rng.Range(0.82f, 0.94f);
					bool flip = rng.NextFloat() < 0.5f;
					// Twin columns at the two ends, a U-shaped slot between them running right across.
					c.Top = (x, z) =>
					{
						float X = x / hl;
						float ax = Mathf.Abs(X);
						float tower = Smoothstep(0.2f, 0.44f, ax);
						float crown = 1f - 0.18f * ((ax - 0.72f) / 0.28f) * ((ax - 0.72f) / 0.28f);
						float peak = (X > 0f) ^ flip ? H : H * second;
						float u = floor + 0.06f * H * (ax / 0.2f) * (ax / 0.2f);
						return Mathf.Lerp(u, peak * crown, tower) + 0.02f * H * Noise2(X, z / hw, 2f, 2, seed) * tower;
					};
					break;
				}
			}
			return c;
		}

		private static Vector3[] PlanFacets(DeterministicRNG rng, int count, float near, float far)
		{
			var facets = new Vector3[count];
			float a0 = rng.NextFloat() * Mathf.PI * 2f;
			for (int i = 0; i < count; i++)
			{
				float a = a0 + i * Mathf.PI * 2f / count + rng.Range(-0.4f, 0.4f);
				facets[i] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), rng.Range(near, far));
			}
			return facets;
		}

		/// <summary>
		/// An iceberg of an IIP class and size, its pivot on the waterline and its keel deep enough to
		/// float it: the water it displaces weighs what its firn-capped body does.
		/// </summary>
		/// <remarks>
		/// The breadth and the draught are solved once, at the finest level, and every level is built
		/// with them, so all levels share one silhouette above water and below; a coarse level's own
		/// volume floats it a few percent off, which nobody sees at the distance it is drawn. Submesh 0
		/// is the weathered ice above the wave-washed band, submesh 1 the band and the body under water
		/// (<see cref="WashedSubmesh"/>).
		/// </remarks>
		public static FloatingIce BuildIceberg(in IcebergShape shape, int resolution, int seed)
		{
			int s = ProceduralNoise.SeedFor("Iceberg/" + shape.Name, seed);
			if (shape.Class == IcebergClass.Irregular)
			{
				return BuildFloatingLump(in shape, resolution, s, GlacialIceDensity / SeaWaterDensity);
			}
			float top = shape.Height;
			Func<float, float> density = y => FirnDensity(top - y);
			Column c = BergColumn(in shape, s);
			Vector2 solved = Solved("Iceberg/" + shape.Name + "/" + shape.Length + "/" + shape.Width + "/" + shape.Height, s, () =>
			{
				SolveBreadth(c, density);
				SolveDraught(c, Resolution[0], density);
				return new Vector2(c.Ram, c.Draught);
			});
			c.Ram = solved.x;
			c.Draught = solved.y;
			MeshBuilder mesh = BuildColumn(c, resolution, BergTextureMetres, 2);
			Finish(mesh);
			return Floating(mesh, density);
		}

		private static readonly Dictionary<string, Vector2> solvedCache = new Dictionary<string, Vector2>();

		/// <summary>
		/// The finest level's solve for a shape and seed, worked out once: it is deterministic, every
		/// level reuses it, and a solve builds the shape some hundred times.
		/// </summary>
		private static Vector2 Solved(string key, int seed, Func<Vector2> solve)
		{
			string k = key + "#" + seed;
			lock (solvedCache)
			{
				if (solvedCache.TryGetValue(k, out Vector2 v))
				{
					return v;
				}
			}
			Vector2 result = solve();
			lock (solvedCache)
			{
				solvedCache[k] = result;
			}
			return result;
		}

		/// <summary>A growler or bergy bit: a scalloped, melt-rounded lump floated by shifting it.</summary>
		private static FloatingIce BuildFloatingLump(in IcebergShape shape, int resolution, int s, float share)
		{
			// The stable way up has the shortest axis vertical; the height above water is then about
			// 0.42 of the vertical semi-axis for a near-ellipsoid at this density, which sets it.
			// The waterline cuts the body above its widest point, through a section about four fifths of
			// its full breadth, so the body is built a quarter wider than the length it shows.
			float semiY = shape.Height / 0.42f;
			float semiX = shape.Length * 0.5f * 1.25f, semiZ = shape.Width * 0.5f * 1.25f;
			var lump = new Lump
			{
				Semi = new Vector3(semiX, Mathf.Min(semiY, semiZ * 0.8f), semiZ),
				Lumpiness = 0.6f,
				ScallopCells = 2.2f,
				ScallopDepth = 0.05f,
				Planes = FracturePlanes(3, 0.3f, s),
				PlaneSoftness = 0.2f,
				Seed = s,
			};
			float notchHeight = Mathf.Clamp(0.12f * shape.Height, 0.05f, 0.5f);
			float notchDepth = Mathf.Clamp(0.03f * shape.Length, 0.03f, 0.4f);
			// The waterline is found at the finest level and every level is cut and floated there.
			Vector2 solved = Solved("Lump/" + shape.Name + "/" + shape.Length + "/" + shape.Height, s, () =>
			{
				MeshBuilder fine = BuildLump(lump, Resolution[0], TextureMetres, 1, float.NegativeInfinity);
				float first = SolveWaterline(fine, share);
				Notch(fine, first, notchHeight, notchDepth);
				return new Vector2(first, SolveWaterline(fine, share));
			});
			MeshBuilder mesh = BuildLump(lump, resolution, TextureMetres, 1, float.NegativeInfinity);
			Notch(mesh, solved.x, notchHeight, notchDepth);
			for (int i = 0; i < mesh.VertexCount; i++)
			{
				mesh.Positions[i] -= new Vector3(0f, solved.y, 0f);
			}
			// The wave-washed band and below: the second submesh, as on the bigger bergs.
			var washed = new MeshBuilder(2);
			washed.Positions.AddRange(mesh.Positions);
			washed.Normals.AddRange(mesh.Normals);
			washed.Tangents.AddRange(mesh.Tangents);
			washed.UVs.AddRange(mesh.UVs);
			washed.Colors.AddRange(mesh.Colors);
			washed.Wind.AddRange(mesh.Wind);
			List<int> tris = mesh.Submeshes[0];
			for (int t = 0; t < tris.Count; t += 3)
			{
				float cy = (mesh.Positions[tris[t]].y + mesh.Positions[tris[t + 1]].y + mesh.Positions[tris[t + 2]].y) / 3f;
				List<int> into = washed.Submeshes[cy < notchHeight ? WashedSubmesh : 0];
				into.Add(tris[t]);
				into.Add(tris[t + 1]);
				into.Add(tris[t + 2]);
			}
			Finish(washed);
			return Floating(washed, y => GlacialIceDensity);
		}

		/// <summary>
		/// Cuts the wave notch into a lump floating at <paramref name="waterline"/> (in its own frame):
		/// a rounded groove pulled in toward the vertical axis, peaking a fifth of its height above the water.
		/// </summary>
		private static void Notch(MeshBuilder mesh, float waterline, float notchHeight, float notchDepth)
		{
			for (int i = 0; i < mesh.VertexCount; i++)
			{
				Vector3 p = mesh.Positions[i];
				float y = p.y - waterline;
				float t = (y - notchHeight * 0.2f) / (y > notchHeight * 0.2f ? notchHeight * 0.8f : notchHeight * 0.9f);
				if (Mathf.Abs(t) < 1f)
				{
					float k = (1f - t * t) * (1f - t * t);
					float m = Mathf.Sqrt(p.x * p.x + p.z * p.z);
					if (m > 1e-4f)
					{
						float scale = Mathf.Max(0.3f, (m - notchDepth * k) / m);
						p.x *= scale;
						p.z *= scale;
						mesh.Positions[i] = p;
					}
				}
			}
		}

		/// <summary>The height of the plane that cuts a closed mesh so <paramref name="share"/> of its volume is below.</summary>
		private static float SolveWaterline(MeshBuilder mesh, float share)
		{
			Bounds b = mesh.Bounds;
			float lo = b.min.y, hi = b.max.y;
			for (int i = 0; i < 50; i++)
			{
				float mid = 0.5f * (lo + hi);
				IceHydrostatics h = Measure(mesh.Positions, mesh.Submeshes, mid);
				if (h.SubmergedFraction < share)
				{
					lo = mid;
				}
				else
				{
					hi = mid;
				}
			}
			return 0.5f * (lo + hi);
		}

		private static FloatingIce Floating(MeshBuilder mesh, Func<float, float> density)
		{
			IceHydrostatics h = Measure(mesh);
			IceMass mass = MassOf(mesh.Positions, mesh.Submeshes, density);
			Bounds b = mesh.Bounds;
			float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
			float uMinX = float.MaxValue, uMaxX = float.MinValue, uMinZ = float.MaxValue, uMaxZ = float.MinValue;
			foreach (Vector3 p in mesh.Positions)
			{
				if (p.y >= 0f)
				{
					minX = Mathf.Min(minX, p.x);
					maxX = Mathf.Max(maxX, p.x);
					minZ = Mathf.Min(minZ, p.z);
					maxZ = Mathf.Max(maxZ, p.z);
				}
				else
				{
					uMinX = Mathf.Min(uMinX, p.x);
					uMaxX = Mathf.Max(uMaxX, p.x);
					uMinZ = Mathf.Min(uMinZ, p.z);
					uMaxZ = Mathf.Max(uMaxZ, p.z);
				}
			}
			return new FloatingIce
			{
				Mesh = mesh,
				Waterline = 0f,
				Freeboard = b.max.y,
				Draught = -b.min.y,
				Length = maxX - minX,
				Width = maxZ - minZ,
				Breadth = Mathf.Max((uMaxX - uMinX) / Mathf.Max(1e-4f, maxX - minX), (uMaxZ - uMinZ) / Mathf.Max(1e-4f, maxZ - minZ)),
				Density = h.Volume > 0f ? mass.Mass / h.Volume : 0f,
				Mass = mass.Mass,
				CentreOfMass = mass.CentreOfMass,
				Hydrostatics = h,
				Body = ToBody(in h, mass.Mass, mass.CentreOfMass),
			};
		}

		/// <summary>What <see cref="WaterFloater"/> needs, from the measured hydrostatics at one uniform density.</summary>
		public static FloatingBody ToBody(in IceHydrostatics h, float density) => ToBody(in h, density * h.Volume, h.Centroid);

		/// <summary>What <see cref="WaterFloater"/> needs, from the measured hydrostatics and the integrated mass.</summary>
		/// <remarks>The radii of gyration are the uniform body's: a firn cap shifts them by a few percent.</remarks>
		public static FloatingBody ToBody(in IceHydrostatics h, float mass, Vector3 centreOfMass)
		{
			float area = Mathf.Max(1e-6f, h.WaterplaneArea);
			return new FloatingBody
			{
				Mass = mass,
				DisplacedVolume = h.VolumeBelow,
				WaterplaneArea = h.WaterplaneArea,
				WaterplaneInertiaRoll = h.WaterplaneInertiaRoll,
				WaterplaneInertiaPitch = h.WaterplaneInertiaPitch,
				BuoyancyHeight = h.CentreOfBuoyancy.y,
				GravityHeight = centreOfMass.y,
				GyrationRoll = h.Gyration.x,
				GyrationPitch = h.Gyration.z,
				// The rectangle with the same area and second moments: where the footprint is sampled.
				HalfLength = Mathf.Sqrt(3f * h.WaterplaneInertiaPitch / area),
				HalfWidth = Mathf.Sqrt(3f * h.WaterplaneInertiaRoll / area),
			};
		}

		// ── Sea ice ───────────────────────────────────────────────────

		/// <summary>
		/// A pancake or a floe of sea ice, floated at sea ice's density with its pivot on the waterline.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Pancakes</b> form where frazil crystals in a swell clump into discs, and the discs, forever
		/// bumping, pile slush onto their edges: round, 0.3–3 m across, up to about 10 cm thick, with a
		/// raised rim and a dished middle. Their sides are rounded, as is their underside.
		/// </para>
		/// <para>
		/// <b>Floes</b> are level ice broken along straight cracks, so their plan is angular, their top
		/// flat, and their edges heaped with the rubble of collisions with their neighbours: a low
		/// ridge round the rim, broken into blocks rather than smooth.
		/// </para>
		/// </remarks>
		public static FloatingIce BuildSeaIce(in SeaIceShape shape, int resolution, int seed)
		{
			int s = ProceduralNoise.SeedFor("SeaIce/" + shape.Name, seed);
			var rng = new DeterministicRNG(s ^ 0x6a09e667);
			float share = SeaIceDensity / SeaWaterDensity;
			float hl = shape.Length * 0.5f, hw = shape.Width * 0.5f;
			float level = shape.Thickness * (1f - share);
			float rimH = shape.RimHeight;
			int seedTop = s;
			var c = new Column
			{
				HalfLength = hl,
				HalfWidth = hw,
				Seed = s,
				Freeboard = level + rimH,
				NotchDepth = 0f,
				NotchAbove = level * 0.5f,
				NotchBelow = shape.Thickness * 0.15f,
				NotchPeak = level * 0.1f,
				MinWall = level * 0.1f,
			};
			if (shape.Kind == SeaIceKind.Pancake)
			{
				c.Exponent = 2f;
				c.Irregularity = 0.5f;
				c.KeelStart = 0.3f;
				c.BottomExponent = 3f;
				c.Bevel = Mathf.Min(rimH * 0.6f, 0.05f);
				c.Top = (x, z) =>
				{
					float r = Mathf.Sqrt((x / hl) * (x / hl) + (z / hw) * (z / hw));
					float lip = Smoothstep(0.78f, 0.96f, r) * (0.75f + 0.25f * Noise2(x / hl, z / hw, 3f, 2, seedTop));
					return level + rimH * lip + 0.1f * rimH * Noise2(x / hl, z / hw, 4f, 2, seedTop + 7);
				};
			}
			else
			{
				c.Exponent = 2.6f;
				c.Irregularity = 1f;
				c.Facets = PlanFacets(rng, 7, 0.82f, 0.95f);
				c.FacetsThrough = true;
				c.KeelStart = 0.6f;
				c.BottomExponent = 6f;
				c.Bevel = Mathf.Min(rimH * 0.4f, 0.15f);
				c.Top = (x, z) =>
				{
					float X = x / hl, Z = z / hw;
					float r = Mathf.Sqrt(X * X + Z * Z);
					// Rafted rubble round the rim: a band, broken into blocks by ridged noise along it.
					float band = Smoothstep(0.74f, 0.9f, r) * (1f - 0.35f * Smoothstep(0.94f, 1.05f, r));
					float blocks = ProceduralNoise.Ridged3(new Vector3(X * 3.1f + 1.3f, Z * 3.1f, 0.7f), 2, 0.5f, seedTop);
					return level + rimH * band * (0.35f + 0.65f * blocks) + 0.04f * rimH * Noise2(X, Z, 3f, 2, seedTop + 3);
				};
			}
			Func<float, float> density = y => SeaIceDensity;
			float thickness = shape.Thickness;
			void Level(float l)
			{
				level = l;
				c.Freeboard = l + rimH;
				c.NotchAbove = l * 0.5f;
				c.NotchPeak = l * 0.1f;
				c.MinWall = l * 0.1f;
			}
			// The rim and the rubble are ice above the water too, and need more under it: lower the
			// level surface until the level ice is as thick as asked (not below a quarter of its
			// freeboard). Solved at the finest level; every level shares the result.
			Vector2 solved = Solved("SeaIce/" + shape.Name + "/" + shape.Length + "/" + shape.Thickness + "/" + rimH, s, () =>
			{
				for (int pass = 0; pass < 4; pass++)
				{
					Level(level);
					SolveDraught(c, Resolution[0], density);
					float error = level + c.Draught - thickness;
					level = Mathf.Max(0.25f * thickness * (1f - share), level - error * (1f - share));
				}
				Level(level);
				SolveDraught(c, Resolution[0], density);
				return new Vector2(level, c.Draught);
			});
			Level(solved.x);
			c.Draught = solved.y;
			MeshBuilder mesh = BuildColumn(c, resolution, TextureMetres, 1);
			Finish(mesh);
			return Floating(mesh, density);
		}

		/// <summary>
		/// A pressure ridge segment: a strip of level ice with a sail of tilted rubble blocks on it and
		/// a keel of them under it, its pivot on the waterline.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Ridges form where floes are driven together: the level ice breaks into blocks about as
		/// thick as itself and the blocks pile up above (the sail, at its angle of repose, about 25°)
		/// and are pushed down below (the keel, about four and a half times deeper than the sail is
		/// high, with steeper flanks). Every block is its own closed box, flat-shaded, so the mesh is a
		/// set of closed shells; boxes interpenetrate as rubble does, so its volume is not split.
		/// </para>
		/// <para>
		/// <b>Levels.</b> Every level has the slab and a rubble core (a prism for the sail and one for
		/// the keel, 28 triangles with the slab); blocks are kept largest first on top of it: 97 at the
		/// finest level (1192 triangles, the boulders' budget), 22 at the middle (292), none at the
		/// coarsest.
		/// </para>
		/// </remarks>
		public static MeshBuilder BuildPressureRidge(in PressureRidgeShape shape, int lod, int seed)
		{
			int s = ProceduralNoise.SeedFor("Ridge/" + shape.Name, seed);
			var rng = new DeterministicRNG(s);
			float t = shape.BlockThickness;
			float share = SeaIceDensity / SeaWaterDensity;
			float sail = shape.SailHeight, keel = shape.SailHeight * shape.KeelRatio;
			float sailHalf = sail / Mathf.Tan(25f * Mathf.Deg2Rad);
			float keelHalf = keel / Mathf.Tan(33f * Mathf.Deg2Rad);
			float freeboard = t * (1f - share);
			float hl = shape.Length * 0.5f;

			// Blocks heaped on the surface of the sail and the keel, not scattered through them: what one
			// sees of a ridge is its outer rubble. Most go to the sail; the keel is mostly seen dimly.
			var blocks = new List<(float size, Matrix4x4 m, Vector3 extent)>();
			int sailCount = Mathf.Max(1, shape.Blocks * 3 / 4);
			for (int i = 0; i < shape.Blocks; i++)
			{
				bool up = i < sailCount;
				float x = rng.Range(-hl * 0.97f, hl * 0.97f);
				float zf = rng.Range(-1f, 1f);
				float z = zf * (up ? sailHalf : keelHalf) * 0.95f;
				float pile = (up ? sail : keel) * (1f - Mathf.Abs(zf));
				// Near the pile's surface: between 55 % and all of its height there.
				float h = pile * rng.Range(0.55f, 1f);
				float y = up ? freeboard + h : freeboard - t - h;
				float len = rng.Range(1f, 2.6f) * Mathf.Max(0.5f, t * 2.2f);
				float wid = rng.Range(0.7f, 1.5f) * Mathf.Max(0.4f, t * 1.8f);
				var extent = new Vector3(len, t * rng.Range(0.8f, 1.1f), wid);
				Quaternion rot = Quaternion.Euler(rng.Range(-50f, 50f), rng.Range(0f, 360f), rng.Range(-45f, 45f));
				blocks.Add((len * wid * extent.y, Matrix4x4.TRS(new Vector3(x, y, z), rot, Vector3.one), extent));
			}
			// Largest first, keel blocks after sail blocks of the same rank: the coarser levels keep what is seen.
			blocks.Sort((a, b) => b.size.CompareTo(a.size));

			var mesh = new MeshBuilder(1);
			// The level ice the ridge stands in, wider than its keel.
			AddBox(mesh, Matrix4x4.TRS(new Vector3(0f, freeboard - t * 0.5f, 0f), Quaternion.identity, Vector3.one),
				new Vector3(shape.Length, t, Mathf.Max(shape.Width, keelHalf * 2.2f)));
			// A rubble core under the blocks at every level, so no gap shows the sky through the sail.
			AddPrism(mesh, hl * 2f * 0.98f, sailHalf * 0.85f, sail * 0.8f, freeboard, true);
			AddPrism(mesh, hl * 2f * 0.98f, keelHalf * 0.85f, keel * 0.85f, freeboard - t, false);
			// The finest level fills the boulders' 1200 triangles: 28 for the slab and cores, 12 a block.
			int keep = lod <= 0 ? blocks.Count : lod == 1 ? Mathf.Min(blocks.Count, 22) : 0;
			for (int i = 0; i < keep; i++)
			{
				AddBox(mesh, blocks[i].m, blocks[i].extent);
			}
			mesh.RecalculateNormals(false);
			mesh.RecalculateTangents();
			return mesh;
		}

		private static readonly Vector3[] BoxNormal = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };

		private static void AddBox(MeshBuilder mesh, Matrix4x4 m, Vector3 size)
		{
			Vector3 e = size * 0.5f;
			var white = new Color32(255, 255, 255, 0);
			for (int f = 0; f < 6; f++)
			{
				Vector3 n = BoxNormal[f];
				Vector3 u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
				Vector3 v = Vector3.Cross(n, u);
				int first = mesh.VertexCount;
				for (int k = 0; k < 4; k++)
				{
					float su = (k == 1 || k == 2) ? 1f : -1f;
					float sv = k >= 2 ? 1f : -1f;
					Vector3 local = Vector3.Scale(n + u * su + v * sv, e);
					Vector3 uvPoint = Vector3.Scale(u * su + v * sv, e);
					var uv = new Vector2(Vector3.Dot(uvPoint, u), Vector3.Dot(uvPoint, v)) / TextureMetres;
					mesh.AddVertex(m.MultiplyPoint3x4(local), Vector3.zero, uv, white);
				}
				Vector3 outward = m.MultiplyVector(n);
				mesh.AddQuad(0, first, first + 1, first + 2, first + 3, outward);
			}
		}

		/// <summary>A triangular prism along x: the sail (apex up) or the keel (apex down) at low detail.</summary>
		private static void AddPrism(MeshBuilder mesh, float length, float half, float height, float baseY, bool up)
		{
			float sign = up ? 1f : -1f;
			float hx = length * 0.5f;
			Vector3 a = new Vector3(0f, baseY + sign * height, 0f), b = new Vector3(0f, baseY, -half), c = new Vector3(0f, baseY, half);
			var white = new Color32(255, 255, 255, 0);
			Vector3 dx = new Vector3(hx, 0f, 0f);
			Vector3 centre = new Vector3(0f, baseY + sign * height / 3f, 0f);
			void Face(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
			{
				int first = mesh.VertexCount;
				Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
				Vector3 mid = (p0 + p1 + p2 + p3) * 0.25f;
				Vector3 outward = mid - centre;
				Vector3 u = (p1 - p0).normalized;
				Vector3 v = Vector3.Cross(n.normalized, u);
				foreach (Vector3 p in new[] { p0, p1, p2, p3 })
				{
					mesh.AddVertex(p, Vector3.zero, new Vector2(Vector3.Dot(p, u), Vector3.Dot(p, v)) / TextureMetres, white);
				}
				mesh.AddQuad(0, first, first + 1, first + 2, first + 3, outward);
			}
			void Tri(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 outward)
			{
				int first = mesh.VertexCount;
				foreach (Vector3 p in new[] { p0, p1, p2 })
				{
					mesh.AddVertex(p, Vector3.zero, new Vector2(p.z, p.y) / TextureMetres, white);
				}
				mesh.AddTriangle(0, first, first + 1, first + 2, outward);
			}
			Face(a - dx, a + dx, b + dx, b - dx);
			Face(a - dx, a + dx, c + dx, c - dx);
			Face(b - dx, b + dx, c + dx, c - dx);
			Tri(a - dx, b - dx, c - dx, Vector3.left);
			Tri(a + dx, b + dx, c + dx, Vector3.right);
		}

		// ── Hydrostatics ──────────────────────────────────────────────

		/// <summary>The volume of a closed mesh split at its waterline y = 0, and the moments that float it.</summary>
		public static IceHydrostatics Measure(MeshBuilder mesh) => Measure(mesh.Positions, mesh.Submeshes, 0f);

		/// <summary>
		/// Exact integrals over a closed, outward-wound polyhedron, split by the plane y = <paramref name="level"/>.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Each triangle, clipped to the side of the plane it is on, makes a tetrahedron with a point
		/// on the plane; summed, the tetrahedra are the volume on that side. The cap that would close
		/// each side along the waterline never has to be built: every tetrahedron on it is flat, with
		/// no volume and no moment. Volume, centroid and second moments are the standard tetrahedron
		/// integrals, in double precision.
		/// </para>
		/// <para>
		/// The waterplane comes from the same clipped triangles: the closed part under water has
		/// ∮ n_y dA = 0, so the cap's area is minus the sum of the clipped triangles' projected areas,
		/// and its first and second moments the same with the integrand carried along — the divergence
		/// theorem on (0, f(x, z), 0), whose divergence is zero.
		/// </para>
		/// </remarks>
		public static IceHydrostatics Measure(List<Vector3> positions, List<List<int>> submeshes, float level)
		{
			var below = new Moments();
			var above = new Moments();
			double area = 0, ax = 0, az = 0, axx = 0, azz = 0;
			var bufBelow = new List<Vector3>(4);
			var bufAbove = new List<Vector3>(4);
			foreach (List<int> list in submeshes)
			{
				for (int t = 0; t + 2 < list.Count; t += 3)
				{
					Vector3 a = positions[list[t]], b = positions[list[t + 1]], c = positions[list[t + 2]];
					a.y -= level;
					b.y -= level;
					c.y -= level;
					Clip(a, b, c, bufBelow, bufAbove);
					for (int k = 1; k + 1 < bufBelow.Count; k++)
					{
						Vector3 p = bufBelow[0], q = bufBelow[k], r = bufBelow[k + 1];
						below.Add(p, q, r);
						// Projected (signed) area and its moments: the waterplane, by subtraction.
						double cy = 0.5 * ((double)(q.z - p.z) * (r.x - p.x) - (double)(q.x - p.x) * (r.z - p.z));
						area -= cy;
						ax -= cy * (p.x + q.x + r.x) / 3.0;
						az -= cy * (p.z + q.z + r.z) / 3.0;
						axx -= cy / 6.0 * ((double)p.x * p.x + (double)q.x * q.x + (double)r.x * r.x + (double)p.x * q.x + (double)q.x * r.x + (double)r.x * p.x);
						azz -= cy / 6.0 * ((double)p.z * p.z + (double)q.z * q.z + (double)r.z * r.z + (double)p.z * q.z + (double)q.z * r.z + (double)r.z * p.z);
					}
					for (int k = 1; k + 1 < bufAbove.Count; k++)
					{
						above.Add(bufAbove[0], bufAbove[k], bufAbove[k + 1]);
					}
				}
			}
			var whole = new Moments();
			whole.Merge(below);
			whole.Merge(above);
			var h = new IceHydrostatics
			{
				Volume = (float)whole.V,
				VolumeBelow = (float)below.V,
				VolumeAbove = (float)above.V,
				Centroid = whole.Centroid(level),
				CentreOfBuoyancy = below.Centroid(level),
				WaterplaneArea = (float)area,
			};
			if (area > 1e-12)
			{
				double cx = ax / area, cz = az / area;
				h.WaterplaneCentroid = new Vector2((float)cx, (float)cz);
				h.WaterplaneInertiaPitch = (float)(axx - area * cx * cx);
				h.WaterplaneInertiaRoll = (float)(azz - area * cz * cz);
			}
			if (whole.V > 1e-12)
			{
				double v = whole.V;
				double gx = whole.X / v, gy = whole.Y / v, gz = whole.Z / v;
				double xx = whole.XX / v - gx * gx, yy = whole.YY / v - gy * gy, zz = whole.ZZ / v - gz * gz;
				h.Gyration = new Vector3((float)Math.Sqrt(Math.Max(0, yy + zz)), (float)Math.Sqrt(Math.Max(0, xx + zz)), (float)Math.Sqrt(Math.Max(0, xx + yy)));
			}
			return h;
		}

		/// <summary>Splits a triangle by y = 0 into the polygon below and the polygon above (each 0, 3 or 4 points, in winding order).</summary>
		private static void Clip(Vector3 a, Vector3 b, Vector3 c, List<Vector3> below, List<Vector3> above)
		{
			below.Clear();
			above.Clear();
			Vector3 p0 = a, p1 = b, p2 = c;
			ClipEdge(p0, p1, below, above);
			ClipEdge(p1, p2, below, above);
			ClipEdge(p2, p0, below, above);
		}

		private static void ClipEdge(Vector3 p, Vector3 q, List<Vector3> below, List<Vector3> above)
		{
			// Sutherland–Hodgman for both halves at once: emit p on its side, then the crossing.
			if (p.y <= 0f)
			{
				below.Add(p);
			}
			if (p.y >= 0f)
			{
				above.Add(p);
			}
			if ((p.y < 0f && q.y > 0f) || (p.y > 0f && q.y < 0f))
			{
				float t = p.y / (p.y - q.y);
				Vector3 x = new Vector3(p.x + (q.x - p.x) * t, 0f, p.z + (q.z - p.z) * t);
				below.Add(x);
				above.Add(x);
			}
		}

		/// <summary>Volume, first and second moments of a set of tetrahedra with one vertex at the origin.</summary>
		private sealed class Moments
		{
			public double V, X, Y, Z, XX, YY, ZZ;

			public void Add(Vector3 a, Vector3 b, Vector3 c)
			{
				double ax = a.x, ay = a.y, az = a.z, bx = b.x, by = b.y, bz = b.z, cx = c.x, cy = c.y, cz = c.z;
				double v = (ax * (by * cz - bz * cy) - ay * (bx * cz - bz * cx) + az * (bx * cy - by * cx)) / 6.0;
				double sx = ax + bx + cx, sy = ay + by + cy, sz = az + bz + cz;
				V += v;
				X += v * sx / 4.0;
				Y += v * sy / 4.0;
				Z += v * sz / 4.0;
				XX += v / 20.0 * (ax * ax + bx * bx + cx * cx + sx * sx);
				YY += v / 20.0 * (ay * ay + by * by + cy * cy + sy * sy);
				ZZ += v / 20.0 * (az * az + bz * bz + cz * cz + sz * sz);
			}

			public void Merge(Moments o)
			{
				V += o.V; X += o.X; Y += o.Y; Z += o.Z; XX += o.XX; YY += o.YY; ZZ += o.ZZ;
			}

			public Vector3 Centroid(float level)
			{
				return V > 1e-12 ? new Vector3((float)(X / V), (float)(Y / V) + level, (float)(Z / V)) : new Vector3(0f, level, 0f);
			}
		}
	}
}
#endif
