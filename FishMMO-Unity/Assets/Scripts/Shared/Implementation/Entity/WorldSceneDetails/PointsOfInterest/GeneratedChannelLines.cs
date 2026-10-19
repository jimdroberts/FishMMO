using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// The water channels the last placing of a scene's sites carried (2026-10-18, phase I2j-b, decision 38): each channel's line,
	/// its half width and the biome it was laid in, so the next repaint can paint that biome's wet margin along it (the channels are
	/// laid after the ground is painted, so a cut or repaint cannot paint along the channels it is about to lay).
	/// </summary>
	/// <remarks>
	/// Lives on an <c>EditorOnly</c>-tagged child beside the scene's "Field Carve" record, which Unity leaves out of every build: the
	/// margin it leads to is already in the terrain's paint. Written afresh every time the sites' linear features are laid; a scene
	/// with no channels keeps none.
	/// </remarks>
	[DisallowMultipleComponent]
	public sealed class GeneratedChannelLines : MonoBehaviour
	{
		/// <summary>One channel's carried stretch: its points on the plan (world xz), its half width, its biome's id.</summary>
		[Serializable]
		public sealed class Line
		{
			public int BiomeID;
			public float HalfWidth;
			public List<Vector2> Points = new List<Vector2>();
		}

		/// <summary>The name of the editor-only child that carries this.</summary>
		public const string ObjectName = "Channel Lines";

		/// <summary>The channels in the order they were laid.</summary>
		public List<Line> Lines = new List<Line>();
	}
}
