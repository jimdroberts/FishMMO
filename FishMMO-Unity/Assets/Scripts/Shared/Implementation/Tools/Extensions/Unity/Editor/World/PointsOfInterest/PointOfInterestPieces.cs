#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Something that can answer "which prefab is a <c>hut</c> in the <c>timber</c> style?": the procedural structure
	/// kit, or a project's hand-made pieces.
	/// </summary>
	/// <remarks>
	/// Hand-made and LOCAL prefabs may replace any kit piece (Jim, 2026-10-10): register them as a source with a higher
	/// <see cref="Priority"/> than the kit's, and the kit is only asked for what they do not have.
	/// </remarks>
	public interface IStructurePieceSource
	{
		/// <summary>Higher is asked first.</summary>
		int Priority { get; }

		/// <summary>The prefab for a piece, or null when this source has none.</summary>
		/// <param name="tag">The piece slot's tag ("hut", "wall", "grave").</param>
		/// <param name="style">The site's style ("timber", "stone"; a race key when the template names none); may be empty.</param>
		/// <param name="seed">Picks among variants, deterministically.</param>
		GameObject Resolve(string tag, string style, int seed);
	}

	/// <summary>
	/// The registry of structure piece sources templates resolve their slots through. Nothing registered resolves
	/// nothing, and a feature skips a piece it cannot resolve: the site still stands as a marker.
	/// </summary>
	public static class PointOfInterestPieces
	{
		private sealed class FuncSource : IStructurePieceSource
		{
			private readonly Func<string, string, int, GameObject> resolve;
			public int Priority { get; }

			public FuncSource(Func<string, string, int, GameObject> resolve, int priority)
			{
				this.resolve = resolve;
				Priority = priority;
			}

			public GameObject Resolve(string tag, string style, int seed) => resolve(tag, style, seed);
		}

		private static readonly List<IStructurePieceSource> sources = new List<IStructurePieceSource>();

		public static IReadOnlyList<IStructurePieceSource> Sources => sources;

		/// <summary>Adds a source (once); sources are asked by priority, highest first, then in the order they were added.</summary>
		public static void Register(IStructurePieceSource source)
		{
			if (source == null || sources.Contains(source))
			{
				return;
			}
			int at = sources.Count;
			for (int i = 0; i < sources.Count; i++)
			{
				if (source.Priority > sources[i].Priority)
				{
					at = i;
					break;
				}
			}
			sources.Insert(at, source);
		}

		/// <summary>Adds a resolver function as a source; returns it, to unregister later.</summary>
		public static IStructurePieceSource Register(Func<string, string, int, GameObject> resolve, int priority = 0)
		{
			if (resolve == null)
			{
				return null;
			}
			var source = new FuncSource(resolve, priority);
			Register(source);
			return source;
		}

		public static void Unregister(IStructurePieceSource source) => sources.Remove(source);

		/// <summary>The first prefab any source has for (tag, style), or null.</summary>
		public static GameObject Resolve(string tag, string style, int seed)
		{
			if (string.IsNullOrEmpty(tag))
			{
				return null;
			}
			foreach (IStructurePieceSource source in sources)
			{
				GameObject prefab = source.Resolve(tag, style ?? string.Empty, seed);
				if (prefab != null)
				{
					return prefab;
				}
			}
			return null;
		}
	}
}
#endif
