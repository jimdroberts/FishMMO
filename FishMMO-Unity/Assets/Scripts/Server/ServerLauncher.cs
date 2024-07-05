using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.Runtime.CompilerServices;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FishMMO.Server
{
	public class ServerLauncher : MonoBehaviour
	{
		public string[] BootList = new string[]
		{
			"LoginServer",
			"WorldServer",
			"SceneServer",
			"ClientBootstrap"
		};

		void Start()
		{
#if !UNITY_EDITOR
			string[] args = Environment.GetCommandLineArgs();
			if (args == null || args.Length < 2)
			{
#endif
				bool tryInit = Initialize(BootList);
				if (!tryInit)
				{
					// otherwise we close the application
					Close();
				}
#if !UNITY_EDITOR
			}
			else
			{
				switch (args[1].ToUpper())
				{
					case "LOGIN":
						Initialize("LoginServer");
						break;
					case "WORLD":
						Initialize("WorldServer");
						break;
					case "SCENE":
						Initialize("SceneServer");
						break;
					default:
						Close();
						break;
				}
			}
#endif
		}

		private void Close()
		{
			Debug.Log("ServerLauncher: Unknown server type. Available servers {Login, World, Scene}");
			Server.Quit();
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void Initialize(string bootstrapSceneName)
		{
#if UNITY_SERVER && !UNITY_EDITOR
			Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
#endif
			SceneManager.LoadScene(bootstrapSceneName, LoadSceneMode.Single);
		}

		private bool Initialize(string[] bootstrapSceneNames)
		{
			bool loaded = false;
#if UNITY_EDITOR
			EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
			foreach (string boostrapSceneName in bootstrapSceneNames)
			{
				foreach (EditorBuildSettingsScene scene in scenes)
				{
					if (scene.enabled &&
						scene.path.Contains(boostrapSceneName))
					{
						UnityEditor.SceneManagement.EditorSceneManager.LoadScene(scene.path, LoadSceneMode.Additive);
						loaded = true;
					}
				}
			}
#elif UNITY_SERVER
			foreach (string bootstrapSceneName in bootstrapSceneNames)
			{
				Scene scene = SceneManager.GetSceneByName(bootstrapSceneName);
				if (scene != null)
				{
					Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);

					SceneManager.LoadScene(bootstrapSceneName, LoadSceneMode.Additive);
					loaded = true;
				}
			}
#endif
			return loaded;
		}
	}
}