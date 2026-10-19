using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// The sculpted landforms a scene was cut with (2026-10-18, phase I2g): every site the cut placed and carved into its ground
	/// (BiomeLandformSite: its entry, place, size and line) and the pads their tops offer points of interest, so a repaint paints
	/// them again exactly (the heights it never touches) and the site planner can stand a terp village or a motte keep on them.
	/// </summary>
	/// <remarks>
	/// Lives on an <c>EditorOnly</c>-tagged root object, which Unity leaves out of every build: what it led to is already in the
	/// terrain's heights and paint. Written by the cut; a scene whose biomes name no landform keeps none.
	/// </remarks>
	[DisallowMultipleComponent]
	public sealed class GeneratedLandforms : MonoBehaviour
	{
		/// <summary>The name of the editor-only object that carries this.</summary>
		public const string ObjectName = "Landforms";

		/// <summary>The landforms in the order the cut carved them.</summary>
		public List<BiomeLandformSite> Sites = new List<BiomeLandformSite>();

		/// <summary>The sea's surface the cut measured their depths from, scene metres; negative infinity for none.</summary>
		public float SeaLevel = float.NegativeInfinity;

		/// <summary>The pads their tops offer.</summary>
		public List<BiomeLandformPad> Pads = new List<BiomeLandformPad>();
	}
}
