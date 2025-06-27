using System.Collections.Generic;

namespace FishMMO.Shared
{
	public class MainBootstrapSystem : BootstrapSystem
	{
		public override void OnPreload()
		{
			Log.CurrentLogLevel = LogLevel.Debug;
			Log.SetDisableDefaultUnityLoggerInBuilds(true);
			Log.SetFileLoggingEnabled(false);

			//Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
#if UNITY_SERVER
#region Server
			List<AddressableSceneLoadData> initialScenes = new List<AddressableSceneLoadData>()
			{
				new AddressableSceneLoadData("ServerLauncher"),
			};
#endregion
#else
			#region Client
			// Initialize the client bootstrap scenes.
			List<AddressableSceneLoadData> initialScenes = new List<AddressableSceneLoadData>()
			{
				new AddressableSceneLoadData("ClientPreboot"),
			};
			#endregion
#endif
			AddressableLoadProcessor.EnqueueLoad(initialScenes);
		}
	}
}