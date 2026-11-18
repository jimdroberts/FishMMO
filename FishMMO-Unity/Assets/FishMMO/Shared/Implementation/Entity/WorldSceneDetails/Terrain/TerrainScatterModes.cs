using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// The scatter modes a terrain's prototypes carry to the client (2026-10-26, phase SCATTER-MODES, decision 34), beside the terrain
	/// (written by the scene cut's scatter, TerrainScatter, only when some rule of the scene names a mode; a scene without one has no
	/// record at all, so nothing in it changes): a detail prototype's facing, lean and power-law size for the GPU detail scatter, and a tree
	/// prototype's lean for the tree renderer (a tree instance has no tilt of its own). Each is two words
	/// (<see cref="Biomes.BiomeScatterModeWord"/>).
	/// </summary>
	/// <remarks>
	/// Details are keyed by prefab name and noise seed, as <see cref="TerrainDetailBiomes"/> is (both survive a copy of the prototype, and
	/// the record pulls no prefab into a build); trees by prototype index (every tile of a scene carries the same tree prototypes, written
	/// with them).
	/// </remarks>
	[DisallowMultipleComponent]
	public sealed class TerrainScatterModes : MonoBehaviour
	{
		[Serializable]
		public struct Detail
		{
			public string Prefab;
			public int NoiseSeed;
			public uint Word;
			public uint Angles;
		}

		[Serializable]
		public struct Tree
		{
			public int Prototype;
			public uint Word;
			public uint Angles;
		}

		[Tooltip("Each detail prototype whose rule names a scatter mode: its prefab's name, noise seed and the two mode words. Written by the cut; not edited by hand.")]
		public List<Detail> Details = new List<Detail>();

		[Tooltip("Each tree prototype whose rule leans its instances: its index and the two mode words. Written by the cut; not edited by hand.")]
		public List<Tree> Trees = new List<Tree>();

		/// <summary>The mode words recorded for a detail prototype of this prefab and noise seed; false (and zeros) when none is.</summary>
		public bool TryDetail(string prefabName, int noiseSeed, out uint word, out uint angles)
		{
			word = 0u;
			angles = 0u;
			if (Details == null || string.IsNullOrEmpty(prefabName))
			{
				return false;
			}
			foreach (Detail d in Details)
			{
				if (d.NoiseSeed == noiseSeed && string.Equals(d.Prefab, prefabName, StringComparison.Ordinal))
				{
					word = d.Word;
					angles = d.Angles;
					return word != 0u;
				}
			}
			return false;
		}

		/// <summary>Each tree prototype's two words (index for index, zeros where none is recorded); null when the record holds no tree.</summary>
		public uint[] TreeWords(int prototypes, out uint[] angles)
		{
			angles = null;
			if (Trees == null || Trees.Count == 0 || prototypes <= 0)
			{
				return null;
			}
			var words = new uint[prototypes];
			angles = new uint[prototypes];
			bool any = false;
			foreach (Tree t in Trees)
			{
				if (t.Prototype >= 0 && t.Prototype < prototypes && t.Word != 0u)
				{
					words[t.Prototype] = t.Word;
					angles[t.Prototype] = t.Angles;
					any = true;
				}
			}
			if (!any)
			{
				angles = null;
				return null;
			}
			return words;
		}
	}
}
