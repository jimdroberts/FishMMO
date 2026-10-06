using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// A scene's rivers and lakes, baked when it was generated: each river's centre line with its
	/// water's surface, bed, width and discharge at every point, and each lake's level.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The one description of the water.</b> The generator carved the ground from it, painted the
	/// riverbeds and kept plants out of the water by it; repainting an existing scene reads it again;
	/// the water's own surfaces and flow are built from it. A river's surface never rises downstream,
	/// point to point.
	/// </para>
	/// <para>
	/// Scene metres throughout: x and z from the scene's centre, y above the body's datum.
	/// </para>
	/// </remarks>
	public class SceneHydrology : ScriptableObject
	{
		/// <summary>How a river begins or ends; the same values as the generator's drainage.</summary>
		public enum End : byte
		{
			Source = 0,
			Lake = 1,
			Sea = 2,
			Confluence = 3,
			Sink = 4,
			Edge = 5,
		}

		[Serializable]
		public sealed class River
		{
			public int Id;
			[Tooltip("The planet-wide river this run is part of; scenes cut across one river share it.")]
			public int PlanetRiver;
			[Tooltip("False for a dry wash: a bed and banks, no water.")]
			public bool Perennial = true;
			public End Start;
			public End Finish;
			[Tooltip("The lake it leaves or enters, or the river it joins; -1 for none.")]
			public int StartLake = -1;
			public int EndLake = -1;
			public int JoinsRiver = -1;
			[Tooltip("Centre line, upstream to downstream: x, water surface y, z.")]
			public Vector3[] Points = Array.Empty<Vector3>();
			[Tooltip("The deepest point of the bed under each point, scene metres.")]
			public float[] Bed = Array.Empty<float>();
			[Tooltip("Bank to bank at each point, metres.")]
			public float[] Width = Array.Empty<float>();
			[Tooltip("Water depth at the deepest point, metres.")]
			public float[] Depth = Array.Empty<float>();
			[Tooltip("Discharge at each point, m³/s.")]
			public float[] Discharge = Array.Empty<float>();
			[Tooltip("Mean speed of the water at each point, m/s.")]
			public float[] Speed = Array.Empty<float>();
			[Tooltip("What the water does at each point: 0 run, 1 riffle, 2 pool, 3 rapid, 4 fall.")]
			public byte[] Reach = Array.Empty<byte>();
		}

		[Serializable]
		public sealed class Lake
		{
			public int Id;
			[Tooltip("The planet-wide lake; scenes sharing it share its level.")]
			public int PlanetLake;
			[Tooltip("The water's surface, scene metres.")]
			public float Level;
			[Tooltip("The level a full lake would stand at, where it spills.")]
			public float SpillLevel;
			[Tooltip("True when evaporation takes all it receives: nothing flows out.")]
			public bool Terminal;
			[Tooltip("True when the climate keeps it frozen.")]
			public bool Frozen;
			[Tooltip("What flows out over its spill point, m³/s.")]
			public float Outflow;
			[Tooltip("Points under its water the lake is flooded from, x and z.")]
			public Vector2[] Seeds = Array.Empty<Vector2>();
			[Tooltip("The rectangle its water may cover, x/z min and size.")]
			public Rect Bounds;
			[Tooltip("Where its water stands: one bit per cell, row-major from MaskOrigin, MaskCell metres a cell.")]
			public byte[] Mask = Array.Empty<byte>();
			public Vector2 MaskOrigin;
			public float MaskCell = 4f;
			public int MaskWidth;
			public int MaskHeight;

			/// <summary>True when the lake's water stands over a scene position.</summary>
			public bool Covers(float x, float z)
			{
				if (Mask == null || Mask.Length == 0 || MaskCell <= 0f)
				{
					return false;
				}
				int cx = Mathf.FloorToInt((x - MaskOrigin.x) / MaskCell), cz = Mathf.FloorToInt((z - MaskOrigin.y) / MaskCell);
				if (cx < 0 || cz < 0 || cx >= MaskWidth || cz >= MaskHeight)
				{
					return false;
				}
				int bit = cz * MaskWidth + cx;
				return (bit >> 3) < Mask.Length && (Mask[bit >> 3] & (1 << (bit & 7))) != 0;
			}
		}

		public List<River> Rivers = new List<River>();
		public List<Lake> Lakes = new List<Lake>();
		[Tooltip("Boulders in and beside the rivers: x, y, z and radius. What the water's flow runs round.")]
		public List<Vector4> Boulders = new List<Vector4>();
		[Tooltip("The sea's surface, scene metres; negative infinity where there is none.")]
		public float SeaLevel = float.NegativeInfinity;
	}
}
