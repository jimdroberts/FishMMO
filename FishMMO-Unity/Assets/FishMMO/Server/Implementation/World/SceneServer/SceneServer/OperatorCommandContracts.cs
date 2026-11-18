using FishNet.Connection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FishMMO.Auth.Core;
using FishMMO.Database;
using FishMMO.Logging;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>One operator sub-command: what it is called, how it is described, and what runs. Modules add theirs through an <see cref="OperatorCommandProvider"/>.</summary>
	public sealed class OperatorCommand
	{
		/// <summary>The word typed after the set's command.</summary>
		public string Name;

		/// <summary>Other words accepted for it. Not shown in the console.</summary>
		public string[] Aliases = Array.Empty<string>();

		/// <summary>Group for help and the console.</summary>
		public string Category;

		/// <summary>One line saying what it does.</summary>
		public string Summary;

		/// <summary>Argument spec, in the grammar documented on <see cref="StaffCommandEntry"/>.</summary>
		public string Arguments = string.Empty;

		/// <summary>The first argument is a character, so the console may offer this on a roster row.</summary>
		public bool RosterAction;

		/// <summary>The first argument is a ticket number, so the console may offer this on a ticket.</summary>
		public bool TicketAction;

		/// <summary>The console confirms before sending.</summary>
		public bool Destructive;

		/// <summary>The handler: the caller and the argument text after the sub-command word.</summary>
		public Action<IPlayerCharacter, string> Run;
	}

	/// <summary>
	/// What an operator command handler borrows from the scene server: the replies, and the async runner that answers
	/// on the main thread. <c>SceneServerSystem</c> implements it; a module's commands get it from their provider.
	/// </summary>
	public interface IOperatorCommandHost
	{
		/// <summary>Sends one line to the operator on the system channel.</summary>
		void Reply(IPlayerCharacter character, string text);

		/// <summary>Sends several lines, in order.</summary>
		void ReplyLines(IPlayerCharacter character, IEnumerable<string> lines);

		/// <summary>Runs <paramref name="action"/> off the main thread and replies with the lines it returns.</summary>
		void RunOperatorLines(IPlayerCharacter character, Func<Task<IReadOnlyList<string>>> action);

		/// <summary>A server system on this scene server, by its contract.</summary>
		bool TryGetSystem<T>(out T system) where T : class, FishMMO.Server.Core.IServerBehaviour;
	}

	/// <summary>
	/// A module's <c>/gm</c> and <c>/admin</c> sub-commands, added to the sets when they are built. Declared with
	/// <c>[assembly: OperatorCommandProvider(typeof(...))]</c>, so a removed module's commands go with it.
	/// </summary>
	public abstract class OperatorCommandProvider
	{
		/// <summary>Sub-commands for game masters (<c>/gm</c>); administrators reach them too.</summary>
		public virtual IEnumerable<OperatorCommand> GameMasterCommands(IOperatorCommandHost host) => Array.Empty<OperatorCommand>();

		/// <summary>Sub-commands for administrators only (<c>/admin</c>).</summary>
		public virtual IEnumerable<OperatorCommand> AdministratorCommands(IOperatorCommandHost host) => Array.Empty<OperatorCommand>();
	}

	/// <summary>Declares an assembly's <see cref="OperatorCommandProvider"/>.</summary>
	[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
	public sealed class OperatorCommandProviderAttribute : Attribute
	{
		/// <summary>The provider's type; it needs a public parameterless constructor.</summary>
		public Type ProviderType { get; }

		public OperatorCommandProviderAttribute(Type providerType)
		{
			ProviderType = providerType;
		}
	}

	/// <summary>Every installed module's <see cref="OperatorCommandProvider"/>, found once in the loaded assemblies.</summary>
	public static class OperatorCommandProviders
	{
		private static List<OperatorCommandProvider> all;

		/// <summary>The providers, in assembly then declaration order.</summary>
		public static IReadOnlyList<OperatorCommandProvider> All
		{
			get
			{
				if (all != null)
				{
					return all;
				}
				var found = new List<OperatorCommandProvider>();
				var seen = new HashSet<Type>();
				foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
				{
					object[] attributes;
					try
					{
						attributes = assembly.GetCustomAttributes(typeof(OperatorCommandProviderAttribute), false);
					}
					catch (Exception)
					{
						continue;
					}
					foreach (OperatorCommandProviderAttribute attribute in attributes)
					{
						Type type = attribute.ProviderType;
						if (type == null || type.IsAbstract || !typeof(OperatorCommandProvider).IsAssignableFrom(type) || !seen.Add(type))
						{
							continue;
						}
						try
						{
							found.Add((OperatorCommandProvider)Activator.CreateInstance(type));
						}
						catch (Exception ex)
						{
							Log.Error("OperatorCommands", $"Could not create the operator command provider {type.FullName}: {ex.Message}");
						}
					}
				}
				all = found;
				return all;
			}
		}
	}
}
