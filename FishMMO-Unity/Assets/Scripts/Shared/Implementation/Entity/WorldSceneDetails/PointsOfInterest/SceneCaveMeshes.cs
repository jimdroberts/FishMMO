using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// The main object of a scene's <c>&lt;Scene&gt; Caves.asset</c>: its cave systems' rock meshes are sub-assets of it
	/// (<c>Cave &lt;site id&gt; P&lt;n&gt;</c>, stored unreadable, each with its collider <c>Cave &lt;site id&gt; C&lt;n&gt;</c>: the same
	/// triangles on positions alone, since 2026-10-20), and it keeps, per cave, the key of the inputs those meshes were built from,
	/// so a repaint or re-cut that changes nothing about a cave keeps its meshes instead of building them again.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Binary.</b> The meshes are megabytes of vertex data; as text the file was tens of megabytes of hex and slow to
	/// write. The serialization mode of an asset file follows its main object, so this attribute stores the whole file,
	/// meshes included, as binary. Unity reads either form, so a scene cut before it still loads its caves.
	/// </para>
	/// <para>
	/// <b>Generation data only.</b> Scenes reference the meshes, never this object, so a build carries the meshes and
	/// leaves the keys out.
	/// </para>
	/// </remarks>
	[PreferBinarySerialization]
	public sealed class SceneCaveMeshes : ScriptableObject
	{
		/// <summary>One cave system's meshes, as last built.</summary>
		[Serializable]
		public struct Cave
		{
			/// <summary>The site whose system it is (the meshes' names carry it).</summary>
			public int SiteId;
			/// <summary>The key of everything the mesher read to build them (hex); empty when unknown, which never matches.</summary>
			public string Inputs;
			/// <summary>How many pieces (<c>P0</c> to <c>P&lt;n−1&gt;</c>).</summary>
			public int Pieces;
			/// <summary>Per piece: within reach of a mouth (casts shadows, always drawn).</summary>
			public bool[] Mouths;
			/// <summary>The triangles over all its pieces, for the notes.</summary>
			public int Triangles;
		}

		public List<Cave> Caves = new List<Cave>();

		/// <summary>The index of a site's entry in <see cref="Caves"/>, or −1.</summary>
		public int IndexOf(int siteId)
		{
			for (int i = 0; i < Caves.Count; i++)
			{
				if (Caves[i].SiteId == siteId)
				{
					return i;
				}
			}
			return -1;
		}
	}
}
