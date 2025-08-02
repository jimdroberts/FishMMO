using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/// <summary>
	/// Broadcast for updating or assigning a name in the naming system.
	/// Contains the type, ID, and name to assign.
	/// </summary>
	public struct NamingBroadcast : IBroadcast
	{
		/// <summary>Type of the naming system (e.g., character, guild).</summary>
		public NamingSystemType Type;
		/// <summary>ID of the entity being named.</summary>
		public long ID;
		/// <summary>Name to assign to the entity.</summary>
		public string Name;
	}

	/// <summary>
	/// Broadcast for looking up an entity by name in the naming system.
	/// Contains the type, lowercase name, ID, and original name.
	/// </summary>
	public struct ReverseNamingBroadcast : IBroadcast
	{
		/// <summary>Type of the naming system (e.g., character, guild).</summary>
		public NamingSystemType Type;
		/// <summary>Lowercase version of the name for case-insensitive lookup.</summary>
		public string NameLowerCase;
		/// <summary>ID of the entity found by name.</summary>
		public long ID;
		/// <summary>Original name of the entity.</summary>
		public string Name;
	}
}