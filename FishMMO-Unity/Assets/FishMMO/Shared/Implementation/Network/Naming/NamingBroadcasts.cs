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
	/// Client to server: the names the client wants for a set of IDs of one kind.
	/// </summary>
	/// <remarks>
	/// <para>
	/// One per kind per frame, replacing one <see cref="NamingBroadcast"/> per ID. Opening a
	/// 100-member roster sent 100 requests in one frame, each answered by a query of its own
	/// (hot-path audit M18); the IDs now travel together and the server resolves the ones it does
	/// not already hold in one query. The server answers with <see cref="NamingBatchBroadcast"/>.
	/// </para>
	/// <para>
	/// Added alongside <see cref="NamingBroadcast"/>, not in place of it: the server still answers
	/// the single request, and answers each waiting connection in the form it asked in.
	/// </para>
	/// <para>
	/// A parallel primitive array, not an array of entries: FishNet's codegen writes arrays of
	/// primitives itself, and a custom element type would need a hand-written array serializer.
	/// </para>
	/// </remarks>
	public struct NamingRequestBatchBroadcast : IBroadcast
	{
		/// <summary>
		/// Most IDs one request carries. The client sends the rest in a later frame; the server
		/// ignores any beyond it.
		/// </summary>
		public const int MaxIDs = 128;

		/// <summary>Type of the naming system (e.g., character, guild) every ID belongs to.</summary>
		public NamingSystemType Type;
		/// <summary>IDs to resolve. At most <see cref="MaxIDs"/>.</summary>
		public long[] IDs;
	}

	/// <summary>
	/// Server to client: names for a set of IDs of one kind, in answer to
	/// <see cref="NamingRequestBatchBroadcast"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <see cref="IDs"/> and <see cref="Names"/> are parallel and the same length. An EMPTY name
	/// is an answer too: the lookup ran and no such entity exists. The client drops what was
	/// waiting on it instead of holding it for a name that is never coming. A lookup that could
	/// not run (the database did not answer) is left out, and the client asks again. No character
	/// or guild can have an empty name, so the empty string cannot be mistaken for one.
	/// </para>
	/// </remarks>
	public struct NamingBatchBroadcast : IBroadcast
	{
		/// <summary>
		/// Most entries one reply carries. A larger answer is split across replies.
		/// </summary>
		public const int MaxEntries = 128;

		/// <summary>Type of the naming system (e.g., character, guild) every ID belongs to.</summary>
		public NamingSystemType Type;
		/// <summary>The IDs answered.</summary>
		public long[] IDs;
		/// <summary>The name of the ID at the same index, or empty when no such entity exists.</summary>
		public string[] Names;
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