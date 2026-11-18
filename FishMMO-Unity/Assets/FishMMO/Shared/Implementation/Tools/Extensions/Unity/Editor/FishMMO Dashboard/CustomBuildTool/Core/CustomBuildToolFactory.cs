#if UNITY_EDITOR
using FishMMO.Shared.CustomBuildTool.Addressables;
using FishMMO.Shared.CustomBuildTool.Config;
using FishMMO.Shared.CustomBuildTool.Execution;
using FishMMO.Shared.CustomBuildTool.Linker;

namespace FishMMO.Shared.CustomBuildTool.Core
{
	/// <summary>
	/// Factory for creating CustomBuildTool instances with proper dependency injection.
	/// Follows the Factory and Dependency Inversion principles.
	/// </summary>
	public static class CustomBuildToolFactory
	{
		/// <summary>
		/// Creates a new CustomBuildTool instance with all required dependencies.
		/// </summary>
		/// <returns>A fully configured CustomBuildTool instance.</returns>
		public static CustomBuildTool Create()
		{
			IBuildConfigurator configurator = new BuildConfigurator();
			IBuildExecutor executor = new BuildExecutor();
			ILinkerGenerator linkerGenerator = new LinkerGenerator();
			IAddressableManager addressableManager = new AddressableManager();

			return new CustomBuildTool(configurator, executor, linkerGenerator, addressableManager);
		}

		/// <summary>
		/// Creates a BuildConfigurator instance.
		/// </summary>
		public static IBuildConfigurator CreateConfigurator()
		{
			return new BuildConfigurator();
		}

		/// <summary>
		/// Creates an AddressableManager instance.
		/// </summary>
		public static IAddressableManager CreateAddressableManager()
		{
			return new AddressableManager();
		}

		/// <summary>
		/// Creates a LinkerGenerator instance.
		/// </summary>
		public static ILinkerGenerator CreateLinkerGenerator()
		{
			return new LinkerGenerator();
		}
	}
}
#endif
