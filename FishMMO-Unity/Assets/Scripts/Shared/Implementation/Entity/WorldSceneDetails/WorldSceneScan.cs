#if UNITY_EDITOR
using System;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared
{
	/// <summary>
	/// The seam the world scene details rebuild offers to editor tooling: every world scene, while
	/// it is open.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <see cref="WorldSceneDetailsCacheReader"/> already opens every world scene additively, reads
	/// it, and closes it again. Anything else that wants to look at all of them — the world systems
	/// audit, for one — would otherwise open the same seven scenes a second time for no reason.
	/// This hands each one over while it is loaded.
	/// </para>
	/// <para>
	/// A delegate rather than a direct call because of the assembly boundary: the reader lives in
	/// <c>FishMMO.Shared</c> and the tools that want the scenes live in editor assemblies that
	/// reference it. The same shape as <c>WeatherQuery.Commands</c> and <c>WeatherQuery.TickSource</c>
	/// — the runtime declares the socket, the editor plugs into it.
	/// </para>
	/// <para>
	/// <b>Listeners must not change the scene.</b> The reader closes each scene with
	/// <c>CloseScene(scene, true)</c> the moment it is done, so an edit made here is discarded
	/// without a word. Look, remember what you saw, and change it afterwards.
	/// </para>
	/// </remarks>
	public static class WorldSceneScan
	{
		/// <summary>Raised once per world scene during a rebuild, while that scene is open. Read-only.</summary>
		public static Action<Scene> Scanned;

		/// <summary>Called by the reader. Never throws out: a broken listener must not stop the rebuild.</summary>
		internal static void Report(Scene scene)
		{
			Action<Scene> scanned = Scanned;
			if (scanned == null)
			{
				return;
			}
			try
			{
				scanned(scene);
			}
			catch (Exception ex)
			{
				UnityEngine.Debug.LogError($"[WorldSceneScan] A listener threw while looking at '{scene.name}': {ex}");
			}
		}
	}
}
#endif
