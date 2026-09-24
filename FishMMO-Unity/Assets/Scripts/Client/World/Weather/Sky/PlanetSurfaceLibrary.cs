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
				/* Asked again straight away, because in the editor Begin is synchronous: the file
				 * is on disk and the asset database hands it over at once. Returning null here
				 * regardless meant the very first frame a body was large enough to texture always
				 * drew it plain, and anything that decided "this body is untextured" on that frame
				 * kept drawing it plain. */
				if (loaded.TryGetValue(name, out texture))
				{
					return texture;
				}
			}
			return null;
		}

		/// <summary>The file a bake writes, matching PlanetSurfaceBaker's own path exactly.</summary>
		/// <remarks>
		/// The baker sanitises the body's name for the file system; this has to sanitise it the
		/// same way or every body whose name holds an awkward character looks up a path that was
		/// never written.
		/// </remarks>
		private static string EditorPathOf(string bodyName)
		{
			foreach (char c in System.IO.Path.GetInvalidFileNameChars())
			{
				bodyName = bodyName.Replace(c, '_');
			}
			return $"Assets/Prefabs/Shared/PlanetSurfaces/{bodyName}.png";
		}

		private static void Begin(string bodyName)
		{
#if UNITY_EDITOR
			/* Straight off disk in the editor, playing or not.
			 *
			 * A baked surface is build output: the addressable catalog is only built for a real
			 * player build, so in the editor the entry may point at content that has never been
			 * packed. Going through Addressables there means a bake made a minute ago stays
			 * invisible — which is precisely when somebody is looking at it. On disk it is always
			 * current, and the load is synchronous, so the body is textured on the first frame it
			 * is big enough to be.
			 */
			loaded[bodyName] = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(EditorPathOf(bodyName));
			return;
#else
			AsyncOperationHandle<Texture2D> handle = Addressables.LoadAssetAsync<Texture2D>(AddressOf(bodyName));
			handle.Completed += operation =>
			{
				// A body with no bake is the normal case in a fresh project, so a failure here is
				// remembered as "nothing" rather than reported.
				loaded[bodyName] = operation.Status == AsyncOperationStatus.Succeeded ? operation.Result : null;
			};
#endif
		}

		/// <summary>Forgets everything, so the next ask loads again. For the editor after a bake.</summary>
		public static void Clear()
		{
			loaded.Clear();
			asked.Clear();
		}
	}
}
