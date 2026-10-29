using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>What kind of way a path is: how wide, what it is made of, how steep it may climb and how hard it is kept.</summary>
	/// <remarks>Append only; the ordinal is stored in scene assets. Ordered by importance: a higher class wins a junction.</remarks>
	public enum ScenePathClass : byte
	{
		/// <summary>A faint hiking trail to a ruin, a cave, a lair: narrow, mostly grown over, climbing steeply.</summary>
		Trail,
		/// <summary>A footpath to a shrine, a graveyard, a waterfall: narrow, well walked.</summary>
		Footpath,
		/// <summary>A cart track between villages: two wheel ruts with grass down the middle.</summary>
		CartTrack,
		/// <summary>A road between towns: wide packed gravel, cobbled at a city's gates.</summary>
		Road,
		/// <summary>A highway between cities and the capital: wide, paved, kept gentle.</summary>
		Highway,
		/// <summary>A settlement's own street, laid by its layout (never routed).</summary>
		Street,
		/// <summary>An ancient road: old paving under centuries of growth.</summary>
		AncientRoad,
		/// <summary>
		/// Ground a site's buildings wear (never routed): bare under and round a building, a trodden apron round a well or a
		/// fire, a worn way out of a door. What keeps grass out of houses and makes a village look lived in.
		/// </summary>
		Yard,
	}

	/// <summary>What a stretch of path is surfaced with. The shader reads it as a code (<see cref="ScenePathStyle.SurfaceCode"/>).</summary>
	public enum ScenePathSurface : byte
	{
		/// <summary>Trodden earth.</summary>
		Earth,
		/// <summary>Earth worn into two wheel ruts, grass between them.</summary>
		Track,
		/// <summary>Packed gravel.</summary>
		Gravel,
		/// <summary>Cobbles and flagstones.</summary>
		Stone,
	}

	/// <summary>Per-point facts about a path (<see cref="ScenePath.Flags"/>).</summary>
	[Flags]
	public enum ScenePathPointFlags : byte
	{
		None = 0,
		/// <summary>On a bridge's deck: nothing is drawn or carved on the ground under it.</summary>
		Bridge = 1 << 0,
		/// <summary>Wading a ford: the bed is left as the river made it, the banks ramped down to it.</summary>
		Ford = 1 << 1,
		/// <summary>Inside a site's footprint (a settlement's gate, a ruin's court): the pad already shaped the ground.</summary>
		InSite = 1 << 2,
		/// <summary>
		/// A segment between two such points is drawn as a rectangle from point to point, its half width either side: a
		/// building's plan or a ploughed plot, whose corners a round-ended strip left as grass. The points are the
		/// rectangle's end midpoints, so never one point, which would give it no direction.
		/// </summary>
		Squared = 1 << 3,
		/// <summary>
		/// A segment between two such points is a building's floor: within it nothing drawn over it wears the ground less
		/// (<see cref="ScenePathField"/>), so no grass or detail comes up indoors whatever path or apron crosses it.
		/// </summary>
		Floor = 1 << 4,
		/// <summary>
		/// On a boardwalk (2026-10-18): a segment between two such points is walked on a plank deck on piles laid along it, so,
		/// as under a bridge's deck, nothing is drawn or carved on the ground beneath (Biomes.BiomeBoardwalks).
		/// </summary>
		Boardwalk = 1 << 5,
		/// <summary>
		/// On a rope ferry's crossing (decision 35 (i), 2026-10-18): a segment between two such points is travelled on the ferry
		/// flat, so, as under a bridge's deck, nothing is drawn or carved on the river's bed beneath.
		/// </summary>
		Ferry = 1 << 6,
		/// <summary>
		/// On a straight road's stair (2026-10-26, phase I2l-b): a segment between two such points climbs a cliff the road keeps its line
		/// over, cut as one even flight from the foot to the top (PathCarver) rather than evened to the road's grade.
		/// </summary>
		Stairs = 1 << 7,
	}

	/// <summary>
	/// How a way runs (<see cref="ScenePath.Form"/>, 2026-10-26, phase I2l-b; decisions 50, 56, 87): routed round the ground as every way
	/// was, or one of the straight roads, with the profile it is built to. Append only; the ordinal is stored in scene assets.
	/// </summary>
	public enum ScenePathForm : byte
	{
		/// <summary>Routed round the ground (every way until 2026-10-26).</summary>
		Routed = 0,
		/// <summary>Dead-straight, cut and filled to its grade, stairs cut where a cliff stands across it.</summary>
		Straight = 1,
		/// <summary>Straight, on an embankment raised over the ground beside it (a sacbe, a royal road).</summary>
		Raised = 2,
		/// <summary>Straight, sunk between sheer walls cut into the ground beside it (a via cava).</summary>
		Cut = 3,
	}

	/// <summary>
	/// One way through a scene: a polyline on the finished ground with its width, wear and surface along it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Points every couple of metres</b>, at the ground's height after the path was carved, so the polyline is the path:
	/// the client's path surface (<see cref="ScenePathField"/>) rasterises it, the atlas draws it and the scatter keeps
	/// trees off it with no other data.
	/// </para>
	/// <para>
	/// <b>Wear</b> is how kept the way is, 0 grown over … 1 bare: a spur fades the farther it runs from the network it
	/// left, so the last stretch to a ruin is all but lost in the grass.
	/// </para>
	/// </remarks>
	[Serializable]
	public class ScenePath
	{
		public int Id;
		public ScenePathClass Class;
		/// <summary>The site the path leaves from, or -1 (it joins another path).</summary>
		public int FromId = -1;
		/// <summary>The site the path leads to, or -1.</summary>
		public int ToId = -1;
		/// <summary>
		/// The bearing (degrees, in the site's own frame: 0 its heading) at which the path enters each end's site, or
		/// NaN where the end is not a site or the site takes a path from any side. A walled settlement opens its gates there.
		/// </summary>
		public float FromEntrance = float.NaN;
		public float ToEntrance = float.NaN;
		/// <summary>How the way runs: routed (every way before 2026-10-26) or one of the straight roads (<see cref="ScenePathForm"/>).</summary>
		public ScenePathForm Form = ScenePathForm.Routed;

		public Vector3[] Points = Array.Empty<Vector3>();
		/// <summary>Half the trodden width at each point, metres.</summary>
		public float[] HalfWidth = Array.Empty<float>();
		/// <summary>How kept the way is at each point, 0 … 1.</summary>
		public float[] Wear = Array.Empty<float>();
		/// <summary>The surface at each point (<see cref="ScenePathSurface"/>).</summary>
		public byte[] Surface = Array.Empty<byte>();
		/// <summary>Per-point flags (<see cref="ScenePathPointFlags"/>).</summary>
		public byte[] Flags = Array.Empty<byte>();

		public int Count => Points != null ? Points.Length : 0;

		public ScenePathPointFlags FlagsAt(int i) => Flags != null && i >= 0 && i < Flags.Length ? (ScenePathPointFlags)Flags[i] : ScenePathPointFlags.None;

		/// <summary>Length along the ground plane, metres.</summary>
		public float Length
		{
			get
			{
				float length = 0f;
				for (int i = 1; i < Count; i++)
				{
					length += Vector2.Distance(new Vector2(Points[i - 1].x, Points[i - 1].z), new Vector2(Points[i].x, Points[i].z));
				}
				return length;
			}
		}

		/// <summary>The widest half width along the path.</summary>
		public float MaxHalfWidth
		{
			get
			{
				float widest = 0f;
				if (HalfWidth != null)
				{
					foreach (float w in HalfWidth)
					{
						widest = Mathf.Max(widest, w);
					}
				}
				return widest;
			}
		}
	}

	/// <summary>
	/// A path class's numbers: its width, surface and kept-ness; how steep it may climb and how it meets the ground.
	/// </summary>
	public readonly struct ScenePathStyle
	{
		public readonly ScenePathClass Class;
		/// <summary>Half the trodden width, metres.</summary>
		public readonly float HalfWidth;
		public readonly ScenePathSurface Surface;
		/// <summary>How kept the way is where it leaves the network, 0 … 1.</summary>
		public readonly float Wear;
		/// <summary>How kept it is at its far end, 0 … 1 (a spur fades toward a lost place).</summary>
		public readonly float FarWear;
		/// <summary>The grade it is routed under (rise over run): steeper costs dearly, so it winds or switches back.</summary>
		public readonly float MaxGrade;
		/// <summary>Metres past the trodden edge over which the carve blends back into the ground.</summary>
		public readonly float Shoulder;
		/// <summary>How far below the ground beside it the way is worn, metres.</summary>
		public readonly float Dip;
		/// <summary>The most it may cut into or build above the ground, metres: a trail follows the ground, a highway levels it.</summary>
		public readonly float MaxCut;
		/// <summary>The length over which its profile is evened along the way, metres.</summary>
		public readonly float SmoothMetres;
		/// <summary>Metres past the trodden edge kept clear of trees and rocks.</summary>
		public readonly float Clearance;
		/// <summary>The deepest water it wades, metres; deeper takes a bridge.</summary>
		public readonly float FordDepth;
		/// <summary>True when the scatter grows no details on its trodden width (a road); a trail's grass is thinned by the shaders instead.</summary>
		public readonly bool ClearsDetails;

		public ScenePathStyle(ScenePathClass kind, float halfWidth, ScenePathSurface surface, float wear, float farWear, float maxGrade,
			float shoulder, float dip, float maxCut, float smoothMetres, float clearance, float fordDepth, bool clearsDetails)
		{
			Class = kind;
			HalfWidth = halfWidth;
			Surface = surface;
			Wear = wear;
			FarWear = farWear;
			MaxGrade = maxGrade;
			Shoulder = shoulder;
			Dip = dip;
			MaxCut = maxCut;
			SmoothMetres = smoothMetres;
			Clearance = clearance;
			FordDepth = fordDepth;
			ClearsDetails = clearsDetails;
		}

		/// <summary>
		/// The numbers for each class. Grades after real practice: a paved highway keeps under about 8 %, a carriage road
		/// 11 %, a cart 15 %; a footpath takes a quarter and a hill trail the steepest a walker climbs without hands.
		/// Widths: a single file trail 0.8 m, a footpath 1.3 m, a cart's gauge with its verges 3 m, a road two carts.
		/// </summary>
		public static ScenePathStyle For(ScenePathClass kind)
		{
			switch (kind)
			{
				case ScenePathClass.Trail:
					return new ScenePathStyle(kind, 0.4f, ScenePathSurface.Earth, 0.45f, 0.18f, 0.42f, 1.2f, 0.04f, 0.25f, 3f, 0.5f, 0.7f, false);
				case ScenePathClass.Footpath:
					return new ScenePathStyle(kind, 0.65f, ScenePathSurface.Earth, 0.8f, 0.55f, 0.27f, 1.6f, 0.06f, 0.4f, 5f, 0.9f, 0.6f, false);
				case ScenePathClass.CartTrack:
					return new ScenePathStyle(kind, 1.5f, ScenePathSurface.Track, 0.85f, 0.7f, 0.15f, 2.5f, 0.08f, 0.8f, 10f, 1.5f, 0.5f, false);
				case ScenePathClass.Road:
					return new ScenePathStyle(kind, 2.4f, ScenePathSurface.Gravel, 1f, 0.95f, 0.11f, 4f, 0f, 1.4f, 18f, 3f, 0.4f, true);
				case ScenePathClass.Highway:
					return new ScenePathStyle(kind, 3.2f, ScenePathSurface.Stone, 1f, 1f, 0.085f, 5f, 0f, 2f, 26f, 4f, 0f, true);
				case ScenePathClass.Street:
					return new ScenePathStyle(kind, 2.5f, ScenePathSurface.Stone, 1f, 1f, 1f, 0.5f, 0f, 0f, 0f, 1f, 0f, true);
				case ScenePathClass.AncientRoad:
					return new ScenePathStyle(kind, 2f, ScenePathSurface.Stone, 0.25f, 0.15f, 0.12f, 2f, 0f, 0.6f, 12f, 0.6f, 0.4f, false);
				case ScenePathClass.Yard:
					return new ScenePathStyle(kind, 1f, ScenePathSurface.Earth, 0.85f, 0.85f, 1f, 0.5f, 0f, 0f, 0f, 0.5f, 0f, false);
				default:
					return For(ScenePathClass.Trail);
			}
		}

		/// <summary>How high a raised straight road's embankment stands over the ground beside it, metres (a sacbe's 0.5–2.5 m).</summary>
		public const float RaisedMetres = 1.2f;

		/// <summary>How deep a cut straight road is sunk below the ground beside it, metres (a via cava's walls).</summary>
		public const float CutMetres = 5f;

		/// <summary>
		/// A way's numbers by its class and form (<see cref="ScenePath.Form"/>, 2026-10-26, phase I2l-b): a routed way's are its class's
		/// exactly (<see cref="For(ScenePathClass)"/>). A straight road evens its line over twice its class's length and cuts and fills
		/// up to 4 m to hold its grade; a raised one stands <see cref="RaisedMetres"/> over the ground between short steep sides; a cut
		/// one lies <see cref="CutMetres"/> down between near-sheer walls. The trodden width, surface and grade are the class's.
		/// </summary>
		public static ScenePathStyle For(ScenePathClass kind, ScenePathForm form)
		{
			ScenePathStyle c = For(kind);
			switch (form)
			{
				case ScenePathForm.Straight:
					return new ScenePathStyle(kind, c.HalfWidth, c.Surface, c.Wear, c.FarWear, c.MaxGrade, c.Shoulder, c.Dip, Mathf.Max(c.MaxCut, 4f),
						2f * Mathf.Max(c.SmoothMetres, 12f), c.Clearance, c.FordDepth, c.ClearsDetails);
				case ScenePathForm.Raised:
					return new ScenePathStyle(kind, c.HalfWidth, c.Surface, c.Wear, c.FarWear, c.MaxGrade, 2f * RaisedMetres, -RaisedMetres, Mathf.Max(c.MaxCut, 4f),
						2f * Mathf.Max(c.SmoothMetres, 12f), c.Clearance + 2f * RaisedMetres, c.FordDepth, c.ClearsDetails);
				case ScenePathForm.Cut:
					return new ScenePathStyle(kind, c.HalfWidth, c.Surface, c.Wear, c.FarWear, c.MaxGrade, 1.2f, CutMetres, Mathf.Max(c.MaxCut, 4f),
						2f * Mathf.Max(c.SmoothMetres, 12f), c.Clearance, c.FordDepth, c.ClearsDetails);
				default:
					return c;
			}
		}

		/// <summary>A way's own numbers: its class's in its form (<see cref="For(ScenePathClass, ScenePathForm)"/>).</summary>
		public static ScenePathStyle Of(ScenePath path) => path == null ? For(ScenePathClass.Trail) : For(path.Class, path.Form);

		/// <summary>
		/// The surface as the shader's code: track 0, earth ⅓, gravel ⅔, stone 1. Where two ways meet the field blends their
		/// codes, so the order is the order a blend passes through: the track (earth with ruts and a grass median) sits at
		/// the end, where the bake's first order (earth 0, track ⅓) put it between earth and gravel — a footpath running into
		/// a road grew a stretch of ruts and median grass at the seam (field encoding 2, <see cref="ScenePathField.Encoding"/>).
		/// </summary>
		public static float SurfaceCode(ScenePathSurface surface)
		{
			switch (surface)
			{
				case ScenePathSurface.Track:
					return 0f;
				case ScenePathSurface.Earth:
					return 1f / 3f;
				case ScenePathSurface.Gravel:
					return 2f / 3f;
				default:
					return 1f;
			}
		}
	}
}
