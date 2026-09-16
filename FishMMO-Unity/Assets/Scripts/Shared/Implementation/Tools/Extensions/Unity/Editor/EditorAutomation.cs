using System;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Lets one editor routine serve both a headless run and a button in an open editor.
	/// </summary>
	/// <remarks>
	/// Render and probe harnesses are launched headlessly with <c>-executeMethod</c> (with or
	/// without <c>-batchmode</c>) and must quit when they are done, or the launching script waits
	/// forever. The same routine run from a FishMMO Dashboard button must not close the editor it
	/// was clicked in.
	/// </remarks>
	public static class EditorAutomation
	{
		/// <summary>
		/// True when this editor was started to run a method and quit: batch mode, or an
		/// <c>-executeMethod</c> launch (a GUI editor under xvfb, for work that needs frames presented).
		/// </summary>
		public static bool LaunchedHeadless { get; } =
			Application.isBatchMode || Array.IndexOf(Environment.GetCommandLineArgs(), "-executeMethod") >= 0;

		/// <summary>
		/// Ends an automated run: quits with <paramref name="exitCode"/> when launched headless;
		/// otherwise leaves the editor open, stopping play mode if the routine started it.
		/// </summary>
		/// <param name="exitCode">The process exit code for a headless run.</param>
		public static void Finish(int exitCode)
		{
			if (LaunchedHeadless)
			{
				EditorApplication.Exit(exitCode);
				return;
			}

			if (EditorApplication.isPlaying)
			{
				EditorApplication.ExitPlaymode();
			}
			Debug.Log($"[EditorAutomation] Finished with {(exitCode == 0 ? "success" : $"exit code {exitCode}")}; the editor stays open.");
		}
	}
}
