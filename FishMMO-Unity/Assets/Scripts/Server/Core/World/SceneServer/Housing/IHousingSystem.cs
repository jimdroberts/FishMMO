using FishMMO.Shared;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Engine-agnostic public API for housing configuration.
	/// </summary>
	/// <remarks>
	/// Everything the housing feature set builds on — purchase, building permissions, tax and
	/// reclamation — resolves to "who may own this plot", so that question is answered here once
	/// rather than restated by each system.
	/// </remarks>
	public interface IHousingSystem : IServerBehaviour
	{
		/// <summary>
		/// Who may own land and housing on this server.
		/// </summary>
		HousingOwnershipMode OwnershipMode { get; }

		/// <summary>
		/// True when housing is enabled in any form.
		/// </summary>
		bool IsHousingEnabled { get; }

		/// <summary>
		/// True when an individual character may own land.
		/// </summary>
		bool AllowsPlayerOwnership { get; }

		/// <summary>
		/// True when a guild may own land.
		/// </summary>
		bool AllowsGuildOwnership { get; }
	}
}
