#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FishMMO.GeneratedFiles
{
	/// <summary>
	/// Restores the *.generated.cs files from their committed sentinel templates
	/// whenever they are missing (fresh clone, after a clean, after a branch switch).
	///
	/// Why this exists: the generated files hold real deployment values — host names,
	/// certificate pins, and the client gate secret — so they are NOT tracked in git.
	/// The sentinel templates in GeneratedFileTemplates/ are tracked instead, and the
	/// real values are written over them by FishMMO Dashboard > Game Settings (or by
	/// CI substituting the sentinels before a release build).
	///
	/// Why this lives in its own assembly with no references: the generated files are
	/// compiled into FishMMO.Shared and FishMMO.Client, so when they are missing those
	/// assemblies fail to compile and nothing inside them can run. This assembly
	/// depends on neither, so it still compiles and can heal the project on load.
	///
	/// Existing files are never overwritten — restoring must never clobber real values.
	/// </summary>
	[InitializeOnLoad]
	public static class GeneratedFileRestorer
	{
		/// <summary>
		/// Folder holding the tracked sentinel templates, relative to the project root
		/// (the parent of Assets/). It lives outside Assets/ so Unity never imports it.
		/// </summary>
		private const string TemplateFolderName = "GeneratedFileTemplates";

		private const string TemplateExtension = ".template";

		/// <summary>
		/// Generated files, as paths relative to the project root. Each one is restored
		/// from "<see cref="TemplateFolderName"/>/&lt;file name&gt;.template".
		/// </summary>
		private static readonly string[] GeneratedFiles =
		{
			"Assets/Scripts/Shared/Implementation/HostConfig.generated.cs",
			"Assets/Scripts/Client/Security/CertificatePins.generated.cs",
			"Assets/Scripts/Client/Security/ClientApiSecret.generated.cs",
		};

		static GeneratedFileRestorer()
		{
			// Deferred: file writes and AssetDatabase.Refresh are not safe to run
			// directly from a static constructor during domain load.
			EditorApplication.delayCall += () => Restore(logWhenNothingToDo: false);
		}

		[MenuItem("FishMMO/Restore Missing Generated Files")]
		private static void RestoreFromMenu()
		{
			Restore(logWhenNothingToDo: true);
		}

		/// <summary>
		/// Entry point for automation:
		/// <c>Unity -batchmode -quit -projectPath . -executeMethod FishMMO.GeneratedFiles.GeneratedFileRestorer.RestoreFromCommandLine</c>
		/// Prefer GeneratedFileTemplates/restore-generated-files.sh, which needs no Unity.
		/// </summary>
		public static void RestoreFromCommandLine()
		{
			if (Restore(logWhenNothingToDo: true) < 0)
				EditorApplication.Exit(1);
		}

		/// <summary>
		/// Copies every missing generated file from its template.
		/// </summary>
		/// <returns>
		/// The number of files created, or -1 if a template was missing or unreadable.
		/// </returns>
		public static int Restore(bool logWhenNothingToDo)
		{
			string projectRoot = Path.GetDirectoryName(Application.dataPath);
			if (string.IsNullOrEmpty(projectRoot))
			{
				Debug.LogError("[FishMMO] Could not resolve the project root; generated files were not restored.");
				return -1;
			}

			string templateFolder = Path.Combine(projectRoot, TemplateFolderName);
			var restored = new List<string>();
			bool failed = false;

			foreach (string relativePath in GeneratedFiles)
			{
				string targetPath = Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
				if (File.Exists(targetPath))
					continue;

				string templatePath = Path.Combine(templateFolder, Path.GetFileName(relativePath) + TemplateExtension);
				if (!File.Exists(templatePath))
				{
					Debug.LogError(
						$"[FishMMO] Missing generated file '{relativePath}' and its template " +
						$"'{TemplateFolderName}/{Path.GetFileName(relativePath)}{TemplateExtension}'. " +
						"The project will not compile until it is restored.");
					failed = true;
					continue;
				}

				try
				{
					string targetDirectory = Path.GetDirectoryName(targetPath);
					if (!string.IsNullOrEmpty(targetDirectory) && !Directory.Exists(targetDirectory))
						Directory.CreateDirectory(targetDirectory);

					// Copy without overwrite: a file appearing between the check above and
					// here (a parallel restore, or Unity importing one) must win.
					File.Copy(templatePath, targetPath, false);
					restored.Add(relativePath);
				}
				catch (IOException)
				{
					// Target already exists — someone else restored it first. Not an error.
				}
				catch (Exception ex)
				{
					Debug.LogError($"[FishMMO] Failed to restore '{relativePath}': {ex.Message}");
					failed = true;
				}
			}

			if (restored.Count > 0)
			{
				Debug.Log(
					$"[FishMMO] Restored {restored.Count} generated file(s) from sentinel templates:\n  " +
					string.Join("\n  ", restored.ToArray()) +
					"\nOpen FishMMO Dashboard > Game Settings to write your real hosts, pins, and secret.");
				AssetDatabase.Refresh();
			}
			else if (logWhenNothingToDo && !failed)
			{
				Debug.Log("[FishMMO] All generated files are present — nothing to restore.");
			}

			return failed ? -1 : restored.Count;
		}
	}
}
#endif
