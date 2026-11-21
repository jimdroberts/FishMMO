using System;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace FishMMO.Shared
{
	/// <summary>
	/// Loads the asset a template names (its icon, its mesh) as the template enters the cache.
	/// </summary>
	/// <remarks>
	/// <para>
	/// In play mode and in a build this is the template's own Addressables load, unchanged: <see cref="AssetReference.LoadAssetAsync{TObject}"/>,
	/// released by the template's unload through <see cref="AssetReference.ReleaseAsset"/>.
	/// </para>
	/// <para>
	/// <b>In the editor outside play mode it reads the asset straight from the AssetDatabase</b> (<c>editorAsset</c>), at once,
	/// and starts no Addressables operation (2026-11-21). An edit-mode load went through whatever runtime catalog the editor
	/// held, which is the last player build's: after a server build that catalog has no client groups, so every icon load
	/// failed with InvalidKeyException. The failure arrived asynchronously, inside whichever test was running when it
	/// completed, and failed that test instead of the one that cached the template. Editor tools (the dashboard's tooltips)
	/// get their icons this way too. No handle is made, so the template's unload (guarded by <c>IsValid()</c>) releases nothing.
	/// </para>
	/// </remarks>
	public static class TemplateAssets
	{
		/// <summary>Loads the asset <paramref name="reference"/> names and hands it to <paramref name="onLoaded"/> (not called on a failure).</summary>
		public static void Load<T>(AssetReference reference, Action<T> onLoaded) where T : UnityEngine.Object
		{
			if (reference == null || onLoaded == null || !reference.RuntimeKeyIsValid())
			{
				return;
			}
#if UNITY_EDITOR
			if (!Application.isPlaying)
			{
				T asset = reference.editorAsset as T;
				if (asset == null && reference.editorAsset != null && typeof(T) == typeof(Sprite))
				{
					// A texture imported as a sprite: the reference names the texture, the sprite is its sub-asset.
					asset = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(UnityEditor.AssetDatabase.GetAssetPath(reference.editorAsset));
				}
				if (asset != null)
				{
					onLoaded(asset);
				}
				return;
			}
#endif
			/* A reference already loading or loaded (a template cached twice: the sim harness adds its mock content by hand
			 * after the labels already did) hands over that load's result. A second LoadAssetAsync on the same reference is
			 * an Addressables error, not a second load. */
			AsyncOperationHandle existing = reference.OperationHandle;
			if (existing.IsValid())
			{
				if (existing.IsDone)
				{
					if (existing.Status == AsyncOperationStatus.Succeeded && existing.Result is T done)
					{
						onLoaded(done);
					}
				}
				else
				{
					existing.Completed += handle =>
					{
						if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result is T result)
						{
							onLoaded(result);
						}
					};
				}
				return;
			}

			reference.LoadAssetAsync<T>().Completed += handle =>
			{
				if (handle.Status == AsyncOperationStatus.Succeeded)
				{
					onLoaded(handle.Result);
				}
			};
		}
	}
}
