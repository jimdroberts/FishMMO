using UnityEngine;
using UnityEditor;
using System.Diagnostics;
using System.IO;
using Debug = UnityEngine.Debug;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace FishMMO.Shared
{
	public class DotNetBuilder : EditorWindow
	{
		[SerializeField]
		private List<DotNetBuildSettings> settingsList = new List<DotNetBuildSettings>();

		private Vector2 scrollPosition;
		private string logOutput = "";

		[MenuItem("FishMMO/Tools/DotNet Builder")]
		public static void ShowWindow()
		{
			GetWindow<DotNetBuilder>("DotNet Builder");
		}

		private void OnGUI()
		{
			EditorGUILayout.LabelField("Build Settings Assets:", EditorStyles.boldLabel);

			EditorGUI.BeginChangeCheck();
			for (int i = 0; i < this.settingsList.Count; i++)
			{
				EditorGUILayout.BeginHorizontal();
				this.settingsList[i] = (DotNetBuildSettings)EditorGUILayout.ObjectField($"Setting {i + 1}", this.settingsList[i], typeof(DotNetBuildSettings), false);
				if (GUILayout.Button("Remove", GUILayout.Width(60)))
				{
					this.settingsList.RemoveAt(i);
					GUIUtility.ExitGUI();
				}
				EditorGUILayout.EndHorizontal();
			}

			EditorGUILayout.BeginHorizontal();
			if (GUILayout.Button("Add New Setting Slot"))
			{
				this.settingsList.Add(null);
			}
			if (GUILayout.Button("Create & Add New Settings Asset"))
			{
				DotNetBuildSettings newSettings = CreateNewSettingsAssetInstance();
				this.settingsList.Add(newSettings);
			}
			EditorGUILayout.EndHorizontal();

			if (EditorGUI.EndChangeCheck())
			{
				this.logOutput = "";
			}

			EditorGUILayout.Space(20);

			if (GUILayout.Button("Build All Selected Libraries"))
			{
				BuildAllAndLog();
			}

			EditorGUILayout.Space(10);

			EditorGUILayout.LabelField("Build Output:", EditorStyles.boldLabel);
			this.scrollPosition = EditorGUILayout.BeginScrollView(this.scrollPosition, GUILayout.Height(800));
			EditorGUILayout.TextArea(this.logOutput, GUI.skin.textArea);
			EditorGUILayout.EndScrollView();
		}

		private DotNetBuildSettings CreateNewSettingsAssetInstance()
		{
			DotNetBuildSettings newSettings = CreateInstance<DotNetBuildSettings>();
			string path = "Assets/NewDotNetBuildSettings.asset";
			path = AssetDatabase.GenerateUniqueAssetPath(path);
			AssetDatabase.CreateAsset(newSettings, path);
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
			Debug.Log($"Created new DotNetBuildSettings asset at: {path}");
			return newSettings;
		}

		private async void BuildAllAndLog()
		{
			this.logOutput = "";

			if (this.settingsList == null || this.settingsList.Count == 0)
			{
				AddLogError("No DotNet Build Settings assets selected. Please add at least one to the list.");
				return;
			}

			List<DotNetBuildSettings> settingsToBuild = this.settingsList.ToList();

			foreach (var settings in settingsToBuild)
			{
				if (settings == null)
				{
					AddLogWarning("Skipping null settings slot in the list.");
					continue;
				}

				AddLog($"--- Starting Build for: {settings.name} ---");

				if (settings.PerformCleanBeforeBuild)
				{
					AddLog($"Performing clean for '{settings.name}'...");
					bool cleanSuccess = await ExecuteDotNetCommand(settings, "clean");
					if (!cleanSuccess)
					{
						AddLogError($"Clean for '{settings.name}' FAILED! Skipping build.");
						Repaint(); // Repaint after failed clean
						continue;
					}
					AddLog($"Clean for '{settings.name}' completed successfully.");
				}

				bool buildSuccess = await ExecuteDotNetCommand(settings, "build");

				if (buildSuccess)
				{
					AddLog($"Build for '{settings.name}' completed successfully!");
				}
				else
				{
					AddLogError($"Build for '{settings.name}' FAILED!");
				}
			}

			AddLog("\nAll requested builds finished. Refreshing Unity AssetDatabase...");
			AssetDatabase.Refresh();
			AddLog("Unity AssetDatabase refreshed.");
		}

		/// <summary>
		/// Executes either 'dotnet build' or 'dotnet clean' based on the command parameter.
		/// </summary>
		/// <param name="settings">The DotNetBuildSettings asset to use for this command.</param>
		/// <param name="command">The dotnet command to execute ("build" or "clean").</param>
		/// <returns>True if the command was successful, false otherwise.</returns>
		private async Task<bool> ExecuteDotNetCommand(DotNetBuildSettings settings, string command)
		{
			string absoluteCsprojPath = settings.GetAbsoluteCsprojPath();

			string commonArguments = $"\"{absoluteCsprojPath}\" " +
									 $"--configuration {settings.BuildConfiguration} " +
									 $"--framework {settings.TargetFramework}";

			string specificArguments = "";
			if (command == "build")
			{
				specificArguments = settings.SkipDotNetRestore ? " --no-restore" : "";

				if (!settings.UseDefaultOutputPath && !string.IsNullOrEmpty(settings.OutputDirectory))
				{
					string absoluteOutputDirectory = settings.GetAbsoluteOutputDirectory();
					if (!string.IsNullOrWhiteSpace(absoluteOutputDirectory))
					{
						// Create directory only if it doesn't exist
						if (!Directory.Exists(absoluteOutputDirectory))
						{
							Directory.CreateDirectory(absoluteOutputDirectory);
							AddLog($"Created output directory: {absoluteOutputDirectory}");
						}
						specificArguments += $" --output \"{absoluteOutputDirectory}\"";
					}
				}
			}

			string fullArguments = $"{command} {commonArguments}{specificArguments}";

			if (!File.Exists(absoluteCsprojPath))
			{
				AddLogError($"Error: Project file not found at '{absoluteCsprojPath}' for command '{command}' on settings '{settings.name}'");
				return false;
			}

			AddLog($"Executing: {settings.DotnetExecutablePath} {fullArguments}");

			using (Process process = new Process())
			{
				process.StartInfo.FileName = settings.DotnetExecutablePath;
				process.StartInfo.Arguments = fullArguments;
				process.StartInfo.UseShellExecute = false;
				process.StartInfo.CreateNoWindow = true;
				process.StartInfo.RedirectStandardOutput = true;
				process.StartInfo.RedirectStandardError = true;

				try
				{
					process.Start();

					// Asynchronously read all output and error streams
					var outputTask = process.StandardOutput.ReadToEndAsync();
					var errorTask = process.StandardError.ReadToEndAsync();

					// Wait for both streams to be fully read and the process to exit
					await Task.WhenAll(outputTask, errorTask);
					process.WaitForExit(); // Ensure the process has exited before checking ExitCode

					string output = outputTask.Result;
					string error = errorTask.Result;

					// Log the collected output and errors to Unity's console and the custom log area
					if (!string.IsNullOrEmpty(output))
					{
						AddLogInternal(output);
					}
					if (!string.IsNullOrEmpty(error))
					{
						AddLogErrorInternal(error);
					}

					return process.ExitCode == 0;
				}
				catch (System.Exception ex)
				{
					AddLogException(ex);
					return false;
				}
			}
		}

		private void AddLogInternal(string message)
		{
			Debug.Log(message);
			this.logOutput += message + "\n";
		}

		private void AddLogErrorInternal(string message)
		{
			Debug.LogError(message);
			this.logOutput += "<color=red>" + message + "</color>\n";
		}

		private void AddLogWarningInternal(string message)
		{
			Debug.LogWarning(message);
			this.logOutput += "<color=orange>" + message + "</color>\n";
		}

		private void AddLog(string message)
		{
			AddLogInternal(message);
		}

		private void AddLogError(string message)
		{
			AddLogErrorInternal(message);
		}

		private void AddLogWarning(string message)
		{
			AddLogWarningInternal(message);
		}

		private void AddLogException(System.Exception ex)
		{
			Debug.LogException(ex);
			this.logOutput += "<color=red>EXCEPTION: " + ex.Message + "</color>\n";
		}
	}
}