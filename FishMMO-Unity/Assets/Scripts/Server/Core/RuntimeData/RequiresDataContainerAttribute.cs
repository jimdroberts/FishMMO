using System;

namespace FishMMO.Server.Core
{
	/// <summary>
	/// Declares that a ServerBehaviour requires a specific RuntimeDataContainer type.
	/// The container will be automatically instantiated and registered before
	/// the behaviour is initialized. Multiple systems can declare the same container type,
	/// and only one instance will be created.
	/// </summary>
	[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
	public sealed class RequiresDataContainerAttribute : Attribute
	{
		/// <summary>
		/// The concrete RuntimeDataContainer type required by this behaviour.
		/// Must be a non-abstract class with a parameterless constructor.
		/// </summary>
		public Type ContainerType { get; }

		/// <summary>
		/// Optional: Priority order for container initialization (lower = earlier).
		/// Defaults to 0. Use this when containers have dependencies on other containers.
		/// If multiple systems declare the same container with different priorities,
		/// the first encountered priority wins.
		/// </summary>
		public int InitializationPriority { get; set; }

		/// <summary>
		/// Initializes a new instance of the <see cref="RequiresDataContainerAttribute"/> class.
		/// </summary>
		/// <param name="containerType">The concrete RuntimeDataContainer type required by this behaviour.</param>
		public RequiresDataContainerAttribute(Type containerType)
		{
			ContainerType = containerType;
			InitializationPriority = 0;
		}
	}
}