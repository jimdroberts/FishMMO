using System;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// Marks an IInteractableHandler implementation with the IInteractable type it handles.
	/// Used by InteractableHandlerInitializer for reflection-based auto-discovery.
	/// </summary>
	[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
	public sealed class HandlesInteractableAttribute : Attribute
	{
		/// <summary>
		/// The IInteractable type this handler processes.
		/// </summary>
		public Type InteractableType { get; }

		public HandlesInteractableAttribute(Type interactableType)
		{
			InteractableType = interactableType ?? throw new ArgumentNullException(nameof(interactableType));
		}
	}
}