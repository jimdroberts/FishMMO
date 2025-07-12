using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FishMMO.Logging;
using FishMMO.Shared;

namespace FishMMO.Patcher
{
	public class PatchGeneratorWindow : EditorWindow
	{
		// --- UI State Variables ---
		private string latestClientDirectory = "";
		private string oldClientsRootDirectory = ""; // For multiple old clients
		private string singleOldClientDirectory = ""; // For a single old client
		private bool generateMultipleClientsMode = false; // Toggle for UI mode
		private string patchOutputDirectory = "";
		private string ignoredExtensionsInput = ".cfg, .log, .bak";
		private string ignoredDirectoriesInput = "FishMMO_BackUpThisFolder_ButDontShipItWithYourGame, FishMMO_BurstDebugInformation_DoNotShip";

		// --- Internal Data & State ---
		private HashSet<string> ignoredExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		private HashSet<string> ignoredDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		// Cached version configurations
		private VersionConfig latestVersionConfig;
		private Dictionary<string, VersionConfig> oldClientVersionCache = new Dictionary<string, VersionConfig>();

		// UI related fields for displaying individual patch progress bars
		private Dictionary<string, PatchProgressInfo> patchProgress = new Dictionary<string, PatchProgressInfo>();
		private Vector2 scrollPosition;

		// Overall processing state to disable UI during async operations
		private bool isProcessing = false;
		private string manifestGenerationStatus = "Ready."; // Status for complete manifest generation

		// Helper class to hold progress details for each patch
		private class PatchProgressInfo
		{
			public float Progress { get; set; } = 0f;
			public Color Color { get; set; } = Color.gray;
			public string Message { get; set; } = "Pending...";
		}

		// Data structure for the complete manifest entry
		[Serializable]
		public class CompleteManifestEntry
		{
			public string RelativePath { get; set; }
			public string Hash { get; set; }
		}

		[MenuItem("FishMMO/Patch/Patch Generator")]
		public static void ShowWindow()
		{
			GetWindow<PatchGeneratorWindow>("FishMMO Patch Generator");
		}

		private void OnEnable()
		{
			UpdateIgnoredExtensionsSet();
			UpdateIgnoredDirectoriesSet();

			// Initialize custom logging for the Editor tool.
			Log.RegisterLoggerFactory(nameof(UnityConsoleLoggerConfig), (cfg, logCallback) => new UnityConsoleLogger((UnityConsoleLoggerConfig)cfg, logCallback));
			var defaultUnityConsoleLoggerConfig = new UnityConsoleLoggerConfig();
			var unityConsoleFormatter = new UnityConsoleFormatter(defaultUnityConsoleLoggerConfig.LogLevelColors, true);
			var manualLoggers = new List<FishMMO.Logging.ILogger>
			{
				new UnityConsoleLogger(new UnityConsoleLoggerConfig
				{
					Enabled = true,
					AllowedLevels = new HashSet<LogLevel>
					{
						LogLevel.Info, LogLevel.Debug, LogLevel.Warning, LogLevel.Error, LogLevel.Critical, LogLevel.Verbose
					}
				},
				(message) => Debug.Log($"{message}")), // Direct Debug.Log for internal callback
			};
			Log.Initialize(null, unityConsoleFormatter, manualLoggers, Log.OnInternalLogMessage, new List<Type>() { typeof(UnityConsoleLoggerConfig) });
		}

		private void OnGUI()
		{
			// --- Header ---
			GUILayout.Label("FishMMO Client Patch Generator", EditorStyles.boldLabel);
			EditorGUILayout.Space();

			// --- Latest Client Directory Selection ---
			EditorGUILayout.BeginHorizontal();
			latestClientDirectory = EditorGUILayout.TextField("Latest Client Directory", latestClientDirectory);
			if (GUILayout.Button("Browse", GUILayout.Width(80)))
			{
				string selectedPath = EditorUtility.OpenFolderPanel("Select Latest Client Build Directory", latestClientDirectory, "");
				if (!string.IsNullOrEmpty(selectedPath))
				{
					latestClientDirectory = selectedPath;
				}
			}
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.Space();
			GUILayout.Label("Old Client(s) Selection", EditorStyles.boldLabel);
			EditorGUILayout.HelpBox("Choose whether to generate patches from a single old client or multiple clients within a root directory.", MessageType.Info);

			// --- Mode Toggle for Old Clients ---
			EditorGUI.BeginChangeCheck();
			generateMultipleClientsMode = EditorGUILayout.ToggleLeft("Generate Patches for Multiple Old Clients (from a root directory)", generateMultipleClientsMode);
			if (EditorGUI.EndChangeCheck())
			{
				// Clear the other directory path when mode changes for clarity
				if (generateMultipleClientsMode)
				{
					singleOldClientDirectory = "";
				}
				else
				{
					oldClientsRootDirectory = "";
				}
			}

			// --- Conditional Old Client Directory Selection ---
			if (generateMultipleClientsMode)
			{
				EditorGUILayout.BeginHorizontal();
				oldClientsRootDirectory = EditorGUILayout.TextField("Root of Old Clients", oldClientsRootDirectory);
				if (GUILayout.Button("Browse", GUILayout.Width(80)))
				{
					string selectedPath = EditorUtility.OpenFolderPanel("Select Root Directory Containing Old Client Builds", oldClientsRootDirectory, "");
					if (!string.IsNullOrEmpty(selectedPath))
					{
						oldClientsRootDirectory = selectedPath;
					}
				}
				EditorGUILayout.EndHorizontal();
				EditorGUILayout.HelpBox("Example: C:\\OldGameClients\\ (contains 1.0.0, 1.0.1, etc. subfolders)", MessageType.None);
			}
			else // Single client mode
			{
				EditorGUILayout.BeginHorizontal();
				singleOldClientDirectory = EditorGUILayout.TextField("Single Old Client Directory", singleOldClientDirectory);
				if (GUILayout.Button("Browse", GUILayout.Width(80)))
				{
					string selectedPath = EditorUtility.OpenFolderPanel("Select a Single Old Client Build Directory", singleOldClientDirectory, "");
					if (!string.IsNullOrEmpty(selectedPath))
					{
						singleOldClientDirectory = selectedPath;
					}
				}
				EditorGUILayout.EndHorizontal();
				EditorGUILayout.HelpBox("Example: C:\\OldGameClients\\1.0.0\\", MessageType.None);
			}

			EditorGUILayout.Space();

			// --- Patch Output Directory Selection ---
			EditorGUILayout.BeginHorizontal();
			patchOutputDirectory = EditorGUILayout.TextField("Patch Output Directory", patchOutputDirectory);
			if (GUILayout.Button("Browse", GUILayout.Width(80)))
			{
				string selectedPath = EditorUtility.OpenFolderPanel("Select Patch Output Directory", patchOutputDirectory, "");
				if (!string.IsNullOrEmpty(selectedPath))
				{
					patchOutputDirectory = selectedPath;
				}
			}
			EditorGUILayout.EndHorizontal();

			// --- Ignored Extensions Configuration ---
			EditorGUI.BeginChangeCheck();
			ignoredExtensionsInput = EditorGUILayout.TextField("Ignored File Extensions (comma-separated)", ignoredExtensionsInput);
			if (EditorGUI.EndChangeCheck())
			{
				UpdateIgnoredExtensionsSet();
			}
			EditorGUILayout.HelpBox("Files with these extensions will be ignored during hash calculation and patch generation. Example: .cfg, .log, .tmp", MessageType.Info);

			// --- Ignored Directories Configuration ---
			EditorGUI.BeginChangeCheck();
			ignoredDirectoriesInput = EditorGUILayout.TextField("Ignored Directories (comma-separated)", ignoredDirectoriesInput);
			if (EditorGUI.EndChangeCheck())
			{
				UpdateIgnoredDirectoriesSet();
			}
			EditorGUILayout.HelpBox("Directories with these names will be completely skipped during file scanning. Example: MyTempFolder, DebugInfo", MessageType.Info);

			EditorGUILayout.Space();

			// --- Generate Patches Button ---
			EditorGUI.BeginDisabledGroup(isProcessing); // Disable button when processing
			if (GUILayout.Button("Generate Patches", GUILayout.Height(40)))
			{
				GeneratePatchesAsync(); // Call the async method
			}
			EditorGUI.EndDisabledGroup();

			EditorGUILayout.Space();
			GUILayout.Label("Generate Complete Manifest:", EditorStyles.boldLabel);
			EditorGUILayout.HelpBox("Generates a manifest (JSON) of all files and their checksums in the Latest Client Directory. This can be used for full client verification.", MessageType.Info);
			EditorGUI.BeginDisabledGroup(isProcessing);
			if (GUILayout.Button("Generate Client File Manifest", GUILayout.Height(30)))
			{
				GenerateCompleteManifestAsync();
			}
			EditorGUI.EndDisabledGroup();
			EditorGUILayout.LabelField("Status:", manifestGenerationStatus);


			EditorGUILayout.Space();
			GUILayout.Label("Patch Generation Progress:", EditorStyles.boldLabel);

			// --- Progress Bars in a Scroll View ---
			scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(200));
			foreach (var entry in patchProgress)
			{
				EditorGUILayout.BeginHorizontal();
				GUI.color = entry.Value.Color;
				Rect rect = EditorGUILayout.BeginVertical();
				EditorGUI.ProgressBar(new Rect(rect.x, rect.y, position.width - 40, EditorGUIUtility.singleLineHeight), entry.Value.Progress, $"{entry.Key}: {entry.Value.Message}");
				GUILayout.Space(EditorGUIUtility.singleLineHeight + 2);
				EditorGUILayout.EndVertical();
				GUI.color = Color.white;
				EditorGUILayout.EndHorizontal();
			}
			EditorGUILayout.EndScrollView();

			// Force repaint to update progress bars frequently
			if (isProcessing)
			{
				Repaint();
			}
		}

		private void UpdateIgnoredExtensionsSet()
		{
			ignoredExtensions.Clear();
			foreach (var ext in ignoredExtensionsInput.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
			{
				string trimmedExt = ext.Trim();
				if (!trimmedExt.StartsWith("."))
				{
					trimmedExt = "." + trimmedExt;
				}
				ignoredExtensions.Add(trimmedExt);
			}
		}

		// Update ignored directories set
		private void UpdateIgnoredDirectoriesSet()
		{
			ignoredDirectories.Clear();
			foreach (var dir in ignoredDirectoriesInput.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
			{
				string trimmedDir = dir.Trim();
				if (!string.IsNullOrEmpty(trimmedDir))
				{
					ignoredDirectories.Add(trimmedDir);
				}
			}
		}

		/// <summary>
		/// Initiates the patch generation process asynchronously.
		/// This method orchestrates the pre-caching of version data on the main thread,
		/// then triggers multi-threaded patch generation.
		/// </summary>
		private async void GeneratePatchesAsync()
		{
			if (isProcessing) return;
			isProcessing = true; // Set processing flag

			EditorUtility.ClearProgressBar();
			patchProgress.Clear();

			try
			{
				// --- Step 1: Validate Directories ---
				if (!Directory.Exists(latestClientDirectory))
				{
					Log.Error("Patcher", $"Error: Latest client directory '{latestClientDirectory}' does not exist. Please check the path.");
					EditorUtility.DisplayDialog("Error", "Latest client directory does not exist.", "OK");
					return;
				}

				List<string> oldClientDirsToProcess = new List<string>();

				if (generateMultipleClientsMode)
				{
					if (!string.IsNullOrEmpty(oldClientsRootDirectory) && Directory.Exists(oldClientsRootDirectory))
					{
						string[] foundOldDirs = Directory.GetDirectories(oldClientsRootDirectory);
						if (foundOldDirs.Length == 0)
						{
							Log.Info("Patcher", $"No old client version subdirectories found in '{oldClientsRootDirectory}'.");
						}
						else
						{
							oldClientDirsToProcess.AddRange(foundOldDirs);
						}
					}
					else
					{
						Log.Error("Patcher", "Error: Root directory for multiple old clients is not valid or does not exist.");
						EditorUtility.DisplayDialog("Error", "Root directory for multiple old clients is not valid or does not exist.", "OK");
						return;
					}
				}
				else // Single client mode
				{
					if (!string.IsNullOrEmpty(singleOldClientDirectory) && Directory.Exists(singleOldClientDirectory))
					{
						oldClientDirsToProcess.Add(singleOldClientDirectory);
					}
					else
					{
						Log.Error("Patcher", "Error: Single old client directory is not valid or does not exist.");
						EditorUtility.DisplayDialog("Error", "Single old client directory is not valid or does not exist.", "OK");
						return;
					}
				}

				if (oldClientDirsToProcess.Count == 0)
				{
					Log.Info("Patcher", "No old client directories to process. Aborting.");
					EditorUtility.DisplayDialog("Information", "No old client directories found to process.", "OK");
					return;
				}

				// Initialize patch progress entries for UI display
				foreach (var dir in oldClientDirsToProcess)
				{
					patchProgress.TryAdd(Path.GetFileName(dir), new PatchProgressInfo());
				}

				// --- Step 2: Pre-cache ALL Version Data (Latest & Old) ---
				Log.Info("Patcher", "Starting pre-caching of all client versions...");
				await PreloadAllClientVersionsAsync(latestClientDirectory, oldClientDirsToProcess);

				if (latestVersionConfig == null)
				{
					EditorUtility.DisplayDialog("Error", "Failed to pre-cache latest client version. Aborting.", "OK");
					return;
				}
				if (oldClientVersionCache.Count == 0 && oldClientDirsToProcess.Any())
				{
					EditorUtility.DisplayDialog("Warning", "No old client versions were successfully pre-cached. Patch generation may not proceed as expected.", "OK");
				}

				// --- Step 3: Prepare Patch Output Directory ---
				try
				{
					if (Directory.Exists(patchOutputDirectory))
					{
						Log.Warning("Patcher", $"Deleting existing content in patch output directory '{patchOutputDirectory}'.");
						Directory.Delete(patchOutputDirectory, true);
					}
					Directory.CreateDirectory(patchOutputDirectory);
				}
				catch (Exception ex)
				{
					Log.Error("Patcher", $"Error preparing patch output directory '{patchOutputDirectory}': {ex.Message}");
					EditorUtility.DisplayDialog("Error", $"Could not prepare output directory: {ex.Message}", "OK");
					return;
				}

				Log.Info("Patcher", "The following file extensions will be ignored during patch generation: " + string.Join(", ", ignoredExtensions));
				Log.Info("Patcher", "The following directories will be ignored during patch generation: " + string.Join(", ", ignoredDirectories));

				// --- Step 4: Generate Patches in Parallel using cached data ---
				Log.Info("Patcher", "Starting multi-threaded patch generation process.");
				Log.Info("Patcher", $"Generating patches from {oldClientDirsToProcess.Count} old client versions to latest ({latestVersionConfig.FullVersion}):");

				PatchGenerator patchGenerator = new PatchGenerator();

				await Task.Run(() =>
				{
					Parallel.ForEach(oldClientDirsToProcess, (oldDirectoryPath) =>
					{
						string oldDirName = Path.GetFileName(oldDirectoryPath);

						if (!oldClientVersionCache.TryGetValue(oldDirectoryPath, out VersionConfig oldVersionConfig))
						{
							Log.Error("Patcher", $"[Patch {oldDirName}] Error: VersionConfig not found in cache for {oldDirectoryPath}. Skipping this patch.");
							EditorApplication.delayCall += () =>
							{
								if (patchProgress.TryGetValue(oldDirName, out var info))
								{
									info.Progress = 1f;
									info.Color = Color.red;
									info.Message = "Skipped (version not cached)";
								}
								Repaint();
							};
							return;
						}

						EditorApplication.delayCall += () =>
						{
							if (patchProgress.TryGetValue(oldDirName, out var info))
							{
								info.Message = "Processing files and diffs...";
								info.Color = Color.yellow;
								info.Progress = 0.05f;
							}
							Repaint();
						};

						try
						{
							CreatePatchInternal(patchGenerator,
												latestVersionConfig.FullVersion,
												oldDirectoryPath,
												oldVersionConfig.FullVersion,
												patchOutputDirectory,
												ignoredExtensions,
												ignoredDirectories,
												(progress, message) =>
												{
													EditorApplication.delayCall += () =>
													{
														if (patchProgress.TryGetValue(oldDirName, out var info))
														{
															info.Progress = progress;
															info.Message = message;
														}
														Repaint();
													};
												});

							EditorApplication.delayCall += () =>
							{
								if (patchProgress.TryGetValue(oldDirName, out var info))
								{
									info.Progress = 1f;
									info.Color = Color.green;
									info.Message = "Completed!";
								}
								Repaint();
							};
						}
						catch (Exception ex)
						{
							Log.Error("Patcher", $"Error generating patch for {oldDirName}: {ex.Message}", ex);
							EditorApplication.delayCall += () =>
							{
								if (patchProgress.TryGetValue(oldDirName, out var info))
								{
									info.Progress = 1f;
									info.Color = Color.red;
									info.Message = $"Failed: {ex.Message}";
								}
								Repaint();
							};
						}
					});
				});

				Log.Info("Patcher", "All patch generation operations complete.");
				EditorUtility.DisplayDialog("Success", "Patch generation process finished. Check console for detailed logs.", "OK");
			}
			catch (Exception ex)
			{
				Log.Error("Patcher", $"An unhandled error occurred during patch generation: {ex.Message}", ex);
				EditorUtility.DisplayDialog("Error", $"An unexpected error occurred: {ex.Message}", "OK");
			}
			finally
			{
				isProcessing = false;
				EditorUtility.ClearProgressBar();
				Repaint();
			}
		}

		/// <summary>
		/// Generates a complete manifest (JSON file) of all files and their checksums
		/// in the specified latest client directory.
		/// </summary>
		private async void GenerateCompleteManifestAsync()
		{
			if (isProcessing) return;
			isProcessing = true;
			manifestGenerationStatus = "Starting...";
			Repaint();

			try
			{
				if (!Directory.Exists(latestClientDirectory))
				{
					Log.Error("Patcher", $"Error: Latest client directory '{latestClientDirectory}' does not exist. Cannot generate manifest.");
					EditorUtility.DisplayDialog("Error", "Latest client directory does not exist.", "OK");
					manifestGenerationStatus = "Failed: Directory not found.";
					return;
				}
				if (string.IsNullOrEmpty(patchOutputDirectory) || !Directory.Exists(patchOutputDirectory))
				{
					Log.Error("Patcher", $"Error: Patch output directory '{patchOutputDirectory}' is invalid or does not exist. Cannot save manifest.");
					EditorUtility.DisplayDialog("Error", "Patch output directory is invalid or does not exist.", "OK");
					manifestGenerationStatus = "Failed: Output directory invalid.";
					return;
				}

				manifestGenerationStatus = "Scanning files and computing hashes...";
				Repaint();

				Dictionary<string, (string relativePath, string hash)> filesWithHashes = new Dictionary<string, (string, string)>();
				await Task.Run(() =>
				{
					filesWithHashes = GetAllFilesWithHashes(latestClientDirectory, ignoredExtensions, ignoredDirectories);
				});

				List<CompleteManifestEntry> manifestEntries = filesWithHashes.Select(kvp => new CompleteManifestEntry
				{
					RelativePath = kvp.Key,
					Hash = kvp.Value.hash
				}).OrderBy(e => e.RelativePath).ToList(); // Order for consistent manifest generation

				string manifestFileName = $"client_file_manifest.json";
				string manifestFilePath = Path.Combine(patchOutputDirectory, manifestFileName);

				manifestGenerationStatus = "Serializing manifest to JSON...";
				Repaint();

				var options = new JsonSerializerOptions { WriteIndented = true };
				string manifestJson = JsonSerializer.Serialize(manifestEntries, options);

				manifestGenerationStatus = "Saving manifest file...";
				Repaint();

				await Task.Run(() =>
				{
					File.WriteAllText(manifestFilePath, manifestJson);
				});

				Log.Info("Patcher", $"Successfully generated complete manifest: {manifestFilePath}");
				EditorUtility.DisplayDialog("Success", $"Complete manifest generated at:\n{manifestFilePath}", "OK");
				manifestGenerationStatus = $"Completed: {manifestFileName}";
			}
			catch (Exception ex)
			{
				Log.Error("Patcher", $"Error generating complete manifest: {ex.Message}", ex);
				EditorUtility.DisplayDialog("Error", $"Failed to generate complete manifest: {ex.Message}", "OK");
				manifestGenerationStatus = $"Failed: {ex.Message}";
			}
			finally
			{
				isProcessing = false;
				Repaint();
			}
		}


		/// <summary>
		/// Preloads version configurations for the latest client and all discovered old clients.
		/// This method reads the version from the 'version.txt' file within each client directory.
		/// </summary>
		/// <param name="latestClientPath">The path to the latest client build directory.</param>
		/// <param name="oldClientPaths">A list of paths to old client build directories.</param>
		private async Task PreloadAllClientVersionsAsync(string latestClientPath, List<string> oldClientPaths)
		{
			latestVersionConfig = null;
			oldClientVersionCache.Clear();

			float totalOperations = 1 + oldClientPaths.Count;
			float currentOperationIndex = 0;

			// Preload Latest Client Version
			EditorUtility.DisplayProgressBar("Pre-caching Client Versions", $"Loading latest client version from: {Path.GetFileName(latestClientPath)}...", currentOperationIndex / totalOperations);
			latestVersionConfig = await GetVersionConfigFromFile(latestClientPath);
			if (latestVersionConfig == null)
			{
				Log.Error("Patcher", $"Failed to pre-cache latest client version from '{latestClientPath}'.");
			}
			else
			{
				Log.Info("Patcher", $"Pre-cached latest client version from '{Path.GetFileName(latestClientPath)}': {latestVersionConfig.FullVersion}");
			}
			currentOperationIndex++;

			// Preload Old Client Versions
			foreach (string oldPath in oldClientPaths)
			{
				string oldClientName = Path.GetFileName(oldPath);
				EditorUtility.DisplayProgressBar("Pre-caching Client Versions", $"Loading old client version from: {oldClientName}...", currentOperationIndex / totalOperations);

				VersionConfig oldConfig = await GetVersionConfigFromFile(oldPath);
				if (oldConfig == null)
				{
					Log.Warning("Patcher", $"Failed to pre-cache old client version from '{oldPath}'. This client will be skipped.");
				}
				else
				{
					oldClientVersionCache[oldPath] = oldConfig;
					Log.Info("Patcher", $"Pre-cached old client version from '{oldClientName}': {oldConfig.FullVersion}");
				}
				currentOperationIndex++;
			}

			EditorUtility.DisplayProgressBar("Pre-caching Client Versions", "All client versions pre-cached.", 1f);
			await Task.Yield(); // Allow UI to update
			EditorUtility.ClearProgressBar();
		}

		/// <summary>
		/// Reads the version string from 'version.txt' within the specified directory and parses it into a VersionConfig.
		/// This method handles file I/O and parsing errors.
		/// </summary>
		/// <param name="directoryPath">The path to the client build directory containing 'version.txt'.</param>
		/// <returns>A VersionConfig instance if successful, otherwise null.</returns>
		private async Task<VersionConfig> GetVersionConfigFromFile(string directoryPath)
		{
			string versionFilePath = Path.Combine(directoryPath, "version.txt");
			VersionConfig config = null;

			// Ensure file reading happens on a background thread to avoid blocking the UI
			string versionString = await Task.Run(() =>
			{
				try
				{
					if (File.Exists(versionFilePath))
					{
						return File.ReadAllText(versionFilePath).Trim();
					}
					else
					{
						Log.Error("Patcher", $"Version file not found at: {versionFilePath}");
						return null;
					}
				}
				catch (Exception ex)
				{
					Log.Error("Patcher", $"Error reading version file from '{versionFilePath}': {ex.Message}");
					return null;
				}
			});

			if (!string.IsNullOrEmpty(versionString))
			{
				// Parsing VersionConfig.Parse uses Debug.LogError, so it must run on the main thread.
				TaskCompletionSource<VersionConfig> tcs = new TaskCompletionSource<VersionConfig>();
				EditorApplication.delayCall += () =>
				{
					try
					{
						config = VersionConfig.Parse(versionString);
						tcs.SetResult(config);
					}
					catch (Exception ex)
					{
						Log.Error("Patcher", $"Error parsing version string '{versionString}': {ex.Message}");
						tcs.SetException(ex);
					}
				};
				await tcs.Task; // Wait for the parsing to complete on the main thread
			}
			return config;
		}


		// Delegate definition for the progress callback
		public delegate void ProgressCallback(float progress, string message);

		/// <summary>
		/// Generates a patch ZIP file comparing an old client directory with the latest client directory.
		/// This method now takes pre-cached version strings.
		/// </summary>
		/// <param name="patchGenerator">The PatchGenerator instance for binary diffing.</param>
		/// <param name="latestVersionString">The full version string of the latest client.</param>
		/// <param name="oldDirectory">The full path to the old client build to generate a patch from.</param>
		/// <param name="oldVersionString">The full version string of the old client.</param>
		/// <param name="patchOutputDirectory">The directory where the generated patch ZIP will be saved.</param>
		/// <param name="ignoredExtensions">A set of file extensions to ignore during hashing and inclusion.</param>
		/// <param name="ignoredDirectories">A set of directory names to ignore.</param>
		/// <param name="progressCallback">An optional callback to report progress and status messages.</param>
		public void CreatePatchInternal(PatchGenerator patchGenerator, string latestVersionString, string oldDirectory, string oldVersionString, string patchOutputDirectory, HashSet<string> ignoredExtensions, HashSet<string> ignoredDirectories, ProgressCallback progressCallback = null)
		{
			string oldDirName = Path.GetFileName(oldDirectory);

			progressCallback?.Invoke(0.1f, "Scanning files for hashes...");

			string latestVersion = latestVersionString;
			string oldVersion = oldVersionString;

			string patchFileName = $"{oldVersion}-{latestVersion}.zip";
			string patchFilePath = Path.Combine(patchOutputDirectory, patchFileName);
			string tempZipFilePath = Path.Combine(patchOutputDirectory, $"temp_{Guid.NewGuid()}.zip");

			ConcurrentBag<string> tempPatchFilesToCleanUp = new ConcurrentBag<string>();

			Log.Info("Patcher", $"Generating patch: {patchFileName}");

			try
			{
				Log.Info("Patcher", $"Scanning Latest Files for hashes (ignoring {ignoredExtensions.Count} extensions and {ignoredDirectories.Count} directories)...");
				Dictionary<string, (string relativePath, string hash)> latestFilesWithHashes = GetAllFilesWithHashes(latestClientDirectory, ignoredExtensions, ignoredDirectories);
				HashSet<string> latestFileRelativePaths = DictionaryKeysToHashSet(latestFilesWithHashes);

				Log.Info("Patcher", $"Scanning Old Files for hashes (ignoring {ignoredExtensions.Count} extensions and {ignoredDirectories.Count} directories)...");
				Dictionary<string, (string relativePath, string hash)> oldFilesWithHashes = GetAllFilesWithHashes(oldDirectory, ignoredExtensions, ignoredDirectories);
				HashSet<string> oldFileRelativePaths = DictionaryKeysToHashSet(oldFilesWithHashes);

				List<string> filesToDelete = oldFileRelativePaths.Except(latestFileRelativePaths).ToList();
				List<string> filesToAdd = latestFileRelativePaths.Except(oldFileRelativePaths).ToList();
				List<string> filesToCompare = oldFileRelativePaths.Intersect(latestFileRelativePaths).ToList();

				ConcurrentBag<ModifiedFileEntry> modifiedFilesData = new ConcurrentBag<ModifiedFileEntry>();
				ConcurrentBag<NewFileEntry> newFilesData = new ConcurrentBag<NewFileEntry>();
				ConcurrentBag<DeletedFileEntry> deletedFilesData = new ConcurrentBag<DeletedFileEntry>();

				foreach (var deletedFilePath in filesToDelete)
				{
					deletedFilesData.Add(new DeletedFileEntry { RelativePath = deletedFilePath });
					Log.Info("Patcher", $"\tIdentified for deletion: {deletedFilePath}");
				}

				progressCallback?.Invoke(0.4f, "Generating file diffs for modified files...");
				Parallel.ForEach(filesToCompare, (comparedRelativePath) =>
				{
					if (oldFilesWithHashes[comparedRelativePath].hash != latestFilesWithHashes[comparedRelativePath].hash)
					{
						string oldFullFilePath = Path.Combine(oldDirectory, comparedRelativePath);
						string newFullFilePath = Path.Combine(latestClientDirectory, comparedRelativePath);

						byte[] patchDataBytes = patchGenerator.Generate(oldFullFilePath, newFullFilePath);

						if (patchDataBytes.Length > 0)
						{
							string tempPatchFile = Path.Combine(Path.GetTempPath(), $"patch_{Guid.NewGuid()}.bin");
							try
							{
								File.WriteAllBytes(tempPatchFile, patchDataBytes);
								tempPatchFilesToCleanUp.Add(tempPatchFile);

								// Get the final file size from the new file
								long finalFileSize = new FileInfo(newFullFilePath).Length;

								modifiedFilesData.Add(new ModifiedFileEntry
								{
									RelativePath = comparedRelativePath,
									OldHash = oldFilesWithHashes[comparedRelativePath].hash,
									NewHash = latestFilesWithHashes[comparedRelativePath].hash,
									PatchDataEntryName = $"patches/{comparedRelativePath.Replace('\\', '/')}.bin",
									TempPatchFilePath = tempPatchFile,
									FinalFileSize = finalFileSize // Populate the new property
								});
								Log.Info("Patcher", $"\tGenerated patch for modified: {comparedRelativePath} (written to temp file)");
							}
							catch (Exception ex)
							{
								Log.Error("Patcher", $"\tError writing patch for {comparedRelativePath} to temp file: {ex.Message}");
							}
						}
						else
						{
							Log.Info("Patcher", $"\tFiles are identical, no patch generated for: {comparedRelativePath}");
						}
					}
				});

				progressCallback?.Invoke(0.6f, "Processing new files...");
				Parallel.ForEach(filesToAdd, (newRelativePath) =>
				{
					newFilesData.Add(new NewFileEntry
					{
						RelativePath = newRelativePath,
						NewHash = latestFilesWithHashes[newRelativePath].hash,
						FileDataEntryName = $"new_files/{newRelativePath.Replace('\\', '/')}"
					});
					Log.Info("Patcher", $"\tPrepared metadata for new file: {newRelativePath}");
				});

				PatchManifest manifest = new PatchManifest
				{
					OldVersion = oldVersion,
					NewVersion = latestVersion,
					DeletedFiles = deletedFilesData.ToList(),
					ModifiedFiles = modifiedFilesData.ToList(),
					NewFiles = newFilesData.ToList()
				};

				var options = new JsonSerializerOptions { WriteIndented = true };
				string manifestJson = JsonSerializer.Serialize(manifest, options);
				byte[] manifestBytes = Encoding.UTF8.GetBytes(manifestJson);

				progressCallback?.Invoke(0.8f, "Compressing patch archive...");
				using (FileStream zipStream = File.Create(tempZipFilePath))
				{
					using (ZipArchive archive = new ZipArchive(zipStream, ZipArchiveMode.Create, true))
					{
						ZipArchiveEntry manifestEntry = archive.CreateEntry("manifest.json", System.IO.Compression.CompressionLevel.Optimal);
						using (Stream entryStream = manifestEntry.Open())
						{
							entryStream.Write(manifestBytes, 0, manifestBytes.Length);
						}
						Log.Info("Patcher", $"\tAdded manifest.json to ZIP.");

						foreach (var entry in modifiedFilesData)
						{
							if (!string.IsNullOrEmpty(entry.TempPatchFilePath) && File.Exists(entry.TempPatchFilePath))
							{
								ZipArchiveEntry patchEntry = archive.CreateEntry(entry.PatchDataEntryName, System.IO.Compression.CompressionLevel.Optimal);
								using (Stream entryStream = patchEntry.Open())
								using (FileStream sourcePatchStream = File.OpenRead(entry.TempPatchFilePath))
								{
									sourcePatchStream.CopyTo(entryStream);
								}
								Log.Info("Patcher", $"\tAdded patch data for {entry.RelativePath} to ZIP by streaming from temp file.");
							}
							else
							{
								Log.Warning("Patcher", $"\tTemporary patch file not found for {entry.RelativePath}, skipping ZIP addition.");
							}
						}

						foreach (var entry in newFilesData)
						{
							string fullFilePath = Path.Combine(latestClientDirectory, entry.RelativePath);
							if (File.Exists(fullFilePath))
							{
								ZipArchiveEntry newFileEntry = archive.CreateEntry(entry.FileDataEntryName, System.IO.Compression.CompressionLevel.Optimal);
								using (Stream entryStream = newFileEntry.Open())
								using (FileStream sourceFileStream = File.OpenRead(fullFilePath))
								{
									sourceFileStream.CopyTo(entryStream);
								}
								Log.Info("Patcher", $"\tAdded new file data for {entry.RelativePath} to ZIP by streaming.");
							}
							else
							{
								Log.Warning("Patcher", $"\tNew file not found at {fullFilePath}, skipping ZIP addition.");
							}
						}
					}
				}

				progressCallback?.Invoke(0.95f, "Finalizing patch file...");
				File.Move(tempZipFilePath, patchFilePath);
				Log.Info("Patcher", $"Patch build completed: {patchFileName}");
				progressCallback?.Invoke(1.0f, "Completed!");
			}
			catch (Exception ex)
			{
				Log.Error("Patcher", $"Error generating patch for '{oldDirectory}': {ex.Message}", ex);
				if (File.Exists(tempZipFilePath))
				{
					File.Delete(tempZipFilePath);
				}
				throw;
			}
			finally
			{
				foreach (string tempFile in tempPatchFilesToCleanUp)
				{
					try
					{
						if (File.Exists(tempFile))
						{
							File.Delete(tempFile);
							Log.Info("Patcher", $"\tCleaned up temporary patch file: {tempFile}");
						}
					}
					catch (Exception ex)
					{
						Log.Error("Patcher", $"\tError cleaning up temporary file {tempFile}: {ex.Message}");
					}
				}
			}
		}

		/// <summary>
		/// Helper method to convert a dictionary's keys to a HashSet for efficient lookups.
		/// </summary>
		private static HashSet<string> DictionaryKeysToHashSet(Dictionary<string, (string relativePath, string hash)> dictionary)
		{
			return new HashSet<string>(dictionary.Keys);
		}

		public static byte[] StreamFileChunksIntoMemory(string sourceFilePath)
		{
			try
			{
				using (FileStream sourceFileStream = File.OpenRead(sourceFilePath))
				{
					using (MemoryStream memoryStream = new MemoryStream())
					{
						sourceFileStream.CopyTo(memoryStream);
						return memoryStream.ToArray();
					}
				}
			}
			catch (Exception ex)
			{
				Log.Error("Patcher", $"Error streaming file chunks from '{sourceFilePath}' to memory: {ex.Message}");
				throw;
			}
		}

		/// <summary>
		/// Recursively scans a directory and its subdirectories to get all file paths and their SHA256 hashes.
		/// Files and directories specified in the 'ignoredExtensions' and 'ignoredDirectories' sets are skipped.
		/// This method handles common file system access errors gracefully.
		/// </summary>
		/// <param name="rootDirectory">The root directory to start scanning from.</param>
		/// <param name="ignoredExtensions">A set of file extensions (e.g., ".log", ".tmp") to ignore.</param>
		/// <param name="ignoredDirectories">A set of directory names (e.g., "Temp", "Debug") to ignore.</param>
		/// <returns>A dictionary where the key is the relative file path (e.g., "Data/file.dat") and the value is a tuple of (relativePath, hash).</returns>
		public static Dictionary<string, (string relativePath, string hash)> GetAllFilesWithHashes(string rootDirectory, HashSet<string> ignoredExtensions, HashSet<string> ignoredDirectories)
		{
			Dictionary<string, (string, string)> filesWithHashes = new Dictionary<string, (string, string)>();
			Stack<string> directories = new Stack<string>();

			// Normalize rootDirectory to ensure it ends with a directory separator.
			// This makes substring calculations for relative paths consistent.
			string normalizedRootDirectory = Path.GetFullPath(rootDirectory);
			if (!normalizedRootDirectory.EndsWith(Path.DirectorySeparatorChar.ToString()) &&
				!normalizedRootDirectory.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
			{
				normalizedRootDirectory += Path.DirectorySeparatorChar;
			}

			directories.Push(normalizedRootDirectory); // Start the traversal from the normalized root directory.

			using (var sha256 = SHA256.Create())
			{
				while (directories.Count > 0)
				{
					string currentDir = directories.Pop();

					// Get the actual directory name, trimming any trailing slashes for accurate comparison with ignoredDirectories.
					string currentDirName = Path.GetFileName(currentDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

					// Skip the current directory if its name is in the ignored list,
					// but explicitly ensure the initial rootDirectory itself is always processed.
					if (ignoredDirectories.Contains(currentDirName) && !currentDir.Equals(normalizedRootDirectory, StringComparison.OrdinalIgnoreCase))
					{
						Log.Info("Patcher", $"Skipping ignored directory and its contents: {currentDirName} (Full Path: {currentDir})");
						continue; // Move to the next directory in the stack.
					}

					try
					{
						string[] currentFiles = Directory.GetFiles(currentDir);
						foreach (string file in currentFiles)
						{
							string extension = Path.GetExtension(file);
							if (ignoredExtensions.Contains(extension))
							{
								Log.Info("Patcher", $"\tSkipping file by extension: {Path.GetFileName(file)} ({extension}) in {currentDir}");
								continue;
							}

							// Calculate relative path based on the normalized root directory.
							string relativePath = file.Substring(normalizedRootDirectory.Length);
							relativePath = relativePath.Replace('\\', '/'); // Standardize path separators for manifest.

							//Log.Debug("Patcher", $"\tProcessing file: '{file}' -> Relative Path: '{relativePath}'");

							string fileHash = ComputeFileHash(file, sha256);
							filesWithHashes.Add(relativePath, (relativePath, fileHash));
						}
					}
					catch (UnauthorizedAccessException)
					{
						Log.Warning("Patcher", $"Access to directory '{currentDir}' is denied. Skipping files in this directory.");
						// Continue to process subdirectories if possible, don't 'continue' the while loop here.
					}
					catch (DirectoryNotFoundException)
					{
						Log.Warning("Patcher", $"Directory '{currentDir}' not found. Skipping.");
						continue; // This directory is gone, move to the next.
					}
					catch (IOException ex)
					{
						Log.Warning("Patcher", $"IO error accessing files in '{currentDir}': {ex.Message}. Skipping.");
						continue; // Critical IO error for this directory, move to the next.
					}

					try
					{
						string[] subdirectories = Directory.GetDirectories(currentDir);
						foreach (string subdir in subdirectories)
						{
							// Check if subdirectory name should be ignored before pushing to stack.
							string subDirName = Path.GetFileName(subdir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
							if (ignoredDirectories.Contains(subDirName))
							{
								Log.Info("Patcher", $"Skipping ignored subdirectory: {subDirName} (Full Path: {subdir})");
								continue; // Skip this subdirectory and its contents.
							}
							directories.Push(subdir); // Add subdirectory to the stack for later processing.
						}
					}
					catch (UnauthorizedAccessException)
					{
						Log.Warning("Patcher", $"Access to subdirectory '{currentDir}' is denied. Skipping.");
						continue;
					}
					catch (DirectoryNotFoundException)
					{
						Log.Warning("Patcher", $"Subdirectory '{currentDir}' not found. Skipping.");
						continue;
					}
				}
			}
			return filesWithHashes;
		}

		private static string ComputeFileHash(string filePath, SHA256 sha256)
		{
			using (var stream = File.OpenRead(filePath))
			{
				byte[] hashBytes = sha256.ComputeHash(stream);
				return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
			}
		}
	}
}