#if UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FishMMO.Logging;
using FishMMO.Shared.Patcher;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace FishMMO.Shared
{
	/// <summary>
	/// Partial class handling Patch Generator inspector panel for the FishMMO Dashboard.
	/// Provides a UIToolkit interface for binary patch generation between client builds.
	/// </summary>
	public partial class FishMMODashboard
	{
		// ────────────────────────────────────────────
		//  PATCHER STATE
		// ────────────────────────────────────────────

		/// <summary>Path to the latest client build directory.</summary>
		private string patcherLatestClientDir = "";

		/// <summary>Path to a single old client build directory.</summary>
		private string patcherOldClientDir = "";

		/// <summary>Path to the root directory containing multiple old client builds.</summary>
		private string patcherOldClientsRootDir = "";

		/// <summary>Path to the output directory for generated patches.</summary>
		private string patcherOutputDir = "";

		/// <summary>Toggle for UI mode: true for multiple clients, false for single client.</summary>
		private bool patcherMultipleMode = false;

		/// <summary>Comma-separated list of file extensions to ignore.</summary>
		private string patcherIgnoredExtensions = ".cfg, .log, .bak, .lock";

		/// <summary>Comma-separated list of directory names to ignore.</summary>
		private string patcherIgnoredDirectories = "FishMMO_BackUpThisFolder_ButDontShipItWithYourGame, FishMMO_BurstDebugInformation_DoNotShip, .fishmmo-update-staging";

		/// <summary>Whether a patch operation is currently in progress.</summary>
		private bool patcherIsProcessing = false;

		/// <summary>Progress entries keyed by old client directory name.</summary>
		private readonly Dictionary<string, PatcherProgressEntry> patcherProgress = new Dictionary<string, PatcherProgressEntry>();

		/// <summary>Root container for the patcher UI, used for scheduled updates.</summary>
		private VisualElement patcherRoot;

		/// <summary>Container for progress bars, rebuilt during generation.</summary>
		private VisualElement patcherProgressContainer;

		/// <summary>Status label for manifest generation.</summary>
		private Label patcherManifestStatusLabel;

		/// <summary>Status label for patch generation.</summary>
		private Label patcherPatchStatusLabel;

		/// <summary>
		/// Progress tracking for a single patch operation.
		/// </summary>
		private sealed class PatcherProgressEntry
		{
			/// <summary>Progress fraction (0–1).</summary>
			public float Progress;
			/// <summary>Display message.</summary>
			public string Message = "Pending...";
			/// <summary>True if completed successfully.</summary>
			public bool Succeeded;
			/// <summary>True if the operation failed.</summary>
			public bool Failed;
		}

		// ────────────────────────────────────────────
		//  PATCHER UI
		// ────────────────────────────────────────────

		/// <summary>
		/// Shows the Patch Generator panel in the inspector area.
		/// </summary>
		private void ShowPatchGeneratorInspector()
		{
			ClearInspector();

			if (inspectorHeader != null)
			{
				inspectorHeader.text = "Patch Generator";
			}

			patcherRoot = new VisualElement();

			// ── Latest Client Directory ──
			VisualElement latestSection = CreateConstantsSection("Latest Client Directory");
			latestSection.Add(CreateDirectoryBrowseRow(
				patcherLatestClientDir,
				"Select Latest Client Build Directory",
				val => patcherLatestClientDir = val));
			patcherRoot.Add(latestSection);

			// ── Old Client Mode ──
			VisualElement modeSection = CreateConstantsSection("Old Client(s) Selection");

			Toggle modeToggle = new Toggle("Multiple Old Clients (from root directory)");
			modeToggle.value = patcherMultipleMode;
			modeToggle.style.marginBottom = 6;
			modeToggle.RegisterValueChangedCallback(evt =>
			{
				patcherMultipleMode = evt.newValue;
				ShowPatchGeneratorInspector(); // Rebuild to show correct field
			});
			modeSection.Add(modeToggle);

			if (patcherMultipleMode)
			{
				Label hint = new Label("Root directory containing versioned subfolders (e.g., 1.0.0, 1.0.1).");
				hint.style.fontSize = 10;
				hint.style.color = new Color(0.5f, 0.5f, 0.5f, 1f);
				hint.style.marginBottom = 4;
				hint.style.whiteSpace = WhiteSpace.Normal;
				modeSection.Add(hint);

				modeSection.Add(CreateDirectoryBrowseRow(
					patcherOldClientsRootDir,
					"Select Root Directory Containing Old Client Builds",
					val => patcherOldClientsRootDir = val));
			}
			else
			{
				Label hint = new Label("Single old client directory to generate a patch from.");
				hint.style.fontSize = 10;
				hint.style.color = new Color(0.5f, 0.5f, 0.5f, 1f);
				hint.style.marginBottom = 4;
				hint.style.whiteSpace = WhiteSpace.Normal;
				modeSection.Add(hint);

				modeSection.Add(CreateDirectoryBrowseRow(
					patcherOldClientDir,
					"Select Old Client Build Directory",
					val => patcherOldClientDir = val));
			}

			patcherRoot.Add(modeSection);

			// ── Output Directory ──
			VisualElement outputSection = CreateConstantsSection("Patch Output Directory");
			outputSection.Add(CreateDirectoryBrowseRow(
				patcherOutputDir,
				"Select Patch Output Directory",
				val => patcherOutputDir = val));
			patcherRoot.Add(outputSection);

			// ── Ignored Extensions ──
			VisualElement ignoreSection = CreateConstantsSection("Ignore Configuration");

			TextField extField = new TextField("Ignored Extensions");
			extField.value = patcherIgnoredExtensions;
			extField.tooltip = "Comma-separated file extensions to skip (e.g., .cfg, .log, .bak)";
			extField.RegisterValueChangedCallback(evt => patcherIgnoredExtensions = evt.newValue);
			ignoreSection.Add(extField);

			TextField dirField = new TextField("Ignored Directories");
			dirField.value = patcherIgnoredDirectories;
			dirField.tooltip = "Comma-separated directory names to skip entirely";
			dirField.RegisterValueChangedCallback(evt => patcherIgnoredDirectories = evt.newValue);
			ignoreSection.Add(dirField);

			patcherRoot.Add(ignoreSection);

			// ── Manifest Generation ──
			VisualElement manifestSection = CreateConstantsSection("Client File Manifest");

			Label manifestDesc = new Label("Generates a JSON manifest of all files and their checksums in the latest client directory for full client verification.");
			manifestDesc.style.fontSize = 10;
			manifestDesc.style.color = new Color(0.5f, 0.5f, 0.5f, 1f);
			manifestDesc.style.marginBottom = 6;
			manifestDesc.style.whiteSpace = WhiteSpace.Normal;
			manifestSection.Add(manifestDesc);

			Button manifestButton = new Button(OnGenerateManifestClicked);
			manifestButton.text = "Generate Client File Manifest";
			manifestButton.style.height = 32;
			manifestButton.style.backgroundColor = new Color(0.25f, 0.35f, 0.55f, 1f);
			manifestButton.style.color = new Color(0.75f, 0.85f, 1f, 1f);
			manifestButton.style.borderTopLeftRadius = 4;
			manifestButton.style.borderTopRightRadius = 4;
			manifestButton.style.borderBottomLeftRadius = 4;
			manifestButton.style.borderBottomRightRadius = 4;
			manifestSection.Add(manifestButton);

			patcherManifestStatusLabel = new Label("Ready.");
			patcherManifestStatusLabel.style.fontSize = 10;
			patcherManifestStatusLabel.style.color = new Color(0.5f, 0.5f, 0.5f, 1f);
			patcherManifestStatusLabel.style.marginTop = 4;
			manifestSection.Add(patcherManifestStatusLabel);

			patcherRoot.Add(manifestSection);

			// ── Patch Generation ──
			VisualElement patchSection = CreateConstantsSection("Patch Generation");

			Button patchButton = new Button(OnGeneratePatchesClicked);
			patchButton.text = "Generate Patches";
			patchButton.style.height = 36;
			patchButton.style.backgroundColor = new Color(0.35f, 0.45f, 0.25f, 1f);
			patchButton.style.color = new Color(0.8f, 0.95f, 0.7f, 1f);
			patchButton.style.borderTopLeftRadius = 4;
			patchButton.style.borderTopRightRadius = 4;
			patchButton.style.borderBottomLeftRadius = 4;
			patchButton.style.borderBottomRightRadius = 4;
			patchButton.style.fontSize = 13;
			patchButton.style.unityFontStyleAndWeight = FontStyle.Bold;
			patchSection.Add(patchButton);

			patcherPatchStatusLabel = new Label("");
			patcherPatchStatusLabel.style.fontSize = 10;
			patcherPatchStatusLabel.style.color = new Color(0.5f, 0.5f, 0.5f, 1f);
			patcherPatchStatusLabel.style.marginTop = 4;
			patchSection.Add(patcherPatchStatusLabel);

			patcherRoot.Add(patchSection);

			// ── Progress Container ──
			patcherProgressContainer = new VisualElement();
			patcherProgressContainer.style.marginTop = 4;
			patcherRoot.Add(patcherProgressContainer);

			inspectorContent.Add(patcherRoot);
		}

		/// <summary>
		/// Creates a row with a text field and a browse button for directory selection.
		/// </summary>
		private VisualElement CreateDirectoryBrowseRow(string currentValue, string dialogTitle, Action<string> onChanged)
		{
			VisualElement row = new VisualElement();
			row.style.flexDirection = FlexDirection.Row;
			row.style.alignItems = Align.Center;

			TextField field = new TextField();
			field.value = currentValue ?? "";
			field.style.flexGrow = 1;
			field.RegisterValueChangedCallback(evt => onChanged?.Invoke(evt.newValue));
			row.Add(field);

			Button browseButton = new Button(() =>
			{
				string selected = EditorUtility.OpenFolderPanel(dialogTitle, currentValue ?? "", "");
				if (!string.IsNullOrEmpty(selected))
				{
					field.value = selected;
					onChanged?.Invoke(selected);
				}
			});
			browseButton.text = "Browse";
			browseButton.style.width = 60;
			browseButton.style.marginLeft = 4;
			row.Add(browseButton);

			return row;
		}

		// ────────────────────────────────────────────
		//  MANIFEST GENERATION
		// ────────────────────────────────────────────

		/// <summary>
		/// Initiates client file manifest generation when the button is clicked.
		/// </summary>
		private async void OnGenerateManifestClicked()
		{
			if (patcherIsProcessing)
			{
				EditorUtility.DisplayDialog("Busy", "A patch operation is already in progress.", "OK");
				return;
			}

			if (!Directory.Exists(patcherLatestClientDir))
			{
				EditorUtility.DisplayDialog("Error", "Latest client directory does not exist.", "OK");
				return;
			}

			if (string.IsNullOrEmpty(patcherOutputDir) || !Directory.Exists(patcherOutputDir))
			{
				EditorUtility.DisplayDialog("Error", "Patch output directory is invalid or does not exist.", "OK");
				return;
			}

			patcherIsProcessing = true;
			UpdateManifestStatus("Scanning files and computing hashes...");

			try
			{
				HashSet<string> ignoredExt = ParseIgnoredExtensions(patcherIgnoredExtensions);
				HashSet<string> ignoredDir = ParseIgnoredDirectories(patcherIgnoredDirectories);
				string latestDir = patcherLatestClientDir;
				string outputDir = patcherOutputDir;

				Dictionary<string, (string relativePath, string hash)> filesWithHashes = null;
				await Task.Run(() =>
				{
					filesWithHashes = PatchGeneratorWindow.GetAllFilesWithHashes(latestDir, ignoredExt, ignoredDir);
				});

				var entries = filesWithHashes
					.Select(kvp => new PatchGeneratorWindow.CompleteManifestEntry
					{
						RelativePath = kvp.Key,
						Hash = kvp.Value.hash,
					})
					.OrderBy(e => e.RelativePath)
					.ToList();

				UpdateManifestStatus("Serializing manifest...");

				var options = new JsonSerializerOptions { WriteIndented = true };
				string json = JsonSerializer.Serialize(entries, options);

				string manifestPath = Path.Combine(outputDir, "client_file_manifest.json");
				await Task.Run(() => File.WriteAllText(manifestPath, json));

				UpdateManifestStatus($"Completed: client_file_manifest.json");
				SetStatus("Client file manifest generated.");
				EditorUtility.DisplayDialog("Success", $"Manifest generated at:\n{manifestPath}", "OK");
			}
			catch (Exception ex)
			{
				UpdateManifestStatus($"Failed: {ex.Message}");
				Debug.LogError($"[FishMMODashboard] Manifest generation failed: {ex.Message}");
				EditorUtility.DisplayDialog("Error", $"Manifest generation failed:\n{ex.Message}", "OK");
			}
			finally
			{
				patcherIsProcessing = false;
			}
		}

		/// <summary>
		/// Updates the manifest status label on the main thread.
		/// </summary>
		private void UpdateManifestStatus(string message)
		{
			if (patcherManifestStatusLabel != null)
			{
				patcherManifestStatusLabel.text = message;
			}
		}

		// ────────────────────────────────────────────
		//  PATCH GENERATION
		// ────────────────────────────────────────────

		/// <summary>
		/// Initiates patch generation when the button is clicked.
		/// </summary>
		private async void OnGeneratePatchesClicked()
		{
			if (patcherIsProcessing)
			{
				EditorUtility.DisplayDialog("Busy", "A patch operation is already in progress.", "OK");
				return;
			}

			// Validate latest client directory
			if (!Directory.Exists(patcherLatestClientDir))
			{
				EditorUtility.DisplayDialog("Error", "Latest client directory does not exist.", "OK");
				return;
			}

			// Gather old client directories
			List<string> oldClientDirs = new List<string>();
			if (patcherMultipleMode)
			{
				if (string.IsNullOrEmpty(patcherOldClientsRootDir) || !Directory.Exists(patcherOldClientsRootDir))
				{
					EditorUtility.DisplayDialog("Error", "Root directory for multiple old clients does not exist.", "OK");
					return;
				}
				string[] found = Directory.GetDirectories(patcherOldClientsRootDir);
				if (found.Length == 0)
				{
					EditorUtility.DisplayDialog("Info", "No subdirectories found in the old clients root directory.", "OK");
					return;
				}
				oldClientDirs.AddRange(found);
			}
			else
			{
				if (string.IsNullOrEmpty(patcherOldClientDir) || !Directory.Exists(patcherOldClientDir))
				{
					EditorUtility.DisplayDialog("Error", "Old client directory does not exist.", "OK");
					return;
				}
				oldClientDirs.Add(patcherOldClientDir);
			}

			if (string.IsNullOrEmpty(patcherOutputDir))
			{
				EditorUtility.DisplayDialog("Error", "Patch output directory is not set.", "OK");
				return;
			}

			patcherIsProcessing = true;
			patcherProgress.Clear();

			// Initialize progress entries
			foreach (string dir in oldClientDirs)
			{
				patcherProgress[Path.GetFileName(dir)] = new PatcherProgressEntry();
			}

			RebuildProgressBars();
			UpdatePatchStatus("Pre-caching client versions...");

			try
			{
				// Pre-cache version configs
				string latestDir = patcherLatestClientDir;
				VersionConfig latestVersion = await GetVersionConfigFromDirectory(latestDir);
				if (latestVersion == null)
				{
					EditorUtility.DisplayDialog("Error", "Failed to read version from latest client directory.\nEnsure version.txt exists.", "OK");
					return;
				}

				Dictionary<string, VersionConfig> oldVersions = new Dictionary<string, VersionConfig>();
				foreach (string oldDir in oldClientDirs)
				{
					VersionConfig cfg = await GetVersionConfigFromDirectory(oldDir);
					if (cfg != null)
					{
						oldVersions[oldDir] = cfg;
					}
					else
					{
						string name = Path.GetFileName(oldDir);
						if (patcherProgress.TryGetValue(name, out PatcherProgressEntry entry))
						{
							entry.Progress = 1f;
							entry.Failed = true;
							entry.Message = "Skipped (version not found)";
						}
					}
				}

				if (oldVersions.Count == 0)
				{
					EditorUtility.DisplayDialog("Error", "No valid old client versions found. Ensure version.txt exists in each directory.", "OK");
					return;
				}

				// Prepare output directory
				try
				{
					if (Directory.Exists(patcherOutputDir))
					{
						Directory.Delete(patcherOutputDir, true);
					}
					Directory.CreateDirectory(patcherOutputDir);
				}
				catch (Exception ex)
				{
					EditorUtility.DisplayDialog("Error", $"Could not prepare output directory:\n{ex.Message}", "OK");
					return;
				}

				UpdatePatchStatus("Generating patches...");

				// Capture locals for thread safety
				HashSet<string> ignoredExt = ParseIgnoredExtensions(patcherIgnoredExtensions);
				HashSet<string> ignoredDir = ParseIgnoredDirectories(patcherIgnoredDirectories);
				string outputDir = patcherOutputDir;
				string latestVer = latestVersion.FullVersion;
				PatchGenerator patchGen = new PatchGenerator();

				// Schedule periodic progress updates
				IVisualElementScheduledItem scheduledUpdate = patcherRoot?.schedule.Execute(() =>
				{
					RebuildProgressBars();
				}).Every(250);

				await Task.Run(() =>
				{
					Parallel.ForEach(oldVersions, (kvp) =>
					{
						string oldDir = kvp.Key;
						VersionConfig oldVer = kvp.Value;
						string dirName = Path.GetFileName(oldDir);

						if (patcherProgress.TryGetValue(dirName, out PatcherProgressEntry entry))
						{
							entry.Message = "Processing...";
						}

						try
						{
							PatchGeneratorWindow.CreatePatchInternal(
								patchGen,
								latestDir,
								latestVer,
								oldDir,
								oldVer.FullVersion,
								outputDir,
								ignoredExt,
								ignoredDir,
								(progress, message) =>
								{
									if (patcherProgress.TryGetValue(dirName, out PatcherProgressEntry e))
									{
										e.Progress = progress;
										e.Message = message;
									}
								});

							if (patcherProgress.TryGetValue(dirName, out PatcherProgressEntry done))
							{
								done.Progress = 1f;
								done.Succeeded = true;
								done.Message = "Completed!";
							}
						}
						catch (Exception ex)
						{
							if (patcherProgress.TryGetValue(dirName, out PatcherProgressEntry fail))
							{
								fail.Progress = 1f;
								fail.Failed = true;
								fail.Message = $"Failed: {ex.Message}";
							}
						}
					});
				});

				scheduledUpdate?.Pause();
				RebuildProgressBars();

				UpdatePatchStatus("All patches generated.");
				SetStatus("Patch generation complete.");
				EditorUtility.DisplayDialog("Success", "Patch generation finished. Check console for detailed logs.", "OK");
			}
			catch (Exception ex)
			{
				UpdatePatchStatus($"Failed: {ex.Message}");
				Debug.LogError($"[FishMMODashboard] Patch generation failed: {ex.Message}");
				EditorUtility.DisplayDialog("Error", $"Patch generation failed:\n{ex.Message}", "OK");
			}
			finally
			{
				patcherIsProcessing = false;
			}
		}

		/// <summary>
		/// Updates the patch generation status label.
		/// </summary>
		private void UpdatePatchStatus(string message)
		{
			if (patcherPatchStatusLabel != null)
			{
				patcherPatchStatusLabel.text = message;
			}
		}

		/// <summary>
		/// Rebuilds the progress bar UI from the current patcherProgress dictionary.
		/// </summary>
		private void RebuildProgressBars()
		{
			if (patcherProgressContainer == null) return;

			patcherProgressContainer.Clear();

			if (patcherProgress.Count == 0) return;

			Label header = new Label("Progress");
			header.AddToClassList("constants-section-header");
			header.style.marginBottom = 4;
			patcherProgressContainer.Add(header);

			foreach (var kvp in patcherProgress)
			{
				VisualElement row = new VisualElement();
				row.style.marginBottom = 4;

				Label nameLabel = new Label(kvp.Key);
				nameLabel.style.fontSize = 11;
				nameLabel.style.color = new Color(0.8f, 0.8f, 0.8f, 1f);
				nameLabel.style.marginBottom = 2;
				row.Add(nameLabel);

				ProgressBar bar = new ProgressBar();
				bar.value = kvp.Value.Progress * 100f;
				bar.title = kvp.Value.Message;
				bar.style.height = 18;

				if (kvp.Value.Succeeded)
				{
					bar.style.backgroundColor = new Color(0.15f, 0.3f, 0.15f, 1f);
				}
				else if (kvp.Value.Failed)
				{
					bar.style.backgroundColor = new Color(0.3f, 0.15f, 0.15f, 1f);
				}

				row.Add(bar);
				patcherProgressContainer.Add(row);
			}
		}

		// ────────────────────────────────────────────
		//  PATCHER HELPERS
		// ────────────────────────────────────────────

		/// <summary>
		/// Reads a VersionConfig from a version.txt file inside the given directory.
		/// </summary>
		private async Task<VersionConfig> GetVersionConfigFromDirectory(string directoryPath)
		{
			string versionFilePath = Path.Combine(directoryPath, "version.txt");

			string versionString = await Task.Run(() =>
			{
				try
				{
					if (File.Exists(versionFilePath))
					{
						return File.ReadAllText(versionFilePath).Trim();
					}
					Debug.LogError($"[FishMMODashboard] version.txt not found at: {versionFilePath}");
					return null;
				}
				catch (Exception ex)
				{
					Debug.LogError($"[FishMMODashboard] Error reading version file: {ex.Message}");
					return null;
				}
			});

			if (string.IsNullOrEmpty(versionString))
			{
				return null;
			}

			// VersionConfig.Parse may use Debug.Log, must run on main thread
			TaskCompletionSource<VersionConfig> tcs = new TaskCompletionSource<VersionConfig>();
			EditorApplication.delayCall += () =>
			{
				try
				{
					VersionConfig config = VersionConfig.Parse(versionString);
					tcs.SetResult(config);
				}
				catch (Exception ex)
				{
					Debug.LogError($"[FishMMODashboard] Error parsing version '{versionString}': {ex.Message}");
					tcs.SetResult(null);
				}
			};

			return await tcs.Task;
		}

		/// <summary>
		/// Parses a comma/space-separated string of file extensions into a HashSet.
		/// </summary>
		private static HashSet<string> ParseIgnoredExtensions(string input)
		{
			HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (string.IsNullOrEmpty(input)) return result;

			foreach (string ext in input.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
			{
				string trimmed = ext.Trim();
				if (!trimmed.StartsWith("."))
				{
					trimmed = "." + trimmed;
				}
				result.Add(trimmed);
			}
			return result;
		}

		/// <summary>
		/// Parses a comma/space-separated string of directory names into a HashSet.
		/// </summary>
		private static HashSet<string> ParseIgnoredDirectories(string input)
		{
			HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (string.IsNullOrEmpty(input)) return result;

			foreach (string dir in input.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
			{
				string trimmed = dir.Trim();
				if (!string.IsNullOrEmpty(trimmed))
				{
					result.Add(trimmed);
				}
			}
			return result;
		}
	}
}
#endif