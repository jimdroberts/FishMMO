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
	/// That is also why <see cref="GeneratedFileDriftCheck"/> runs alongside the restore:
	/// a file that already exists keeps the shape it had when it was written, so a field
	/// added to a template later never reaches it, and the only symptom is a CS0117 in an
	/// assembly that looks unrelated (https://github.com/jimdroberts/FishMMO/issues/122).
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
		/// The template a generated file is restored from and checked against, as a path
		/// relative to the project root.
		/// </summary>
		private static string TemplatePathFor(string relativePath)
		{
			return TemplateFolderName + "/" + Path.GetFileName(relativePath) + TemplateExtension;
		}

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
		/// <remarks>
		/// Strongly prefer the script in CI, because this entry point cannot report the
		/// problems it exists for. A batch-mode Unity that finds ANY assembly failing to
		/// compile logs "Scripts have compiler errors." and shuts down without running
		/// -executeMethod or any [InitializeOnLoad] constructor — and a missing or drifted
		/// generated file breaks FishMMO.Shared or FishMMO.Client by definition, so the
		/// run is already over before this method would be called. It is useful for the
		/// case where everything compiles and you want the check anyway; the shell script
		/// is what actually guards a headless build. In the interactive Editor there is no
		/// such abort, so loading the project still runs the restore and the drift report.
		/// </remarks>
		public static void RestoreFromCommandLine()
		{
			if (Restore(logWhenNothingToDo: true) < 0)
				EditorApplication.Exit(1);
		}

		/// <summary>
		/// Copies every missing generated file from its template, then checks the ones
		/// that already existed still declare everything their template declares.
		/// </summary>
		/// <returns>
		/// The number of files created, or -1 if a template was missing or unreadable, or
		/// an existing file has drifted from its template. A drifted file is a compile
		/// error waiting to happen, so automation should treat it the same as a missing one.
		/// </returns>
		public static int Restore(bool logWhenNothingToDo)
		{
			string projectRoot = Path.GetDirectoryName(Application.dataPath);
			if (string.IsNullOrEmpty(projectRoot))
			{
				Debug.LogError("[FishMMO] Could not resolve the project root; generated files were not restored.");
				return -1;
			}

			var restored = new List<string>();
			bool failed = false;

			foreach (string relativePath in GeneratedFiles)
			{
				string targetPath = Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
				if (File.Exists(targetPath))
					continue;

				string templatePath = Path.Combine(projectRoot, TemplatePathFor(relativePath).Replace('/', Path.DirectorySeparatorChar));
				if (!File.Exists(templatePath))
				{
					Debug.LogError(
						$"[FishMMO] Missing generated file '{relativePath}' and its template " +
						$"'{TemplatePathFor(relativePath)}'. " +
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

			// Runs after the restore so files created just now are covered too — they are
			// copies of their template, so they pass, and a drifted file is reported whether
			// or not anything was restored this pass.
			if (ReportDrift(projectRoot))
				failed = true;
			else if (restored.Count == 0 && logWhenNothingToDo && !failed)
				Debug.Log("[FishMMO] All generated files are present and match their templates — nothing to restore.");

			return failed ? -1 : restored.Count;
		}

		/// <summary>
		/// Logs every generated file that does not declare what its template declares.
		/// </summary>
		/// <returns>True if any file has drifted, or a template could not be read.</returns>
		private static bool ReportDrift(string projectRoot)
		{
			bool drifted = false;

			foreach (string relativePath in GeneratedFiles)
			{
				string targetPath = Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
				string templatePath = Path.Combine(projectRoot, TemplatePathFor(relativePath).Replace('/', Path.DirectorySeparatorChar));

				// A file missing here was already reported by the restore above.
				if (!File.Exists(targetPath) || !File.Exists(templatePath))
					continue;

				string drift;
				try
				{
					drift = GeneratedFileDriftCheck.Describe(
						relativePath,
						TemplatePathFor(relativePath),
						File.ReadAllText(templatePath),
						File.ReadAllText(targetPath));
				}
				catch (Exception ex)
				{
					Debug.LogError($"[FishMMO] Could not check '{relativePath}' against its template: {ex.Message}");
					drifted = true;
					continue;
				}

				if (drift == null)
					continue;

				Debug.LogError(drift);
				drifted = true;
			}

			return drifted;
		}
	}
}
#endif
