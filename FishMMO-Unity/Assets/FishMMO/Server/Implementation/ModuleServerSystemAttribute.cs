using System;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Declares a module's server system in code: the server whose scene is <see cref="ServerScene"/> creates one at start
	/// and runs it with its other systems.
	/// </summary>
	/// <remarks>
	/// The asset route (a system asset labelled <c>&lt;server scene&gt;_Systems</c>) still works and wins over this one; use
	/// the declaration for a system that has nothing to author, so a module needs no asset or Addressables entry for it.
	/// <code>[assembly: ModuleServerSystem(typeof(MyModuleSystem), "SceneServer")]</code>
	/// </remarks>
	[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
	public sealed class ModuleServerSystemAttribute : Attribute
	{
		/// <summary>The scene server's scene name.</summary>
		public const string SceneServer = "SceneServer";

		/// <summary>The system type, a non-abstract <see cref="ServerBehaviour"/>.</summary>
		public Type SystemType { get; }

		/// <summary>The scene of the server that runs it (<see cref="SceneServer"/> for the scene server).</summary>
		public string ServerScene { get; }

		public ModuleServerSystemAttribute(Type systemType, string serverScene)
		{
			SystemType = systemType;
			ServerScene = serverScene;
		}
	}
}
