using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishMMO.Client
{
	/// <summary>
	/// Puts a pipeline-state recording (<see cref="GraphicsStateTrace"/>) where the build ships it: a
	/// <see cref="GraphicsStateCollection"/> asset in <see cref="Folder"/>, merged into the one already there for the same
	/// platform, graphics API and quality level, and an Addressables entry in <see cref="Group"/> at
	/// <see cref="GraphicsStateWarmup.AddressFor"/> (<see cref="GraphicsStateWarmup"/> loads it from there).
	/// </summary>
	/// <remarks>
	/// <b>Why an asset, not the file.</b> A recording names each shader by its asset GUID. In a player, loading the file
	/// (GraphicsStateCollection.LoadFromJson) finds only shaders built into the player, never one in an asset bundle, which
	/// is every FishMMO shader. As an asset in the shaders' own group its references are real references, resolved by the
	/// bundle that holds the shaders (a shared one is isolated at build time, never copied), so the states it creates are
	/// the ones the game draws with.
	/// </remarks>
	public static class GraphicsStateRecordings
	{
		/// <summary>Where the recordings live in the project.</summary>
		public const string Folder = "Assets/FishMMO/Client/Rendering/GraphicsStates";

		/// <summary>The Addressables group: the resident client group that holds the shaders (the smart-group rule for .graphicsstate).</summary>
		public const string Group = "Client_Static_Permanent";

		[MenuItem("FishMMO/Graphics States/Add Recording…")]
		private static void AddFromMenu()
		{
			string file = EditorUtility.OpenFilePanel("Pipeline-state recording", "", "json,graphicsstate");
			if (string.IsNullOrEmpty(file))
			{
				return;
			}
			EditorUtility.DisplayDialog("Graphics states", Add(file), "OK");
		}

		/// <summary>
		/// Merges the recording at <paramref name="file"/> into <see cref="Folder"/>'s asset for its platform, API and quality
		/// level (created when there is none) and makes sure that asset is addressable. Returns what was done.
		/// </summary>
		public static string Add(string file)
		{
			var recording = new GraphicsStateCollection();
			if (!recording.LoadFromJson(File.ReadAllText(file)))
			{
				return $"{file} is not a pipeline-state recording.";
			}
			string name = GraphicsStateWarmup.NameFor(recording.runtimePlatform.ToString(), recording.graphicsDeviceType.ToString(), recording.qualityLevelName);
			string assetPath = $"{Folder}/{name}.graphicsstate";
			var merged = new GraphicsStateCollection();
			int before = 0;
			if (File.Exists(assetPath) && merged.LoadFromFile(assetPath))
			{
				before = merged.totalGraphicsStateCount;
				merged.Append(recording);
			}
			else
			{
				merged = recording;
			}
			Directory.CreateDirectory(Folder);
			if (!merged.SaveToFile(Path.GetFullPath(assetPath)))
			{
				return $"Saving {assetPath} failed.";
			}
			AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
			if (AssetDatabase.LoadAssetAtPath<GraphicsStateCollection>(assetPath) == null)
			{
				return $"{assetPath} was saved but did not import as a graphics state collection.";
			}
			string entry = Register(assetPath, name);
			string what = $"{name}: {merged.variantCount} shader variants, {merged.totalGraphicsStateCount} states ({merged.totalGraphicsStateCount - before} new); {entry}.";
			Debug.Log($"[Graphics states] {what}");
			return what;
		}

		/// <summary>Makes <paramref name="assetPath"/> an entry of <see cref="Group"/> at its warm-up address.</summary>
		private static string Register(string assetPath, string name)
		{
			AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
			AddressableAssetGroup group = settings != null ? settings.FindGroup(Group) : null;
			if (group == null)
			{
				return $"NOT addressable: no {Group} group";
			}
			string guid = AssetDatabase.AssetPathToGUID(assetPath);
			AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, group, false, false);
			entry.SetAddress(GraphicsStateWarmup.AddressFor(name), false);
			settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true);
			AssetDatabase.SaveAssets();
			return $"addressable in {Group} at {entry.address}";
		}

		/// <summary>Batch: <c>-executeMethod FishMMO.Client.GraphicsStateRecordings.AddBatch -graphicsStateFile &lt;path&gt;</c>.</summary>
		public static void AddBatch()
		{
			string[] args = System.Environment.GetCommandLineArgs();
			for (int i = 0; i < args.Length - 1; i++)
			{
				if (args[i] == "-graphicsStateFile")
				{
					string result = Add(args[i + 1]);
					Debug.Log(result);
					if (Application.isBatchMode)
					{
						EditorApplication.Exit(result.Contains("addressable in") ? 0 : 1);
					}
					return;
				}
			}
			Debug.LogError("[Graphics states] -graphicsStateFile <path> is missing.");
			if (Application.isBatchMode)
			{
				EditorApplication.Exit(1);
			}
		}
	}
}
