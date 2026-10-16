#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// What a scene's canyon walls were built from that only the cut knows: which heightmap cells are wall and the planet
	/// geology's rock under each, kept beside the scene's terrain (<c>&lt;Scene&gt; Canyon Wall Cells.asset</c>) so a repaint
	/// can dress the walls in the rock the current templates give them (<see cref="CanyonWalls.Redress"/>).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why it is kept.</b> The geology grid the walls read (SceneGeologyGrid) is built by the erosion, which only a full cut
	/// runs; a repaint has the finished terrain and no erosion. The walls' shape comes from the cut's ground and the beds'
	/// hardness and never changes on a repaint; the rock each cell wears comes from that cell's geology rock and the biome's
	/// rock rule there (BiomeTemplateArt.RockRule), the one part a template edit moves. So only the cells and their geology
	/// rock are kept: a few thousand runs, not the grid.
	/// </para>
	/// <para>
	/// Written by <see cref="CanyonWalls.Build"/> at the cut, in the same folder as the wall meshes; a re-cut clears the folder
	/// and writes it again. A scene cut before 2026-10-16 has none, and its walls keep the rock they were cut with.
	/// </para>
	/// </remarks>
	public sealed class SceneCanyonWallCells : ScriptableObject
	{
		/// <summary>The layout of <see cref="Runs"/>; a record of any other version is not read.</summary>
		public const int CurrentVersion = 1;

		public int Version = CurrentVersion;

		[Header("The tile plan the cells are of")]
		public int CountX;
		public int CountZ;
		public float TileMetres;
		public int Resolution;

		[Header("How the walls were chunked and subdivided")]
		public int ChunkCells;
		public int Subdivision;

		[Header("The cells")]
		[Tooltip("The geology's rocks under the wall cells (RockTypes names; empty where the geology named none).")]
		public List<string> Rocks = new List<string>();
		[Tooltip("The wall cells, row by row from the south-west, as (first cell, count, index into Rocks) triples.")]
		public int[] Runs = Array.Empty<int>();

		/// <summary>The plan the cells were cut on.</summary>
		public TerrainTilePlan Plan => new TerrainTilePlan { CountX = CountX, CountZ = CountZ, TileMetres = TileMetres, Resolution = Resolution };

		/// <summary>Whether the cells are of this plan's grid (the scene was not resized since).</summary>
		public bool Matches(TerrainTilePlan plan)
			=> plan.CountX == CountX && plan.CountZ == CountZ && plan.Resolution == Resolution && plan.TileMetres.Equals(TileMetres);
	}
}
#endif
