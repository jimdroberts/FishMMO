using System;
using System.IO;
using System.Linq;
using System.Text;
using YamlDotNet.Serialization;
using FishMMO.Logging;

public static class UnityAssetReader
{
	private const string VersionConfigFileName = "VersionConfig.asset";
	private const string TargetTypeNameInYaml = "VersionConfig";

	/// <summary>
	/// Searches a given directory and its subdirectories for the VersionConfig.asset file
	/// and attempts to parse its version.
	/// </summary>
	/// <param name="clientDirectory">The root directory of the Unity client build.</param>
	/// <returns>The parsed VersionConfig object if found and valid, otherwise null.</returns>
	public static VersionConfig GetVersionFromClientDirectory(string clientDirectory)
	{
		if (!Directory.Exists(clientDirectory))
		{
			Log.Error("UnityAssetReader", $" Client directory '{clientDirectory}' does not exist.");
			return null;
		}

		Log.Info("UnityAssetReader", $"Searching for '{VersionConfigFileName}' in '{clientDirectory}'...");

		string versionAssetPath = null;

		// 1. Look for the "<YourProjectName>_Data" folder dynamically
		if (versionAssetPath == null)
		{
			Log.Info("UnityAssetReader", $"'{VersionConfigFileName}' not found in direct paths. Searching for '*_Data' directories...");

			// Find directories ending with "_Data" (e.g., "MyGame_Data")
			// This is typically the build output folder for the game's data.
			string[] dataDirectories = Directory.GetDirectories(clientDirectory, "*_Data", SearchOption.TopDirectoryOnly);

			foreach (string dataDir in dataDirectories)
			{
				// Now, search within potential subfolders inside the _Data directory
				// Common places for ScriptableObjects in builds are StreamingAssets (if Addressables/Resources)
				// or sometimes directly in the _Data folder or deeper if not Addressables.
				string[] potentialDataSubPaths = new string[]
				{
					dataDir,
					Path.Combine(dataDir, "StreamingAssets", "aa", "settings"), // Common Addressables path
                    Path.Combine(dataDir, "StreamingAssets"),                  // General StreamingAssets
                };

				foreach (string subPath in potentialDataSubPaths)
				{
					if (Directory.Exists(subPath))
					{
						try
						{
							versionAssetPath = Directory.GetFiles(subPath, VersionConfigFileName, SearchOption.AllDirectories)
													.FirstOrDefault();
							if (versionAssetPath != null)
							{
								Log.Info("UnityAssetReader", $"Found '{VersionConfigFileName}' in a *_Data directory: {versionAssetPath}");
								break; // Found it in a *_Data path
							}
						}
						catch (UnauthorizedAccessException)
						{
							Log.Info("UnityAssetReader", $"Warning: Access denied to subdirectory '{subPath}'. Skipping.");
						}
						catch (Exception ex)
						{
							Log.Info("UnityAssetReader", $"Error searching in '{subPath}': {ex.Message}");
						}
					}
				}
				if (versionAssetPath != null)
				{
					break; // Break outer loop if found
				}
			}
		}

		// 2. Fallback to a broader recursive search if still not found (least efficient)
		if (versionAssetPath == null)
		{
			Log.Info("UnityAssetReader", $"'{VersionConfigFileName}' not found in common or *_Data paths. Performing broader search in '{clientDirectory}'...");
			try
			{
				versionAssetPath = Directory.GetFiles(clientDirectory, VersionConfigFileName, SearchOption.AllDirectories)
										.FirstOrDefault();
			}
			catch (UnauthorizedAccessException)
			{
				Log.Info("UnityAssetReader", $"Warning: Access denied to a subdirectory within '{clientDirectory}'.");
			}
			catch (Exception ex)
			{
				Log.Info("UnityAssetReader", $"Error during broad search for '{VersionConfigFileName}': {ex.Message}");
			}
		}

		if (versionAssetPath == null)
		{
			Log.Error("UnityAssetReader", $" '{VersionConfigFileName}' not found in directory '{clientDirectory}' or its common subdirectories.");
			return null;
		}

		Log.Info("UnityAssetReader", $"Found VersionConfig.asset at: {versionAssetPath}");
		return ParseVersionConfigAsset(versionAssetPath);
	}

	/// <summary>
	/// Parses the YAML content of a Unity VersionConfig.asset file.
	/// </summary>
	/// <param name="assetPath">The full path to the .asset file.</param>
	/// <returns>A VersionConfig object or null if parsing fails.</returns>
	private static VersionConfig ParseVersionConfigAsset(string assetPath)
	{
		try
		{
			string yamlContent = File.ReadAllText(assetPath);
			// YamlDotNet's DeserializerBuilder can work with multiple documents,
			// but for Unity assets, it's often better to manually extract the
			// relevant block as the root element might be a generic "MonoBehaviour" or "ScriptableObject".

			using (var reader = new StringReader(yamlContent))
			{
				string line;
				bool inTargetBlock = false;
				StringBuilder targetBlockContent = new StringBuilder();
				int lineNumber = 0; // For debugging

				while ((line = reader.ReadLine()) != null)
				{
					lineNumber++;
					// Unity asset files start new documents with "--- !u!XYZ &ABCDEF"
					// where 114 is for MonoBehaviour/ScriptableObject.
					if (line.Trim().StartsWith("--- !u!114 &"))
					{
						// If we were already in a block, and found a new one, the previous block ended.
						// For our purpose, we only expect one VersionConfig per file, so we can break.
						if (inTargetBlock && targetBlockContent.Length > 0)
						{
							break; // Stop after finding the first potential VersionConfig block
						}
						inTargetBlock = true; // Mark that we are potentially in the VersionConfig block
						targetBlockContent.Clear(); // Clear any previous content
						continue; // Skip the document header line itself
					}

					if (inTargetBlock)
					{
						// Collect lines. We need to be careful about indentation.
						// The actual data fields (major, minor, etc.) are directly under the
						// ScriptableObject/MonoBehaviour entry.
						// Example structure in YAML for ScriptableObject:
						// --- !u!114 &11400000
						// MonoBehaviour:
						//   m_ObjectHideFlags: 0
						//   m_CorrespondingSourceObject: {fileID: 0}
						//   m_PrefabInstance: {fileID: 0}
						//   major: 1
						//   minor: 0
						//   patch: 0
						//   preRelease: ""
						//   buildMetadata: ""

						// We can just append all lines until the next '--- !u!' or end of file.
						// YamlDotNet should be able to parse the 'major', 'minor' etc., directly
						// even with the Unity-specific 'm_ObjectHideFlags', 'm_Script' etc.,
						// as long as our `VersionConfig` class only defines the fields it cares about.
						targetBlockContent.AppendLine(line);
					}
				}

				if (targetBlockContent.Length > 0)
				{
					string processedContent = targetBlockContent.ToString();

					// YamlDotNet needs the root element to be directly parsable by VersionConfig.
					// If the first line is "MonoBehaviour:" (or "ScriptableObject:"), remove it and its indent.
					var lines = processedContent.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
					if (lines.Length > 0)
					{
						string firstLineTrimmed = lines[0].Trim();
						if (firstLineTrimmed.StartsWith("MonoBehaviour:") || firstLineTrimmed.StartsWith("ScriptableObject:"))
						{
							processedContent = string.Join(Environment.NewLine, lines.Skip(1));
						}
					}

					processedContent = processedContent.Trim(); // Trim any leading/trailing whitespace/empty lines

					if (!string.IsNullOrWhiteSpace(processedContent))
					{
						var deserializerForContent = new DeserializerBuilder().Build();
						VersionConfig version = null;
						try
						{
							// YamlDotNet's default deserialization can handle ignoring unknown properties.
							// This will map 'major', 'minor', etc., if they exist in the YAML block.
							version = deserializerForContent.Deserialize<VersionConfig>(processedContent);
						}
						catch (Exception innerEx)
						{
							Log.Debug("UnityAssetReader", $"Failed to deserialize potential VersionConfig block.", innerEx);
							// This block might not be the VersionConfig, or it's malformed.
							// Continue searching or return null if this was the only candidate.
						}


						// A robust check: ensure at least one of our core fields has a value that isn't default 0,
						// to confirm it's likely our object and not some other empty scriptable object.
						// If it's valid VersionConfig, it should have at least major/minor/patch set.
						if (version != null && (version.Major >= 0 || version.Minor >= 0 || version.Patch >= 0)) // patch >= 0 allows 0.0.0
						{
							Log.Info("UnityAssetReader", $"Successfully parsed version: {version.FullVersion}");
							return version;
						}
					}
				}
			}

			Log.Error("UnityAssetReader", $" Could not find or parse valid '{TargetTypeNameInYaml}' data within '{assetPath}'.");
			return null;
		}
		catch (Exception ex)
		{
			Log.Info("UnityAssetReader", $"Error reading or parsing VersionConfig.asset at '{assetPath}': {ex.Message}");
			return null;
		}
	}
}