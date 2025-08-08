using UnityEngine;
using UnityEditor;
using System;
using System.Threading.Tasks;
using System.Threading;
using UnityEngine.Rendering;
using System.IO;

namespace MeshyAI
{
	public enum RenderPipeline
	{
		Standard,
		URP
	}

	/// <summary>
	/// Unity Editor window for generating 3D models from text prompts using the Meshy.ai API.
	/// <para>
	/// This class has been refactored to use dependency injection and SOLID principles.
	/// It now acts as a controller for the UI and workflow, delegating all business logic
	/// to the service interfaces: IMeshyApiService, IMeshyMaterialService, and IMeshyModelIO.
	/// </para>
	/// </summary>
	public class MeshyEditorWindow : EditorWindow
	{
		// === Dependencies ===
		private IMeshyApiService apiService;
		private IMeshyMaterialService materialService;
		private IMeshyModelIO modelIO;

		// === Meshy API Configuration ===
		private MeshySettings settings;

		// === Generation Settings ===
		private const int PREVIEW_TASK_COST = 5;
		private const int REFINE_TASK_COST = 10;

		private bool moderation = false;

		// === Rate Limiting Settings ===
		[Tooltip("The maximum number of requests allowed per second. Default is 20 for Pro/Studio tiers.")]
		private int requestsPerSecond = 20;

		// === Preview Task Parameters (using enums) ===
		private string prompt = "a simple low poly stylized tree";
		private ArtStyle artStyle = ArtStyle.realistic;
		private AIModel aiModel = AIModel.meshy_5;
		private int seed = 0;
		private Topology topology = Topology.triangle;
		private int targetPolycount = 1000;
		private SymmetryMode symmetryMode = SymmetryMode.auto;
		private RenderPipeline renderPipeline = RenderPipeline.URP;

		// === Texture Import/Assignment Toggles ===
		private bool enablePBR = false;
		private bool enableMetallic = false;
		private bool enableMetallicSmoothness = false;
		private bool enableNormal = true;
		private bool enableRoughness = true;

		// === Material Property Controls ===
		private float materialSmoothness = 0.0f;
		private float materialMetallic = 0.0f;

		// === Internal State Variables ===
		private int currentBalance = 0;
		private TaskStatus currentStatus = TaskStatus.Idle;
		private string statusMessage = "Ready to generate.";
		private Texture2D previewTexture;
		private string currentTaskId = null;
		private MeshyTaskDetailsResponse taskDetails = null;
		private CancellationTokenSource cancellationTokenSource;
		private float currentProgress = 0f;
		private string modelFileName = "new_model";
		private string modelDirectory = "Assets/Prefabs/Meshy/Models";

		// Ensure shouldRemesh is declared
		private bool shouldRemesh = true;

		/// <summary>
		/// Adds the Meshy 3D Generator window to the Unity Editor menu.
		/// </summary>
		[MenuItem("Meshy/Generate 3D Model")]
		public static void ShowEditorWindow()
		{
			GetWindow<MeshyEditorWindow>("Meshy 3D Generator");
		}

		/// <summary>
		/// Called when the editor window is enabled, gains focus, or is opened.
		/// Initializes the dependencies and loads settings.
		/// </summary>
		private void OnEnable()
		{
			// Initialize dependencies
			apiService = new MeshyApiService();
			materialService = new MeshyMaterialService();
			modelIO = new MeshyModelIO();
			cancellationTokenSource = new CancellationTokenSource();

			// Try to automatically load the settings ScriptableObject if it exists.
			string[] guids = AssetDatabase.FindAssets("t:MeshySettings");
			if (guids.Length > 0)
			{
				string path = AssetDatabase.GUIDToAssetPath(guids[0]);
				settings = AssetDatabase.LoadAssetAtPath<MeshySettings>(path);
			}

			UpdateBalance();

			// Auto-detect URP and default to URP shader if the project is using a URP pipeline
			try
			{
				var urpLit = Shader.Find("Universal Render Pipeline/Lit");
				if (GraphicsSettings.currentRenderPipeline != null && urpLit != null)
				{
					renderPipeline = RenderPipeline.URP;
				}
			}
			catch { /* ignore auto-detect issues */ }
		}

		/// <summary>
		/// Draws the Meshy 3D Generator editor window UI and handles user input.
		/// </summary>
		private void OnGUI()
		{
			GUILayout.Label("Meshy Text-to-3D Generator", EditorStyles.boldLabel);
			GUILayout.Space(10);

			// Settings Object Field
			settings = (MeshySettings)EditorGUILayout.ObjectField("Settings Object", settings, typeof(MeshySettings), false);
			if (settings == null)
			{
				EditorGUILayout.HelpBox("Please create a MeshySettings asset and assign it here.", MessageType.Warning);
			}

			// Button to import a completed taskDetails.json and enable Save 3D Model to File
			GUILayout.Space(10);
			if (GUILayout.Button("Import taskDetails.json"))
			{
				string jsonPath = EditorUtility.OpenFilePanel("Select taskDetails.json", Application.dataPath, "json");
				if (!string.IsNullOrEmpty(jsonPath) && File.Exists(jsonPath))
				{
					try
					{
						string json = File.ReadAllText(jsonPath);
						taskDetails = JsonUtility.FromJson<MeshyTaskDetailsResponse>(json);
						currentStatus = TaskStatus.RefineSucceeded;
						statusMessage = "Imported taskDetails.json. You can now save the model.";
						Repaint();
					}
					catch (Exception e)
					{
						Debug.LogError($"Failed to import taskDetails.json: {e.Message}");
						EditorUtility.DisplayDialog("Import Error", "Failed to import taskDetails.json. See console for details.", "OK");
					}
				}
			}

			// Display the current credit balance
			EditorGUILayout.LabelField("Current Balance:", currentBalance.ToString() + " credits");

			// New field to specify the file name for the saved model
			modelFileName = EditorGUILayout.TextField("Model File Name", modelFileName);

			GUILayout.Space(10);

			// Generation Parameters
			GUILayout.Label("Prompt", EditorStyles.boldLabel);
			prompt = EditorGUILayout.TextArea(prompt, GUILayout.MinHeight(50));
			artStyle = (ArtStyle)EditorGUILayout.EnumPopup("Art Style", artStyle);
			aiModel = (AIModel)EditorGUILayout.EnumPopup("AI Model", aiModel);
			seed = EditorGUILayout.IntField("Seed", seed);
			topology = (Topology)EditorGUILayout.EnumPopup("Topology", topology);
			targetPolycount = EditorGUILayout.IntField("Target Polycount", targetPolycount);
			shouldRemesh = EditorGUILayout.Toggle("Should Remesh", shouldRemesh);
			symmetryMode = (SymmetryMode)EditorGUILayout.EnumPopup("Symmetry Mode", symmetryMode);
			renderPipeline = (RenderPipeline)EditorGUILayout.EnumPopup("Render Pipeline", renderPipeline);

			// --- Texture Import/Assignment Settings ---
			GUILayout.Space(10);
			GUILayout.Label("Texture Import/Assignment Settings", EditorStyles.boldLabel);
			enablePBR = EditorGUILayout.Toggle("Enable PBR", enablePBR);
			enableMetallic = EditorGUILayout.Toggle("Enable Metallic", enableMetallic);
			enableMetallicSmoothness = EditorGUILayout.Toggle("Enable MetallicSmoothness (URP)", enableMetallicSmoothness);
			enableNormal = EditorGUILayout.Toggle("Enable Normal Map", enableNormal);
			enableRoughness = EditorGUILayout.Toggle("Enable Roughness", enableRoughness);

			// --- Material Property Sliders ---
			GUILayout.Space(10);
			GUILayout.Label("Material Properties", EditorStyles.boldLabel);
			materialSmoothness = EditorGUILayout.Slider("Material Smoothness", materialSmoothness, 0f, 1f);
			materialMetallic = EditorGUILayout.Slider("Material Metallic", materialMetallic, 0f, 1f);

			// Rate Limit Settings
			GUILayout.Space(10);
			GUILayout.Label("Rate Limit Settings", EditorStyles.boldLabel);
			requestsPerSecond = EditorGUILayout.IntField("Requests per Second", requestsPerSecond);

			GUILayout.Space(10);

			// Generation Control Buttons
			bool isProcessing = currentStatus == TaskStatus.Generating || currentStatus == TaskStatus.Refining || currentStatus == TaskStatus.StartingPreview || currentStatus == TaskStatus.Downloading;
			EditorGUI.BeginDisabledGroup(isProcessing || settings == null || string.IsNullOrEmpty(settings.meshyApiKey) || settings.meshyApiKey == "YOUR_API_KEY_HERE");
			if (GUILayout.Button($"Generate Preview ({PREVIEW_TASK_COST} credits)"))
			{
				StartGeneration();
			}
			EditorGUI.EndDisabledGroup();

			// Refine button: enabled only if preview is complete and not processing
			EditorGUI.BeginDisabledGroup(isProcessing || currentStatus != TaskStatus.PreviewSucceeded);
			if (GUILayout.Button($"Start Refine Task ({REFINE_TASK_COST} credits)"))
			{
				InitiateRefineTask(currentTaskId);
			}
			EditorGUI.EndDisabledGroup();

			// This button saves the model to a file
			EditorGUI.BeginDisabledGroup(currentStatus != TaskStatus.RefineSucceeded || taskDetails == null || taskDetails.model_urls == null);
			if (GUILayout.Button("Save 3D Model to File"))
			{
				StartDownloadAndSaveModelAssets();
			}
			EditorGUI.EndDisabledGroup();

			if (isProcessing)
			{
				if (GUILayout.Button("Cancel"))
				{
					CancelTask();
				}
			}

			// Added space for better UI separation
			GUILayout.Space(10);

			// Display the progress bar if a generation task is active
			if (isProcessing)
			{
				EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(false, 20), currentProgress / 100f, statusMessage);
			}
			else
			{
				GUILayout.Label(statusMessage);
			}

			if (previewTexture != null)
			{
				GUILayout.Space(10);
				float availableWidth = position.width;
				float availableHeight = GUILayoutUtility.GetLastRect().yMax;
				Rect textureRect = GUILayoutUtility.GetRect(availableWidth, availableHeight, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
				GUI.DrawTexture(textureRect, previewTexture, ScaleMode.ScaleToFit, false);
			}

			// Add a button for retry
			if (GUILayout.Button("Retry"))
			{
				currentStatus = TaskStatus.RefineSucceeded;
			}
		}

		private async void UpdateBalance()
		{
			if (settings == null || string.IsNullOrEmpty(settings.meshyApiKey) || settings.meshyApiKey == "YOUR_API_KEY_HERE")
			{
				currentBalance = 0;
				Repaint();
				return;
			}
			TaskStatus prevStatus = currentStatus;
			try
			{
				currentStatus = TaskStatus.CheckingBalance;
				statusMessage = "Checking credits...";
				cancellationTokenSource = new CancellationTokenSource();
				currentBalance = await apiService.GetBalanceAsync(settings.meshyApiKey, cancellationTokenSource.Token);
			}
			catch (Exception e)
			{
				Debug.LogError($"Error updating balance: {e.Message}");
				currentBalance = 0;
				statusMessage = "Failed to get balance.";
			}
			finally
			{
				currentStatus = prevStatus;
				Repaint();
			}
		}

		private async void StartGeneration()
		{
			if (currentStatus == TaskStatus.Generating || currentStatus == TaskStatus.Refining) return;

			// Reset state
			previewTexture = null;
			taskDetails = null;
			currentTaskId = null;
			currentProgress = 0f;

			try
			{
				UpdateStatus("Checking credits...", TaskStatus.CheckingBalance, 0);
				currentBalance = await apiService.GetBalanceAsync(settings.meshyApiKey, cancellationTokenSource.Token);
				if (currentBalance < PREVIEW_TASK_COST)
				{
					UpdateStatus("Insufficient credits for a preview task.", TaskStatus.Failed);
					return;
				}

				await PerformPreviewTask();
			}
			catch (OperationCanceledException)
			{
				UpdateStatus("Task was canceled by user.", TaskStatus.Canceled);
			}
			catch (Exception e)
			{
				UpdateStatus($"Error starting generation: {e.Message}", TaskStatus.Failed);
			}
		}

		private async Task PerformPreviewTask()
		{
			cancellationTokenSource = new CancellationTokenSource();
			UpdateStatus("Starting preview task...", TaskStatus.StartingPreview, 5);
			try
			{
				var request = new PreviewTaskRequest()
				{
					mode = "preview",
					prompt = this.prompt,
					art_style = this.artStyle.ToString().ToLower(),
					ai_model = GetMeshyModelString(aiModel),
					seed = this.seed,
					topology = this.topology.ToString().ToLower(),
					target_polycount = this.targetPolycount,
					moderation = this.moderation,
					should_remesh = this.shouldRemesh,
					symmetry_mode = this.symmetryMode.ToString().ToLower()
				};
				currentTaskId = await apiService.StartPreviewTaskAsync(settings.meshyApiKey, request, cancellationTokenSource.Token);
				if (currentTaskId != null)
				{
					await PollTaskStatus(currentTaskId, TaskStatus.Generating, TaskStatus.PreviewSucceeded);
				}
			}
			catch (OperationCanceledException)
			{
				UpdateStatus("Preview task was canceled by user.", TaskStatus.Canceled);
			}
			catch (Exception e)
			{
				UpdateStatus($"Error starting preview task: {e.Message}", TaskStatus.Failed);
			}
		}

		private async void InitiateRefineTask(string taskId)
		{
			try
			{
				UpdateStatus("Checking credits for refine task...", TaskStatus.CheckingBalance, 0);
				currentBalance = await apiService.GetBalanceAsync(settings.meshyApiKey, cancellationTokenSource.Token);
				if (currentBalance < REFINE_TASK_COST)
				{
					UpdateStatus("Insufficient credits for a refine task.", TaskStatus.Failed);
					return;
				}

				cancellationTokenSource = new CancellationTokenSource();
				UpdateStatus("Starting refine task...", TaskStatus.StartingRefine, 5);
				var request = new RefineTaskRequest()
				{
					mode = "refine",
					preview_task_id = taskId,
					enable_pbr = this.enablePBR,
					ai_model = GetMeshyModelString(this.aiModel),
					moderation = this.moderation
				};
				currentTaskId = await apiService.StartRefineTaskAsync(settings.meshyApiKey, request, cancellationTokenSource.Token);
				if (currentTaskId != null)
				{
					await PollTaskStatus(currentTaskId, TaskStatus.Refining, TaskStatus.RefineSucceeded);
				}
			}
			catch (OperationCanceledException)
			{
				UpdateStatus("Refine task was canceled by user.", TaskStatus.Canceled);
			}
			catch (Exception e)
			{
				UpdateStatus($"Error starting refine task: {e.Message}", TaskStatus.Failed);
			}
		}

		private async Task PollTaskStatus(string taskId, TaskStatus generatingStatus, TaskStatus successStatus)
		{
			while (!cancellationTokenSource.IsCancellationRequested)
			{
				try
				{
					taskDetails = await apiService.GetTaskDetailsAsync(settings.meshyApiKey, taskId, cancellationTokenSource.Token);
					currentProgress = taskDetails.progress;

					// Update status based on API response
					if (taskDetails.status == "SUCCEEDED")
					{
						if (taskDetails.thumbnail_url != null)
						{
							previewTexture = await apiService.DownloadTextureAsync(taskDetails.thumbnail_url, cancellationTokenSource.Token);
						}
						if (successStatus == TaskStatus.RefineSucceeded)
						{
							UpdateStatus("Generation successful! You can now save the 3D model.", TaskStatus.RefineSucceeded, 100);
						}
						else if (successStatus == TaskStatus.PreviewSucceeded)
						{
							UpdateStatus("Preview successful! Click 'Start Refine Task' to continue.", TaskStatus.PreviewSucceeded, 100);
						}
						break;
					}
					else if (taskDetails.status == "FAILED" || taskDetails.status == "EXPIRED" || taskDetails.status == "CANCELED")
					{
						UpdateStatus($"Task failed: {taskDetails.task_error?.message}", TaskStatus.Failed);
						break;
					}
					else
					{
						UpdateStatus($"{taskDetails.status}: {taskDetails.progress}%", generatingStatus, taskDetails.progress);
					}
				}
				catch (OperationCanceledException)
				{
					UpdateStatus("Task was canceled by user.", TaskStatus.Canceled);
					return;
				}
				catch (Exception e)
				{
					UpdateStatus($"Error polling task status: {e.Message}", TaskStatus.Failed);
					return;
				}

				await Task.Delay(1000, cancellationTokenSource.Token); // Poll every second
			}
			cancellationTokenSource?.Dispose();
			cancellationTokenSource = null;
			UpdateBalance();
		}

		private async void StartDownloadAndSaveModelAssets()
		{
			if (taskDetails == null || taskDetails.model_urls == null) return;

			cancellationTokenSource = new CancellationTokenSource();
			UpdateStatus("Downloading and saving model assets...", TaskStatus.Downloading, 0);
			try
			{
				// Ensure the directory exists
				modelIO.EnsureDirectory(modelDirectory);
				string modelPath = Path.Combine(modelDirectory, modelFileName);
				modelIO.EnsureDirectory(modelPath);
				string actualModelPath = Path.Combine(modelPath, modelFileName);

				// Save taskDetails as JSON
				string jsonPath = Path.Combine(modelPath, modelFileName + "_taskDetails.json");
				string taskDetailsJson = JsonUtility.ToJson(taskDetails, true);
				File.WriteAllText(jsonPath, taskDetailsJson);

				// Download the OBJ file and save it
				var www = await apiService.DownloadFileAsync(taskDetails.model_urls.obj, cancellationTokenSource.Token);
				await modelIO.SaveFileAsync(www, actualModelPath + ".obj");

				// Download and save the textures
				TextureSet textures = new TextureSet();
				if (taskDetails.texture_urls != null && taskDetails.texture_urls.Count > 0)
				{
					var textureUrls = taskDetails.texture_urls[0];
					if (!string.IsNullOrEmpty(textureUrls.base_color))
					{
						textures.baseColor = await apiService.DownloadTextureAsync(textureUrls.base_color, cancellationTokenSource.Token);
					}
					if (!string.IsNullOrEmpty(textureUrls.normal) && enableNormal)
					{
						textures.normal = await apiService.DownloadTextureAsync(textureUrls.normal, cancellationTokenSource.Token);
					}
					if (!string.IsNullOrEmpty(textureUrls.roughness) && enableRoughness)
					{
						textures.roughness = await apiService.DownloadTextureAsync(textureUrls.roughness, cancellationTokenSource.Token);
					}
					if (!string.IsNullOrEmpty(textureUrls.metallic) && enableMetallic)
					{
						textures.metallic = await apiService.DownloadTextureAsync(textureUrls.metallic, cancellationTokenSource.Token);
					}
				}

				// Save textures to disk as PNG files
				if (textures.baseColor != null)
				{
					var png = textures.baseColor.EncodeToPNG();
					File.WriteAllBytes(Path.Combine(modelPath, modelFileName + "_BaseColor.png"), png);
				}
				if (textures.normal != null)
				{
					var png = textures.normal.EncodeToPNG();
					File.WriteAllBytes(Path.Combine(modelPath, modelFileName + "_Normal.png"), png);
				}
				if (textures.roughness != null)
				{
					var png = textures.roughness.EncodeToPNG();
					File.WriteAllBytes(Path.Combine(modelPath, modelFileName + "_Roughness.png"), png);
				}
				if (textures.metallic != null)
				{
					var png = textures.metallic.EncodeToPNG();
					File.WriteAllBytes(Path.Combine(modelPath, modelFileName + "_Metallic.png"), png);
				}

				// Create or load the material and assign textures
				Material newMaterial = materialService.CreateOrLoadMaterial(modelPath, modelFileName, renderPipeline, materialMetallic, materialSmoothness);
				materialService.AssignTexturesToMaterial(newMaterial, textures, renderPipeline, materialMetallic, materialSmoothness);

				// Import the OBJ model
				modelIO.RefreshAssetDatabase();
				GameObject importedObject = AssetDatabase.LoadAssetAtPath<GameObject>(actualModelPath + ".obj");

				if (importedObject != null)
				{
					materialService.AssignMaterialToRenderers(importedObject, newMaterial);
					materialService.CreatePrefab(importedObject, actualModelPath + ".prefab");
				}

				UpdateStatus("Model saved and prefab created!", TaskStatus.RefineSucceeded, 100);
			}
			catch (OperationCanceledException)
			{
				UpdateStatus("Download was canceled by user.", TaskStatus.Canceled);
			}
			catch (Exception e)
			{
				UpdateStatus($"Error saving assets: {e.Message}", TaskStatus.Failed);
			}
		}

		private void CancelTask()
		{
			if (cancellationTokenSource != null)
			{
				cancellationTokenSource.Cancel();
				cancellationTokenSource.Dispose();
				cancellationTokenSource = null;
			}
			UpdateStatus("Task was canceled by user.", TaskStatus.Canceled);
		}

		private void UpdateStatus(string message, TaskStatus status, float progress = 0f)
		{
			currentStatus = status;
			statusMessage = message;
			currentProgress = progress;
			Repaint();
		}

		/// <summary>
		/// Returns the correct string for the Meshy AI model enum for API requests.
		/// </summary>
		private string GetMeshyModelString(AIModel model)
		{
			// Add more mappings if you add more models
			switch (model)
			{
				case AIModel.meshy_5:
					return "meshy-5";
				case AIModel.meshy_4:
					return "meshy-4";
				default:
					return model.ToString().Replace("_", "-").ToLower();
			}
		}
	}
}