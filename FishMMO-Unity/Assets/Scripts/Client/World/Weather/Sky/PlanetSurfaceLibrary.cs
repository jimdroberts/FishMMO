using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using FishMMO.Shared.Celestial;

namespace FishMMO.Client
{
	/// <summary>
	/// The baked surface of a world, for drawing it in the sky.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Looked up by address rather than read off the body, because a baked surface is build output:
	/// the folder is gitignored and a client build makes its own. A reference on
	/// <see cref="CelestialBody.SurfaceTexture"/> would put a committed asset's pointer on a file
	/// that the bake deletes again, and would drag every planet's art into a dedicated server build
	/// that never draws a frame.
	/// </para>
	/// <para>
	/// <b>Never blocks.</b> The first ask for a body starts the load and answers with whatever was
	/// authored by hand, or nothing; the sky simply draws that body untextured for a frame or two
	/// and picks the image up when it lands. A planet appearing plain for a moment is a far better
	/// failure than a hitch in the middle of a sunrise.
	/// </para>
	/// </remarks>
	public static class PlanetSurfaceLibrary
	{
		/// <summary>The address a baked surface is registered under. Matches the baker.</summary>
		public static string AddressOf(string bodyName) => $"PlanetSurfaces/{bodyName}";

		private static readonly Dictionary<string, Texture2D> loaded = new Dictionary<string, Texture2D>();
		private static readonly HashSet<string> asked = new HashSet<string>();

		/// <summary>
		/// The surface to draw a body with, or null.
		/// </summary>
		/// <remarks>
		/// A hand-made <see cref="CelestialBody.SurfaceTexture"/> wins: somebody assigned it on
		/// purpose, and a generated one should not quietly replace it.
		/// </remarks>
		public static Texture2D Get(CelestialBody body)
		{
			if (body == null)
			{
				return null;
			}
			if (body.SurfaceTexture != null)
			{
				return body.SurfaceTexture;
			}

			string name = body.name;
			if (string.IsNullOrEmpty(name))
			{
				return null;
			}
			if (loaded.TryGetValue(name, out Texture2D texture))
			{
				return texture;
			}
			if (asked.Add(name))
			{
				Begin(name);
			}
			return null;
		}

		private static void Begin(string bodyName)
		{
#if UNITY_EDITOR
			/* Straight off disk outside play mode. The addressable catalog is not built while
			 * somebody is working in the editor, so a bake made a minute ago would otherwise be
			 * invisible until the next content build — which is exactly when a designer wants to
			 * see it. */
			if (!Application.isPlaying)
			{
				loaded[bodyName] = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(
					$"Assets/Prefabs/Shared/PlanetSurfaces/{bodyName}.png");
				return;
			}
#endif
			AsyncOperationHandle<Texture2D> handle = Addressables.LoadAssetAsync<Texture2D>(AddressOf(bodyName));
			handle.Completed += operation =>
			{
				// A body with no bake is the normal case in a fresh project, so a failure here is
				// remembered as "nothing" rather than reported.
				loaded[bodyName] = operation.Status == AsyncOperationStatus.Succeeded ? operation.Result : null;
			};
		}

		/// <summary>Forgets everything, so the next ask loads again. For the editor after a bake.</summary>
		public static void Clear()
		{
			loaded.Clear();
			asked.Clear();
		}
	}
}
