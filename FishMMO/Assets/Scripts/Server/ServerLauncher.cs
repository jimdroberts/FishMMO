using UnityEngine;
using UnityEngine.SceneManagement;
using System;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FishMMO.Server
{
	public class ServerLauncher : MonoBehaviour
	{
		public Server Server { get; private set; }

		void Start()
		{
			string[] args = Environment.GetCommandLineArgs();
			if (args == null || args.Length < 2)
			{
				Close();
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
		}

		private void Close()
		{
			Debug.Log("ServerLauncher: Unknown server type. Available servers {Login, World, Scene}");
			Server.Quit();
		}

		private void Initialize(string bootstrapSceneName)
		{
#if UNITY_SERVER && !UNITY_EDITOR
			Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
#endif
			SceneManager.LoadScene(bootstrapSceneName, LoadSceneMode.Single);
		}
	}
}