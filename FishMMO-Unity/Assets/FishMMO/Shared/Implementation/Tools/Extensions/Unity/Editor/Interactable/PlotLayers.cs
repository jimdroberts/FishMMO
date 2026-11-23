using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// What turns a planned housing lot's post into a working plot: the seam the world generator lays housing through.
	/// </summary>
	/// <remarks>
	/// The world generator (WorldDesign) plans lots, levels them, routes a path spur to each and makes the post (its network
	/// object and collider). The plot itself (its component, its interaction, its look) belongs to the Housing module, which
	/// registers a layer here. With no layer registered the generator plans no lots at all: a world without housing gets
	/// no empty deed posts (second-wave moves; a core seam rather than one module referencing the other).
	/// </remarks>
	public static class PlotLayers
	{
		/// <summary>The registered layer, or null when no module provides housing.</summary>
		public static IPlotLayer Active { get; private set; }

		/// <summary>Registers <paramref name="layer"/> (the Housing module does, at editor load).</summary>
		public static void Register(IPlotLayer layer) => Active = layer;
	}

	/// <summary>Dresses one lot's post as a plot.</summary>
	public interface IPlotLayer
	{
		/// <summary>
		/// Adds the plot to <paramref name="post"/>: its key, price and floor size, a volume <paramref name="volumeHeight"/> tall
		/// whose origin is <paramref name="volumeOffset"/> from the post, its interaction and its visual. Materials come from
		/// <paramref name="material"/> (name, colour), shared across the scene's plots. Returns the components it dirtied.
		/// </summary>
		Component[] Lay(GameObject post, string plotKey, long price, Vector2 size, float volumeHeight, Vector3 volumeOffset,
			Func<string, Color, Material> material);
	}
}
