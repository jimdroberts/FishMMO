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
				default:
					return For(ScenePathClass.Trail);
			}
		}

		/// <summary>The surface as the shader's code: earth 0, track ⅓, gravel ⅔, stone 1.</summary>
		public static float SurfaceCode(ScenePathSurface surface) => (byte)surface / 3f;
	}
}
