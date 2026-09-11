using System;
using System.Collections.Generic;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// One server process, as an operator needs to see it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// One type for all three tiers rather than three near-identical ones. A login server has no
	/// character count and no control columns, so those are simply zero and null for it — which
	/// is honest, because the reason they are absent is that a login server has no players on it
	/// and cannot be locked, not that the panel failed to read them.
	/// </para>
	/// <para>
	/// <b>Staleness is not stored and is not decided here.</b> Only <see cref="LastPulse"/> is a
	/// fact; whether that counts as dead depends on the pulse interval the deployment runs, so
	/// the caller is given the age and the threshold and makes the call. A boolean baked into
	/// the row would be a judgement frozen at read time.
	/// </para>
	/// </remarks>
	public sealed class ServerAdminData
	{
		/// <summary>Which tier: <c>login</c>, <c>world</c> or <c>scene</c>.</summary>
		public string Kind { get; set; }

		/// <summary>Primary key, within its tier.</summary>
		public long ID { get; set; }

		/// <summary>The name the process registered under.</summary>
		public string Name { get; set; }

		/// <summary>Where it listens.</summary>
		public string Address { get; set; }

		/// <summary>The port it listens on.</summary>
		public int Port { get; set; }

		/// <summary>When it last said it was alive.</summary>
		public DateTime LastPulse { get; set; }

		/// <summary>When the row was created, which is when the process started.</summary>
		public DateTime TimeCreated { get; set; }

		/// <summary>Players it is carrying. Always zero for a login server.</summary>
		public int CharacterCount { get; set; }

		/// <summary>Whether it is refusing new logins above Player.</summary>
		public bool Locked { get; set; }

		/// <summary>When it is due to shut down, or null.</summary>
		public DateTime? ShutdownAtUtc { get; set; }
	}

	/// <summary>Every server the shard knows about, in one read.</summary>
	/// <remarks>
	/// One call rather than three, because the board is read together and refreshed on a timer:
	/// three round trips on a five-second poll is three times the load for a view that would
	/// then also be able to show the tiers as of three different instants.
	/// </remarks>
	public sealed class ServerBoardData
	{
		/// <summary>The authentication tier.</summary>
		public IReadOnlyList<ServerAdminData> LoginServers { get; set; } = Array.Empty<ServerAdminData>();

		/// <summary>The world tier.</summary>
		public IReadOnlyList<ServerAdminData> WorldServers { get; set; } = Array.Empty<ServerAdminData>();

		/// <summary>The scene tier.</summary>
		public IReadOnlyList<ServerAdminData> SceneServers { get; set; } = Array.Empty<ServerAdminData>();

		/// <summary>How many scene instances are live.</summary>
		public int SceneInstanceCount { get; set; }
	}

	/// <summary>One live scene instance.</summary>
	public sealed class SceneInstanceAdminData
	{
		/// <summary>Primary key.</summary>
		public long ID { get; set; }

		/// <summary>The scene server hosting it.</summary>
		public long SceneServerID { get; set; }

		/// <summary>The world server it belongs to.</summary>
		public long WorldServerID { get; set; }

		/// <summary>The scene being run.</summary>
		public string SceneName { get; set; }

		/// <summary>The handle the scene server knows it by.</summary>
		public int SceneHandle { get; set; }

		/// <summary>Pending, Loading, Ready or Failed.</summary>
		public int Status { get; set; }

		/// <summary>What kind of scene it is.</summary>
		public int Type { get; set; }

		/// <summary>Players in it.</summary>
		public int CharacterCount { get; set; }

		/// <summary>The character that caused it to be created, for an instance.</summary>
		public long CharacterID { get; set; }

		/// <summary>The party it was created for, or zero.</summary>
		public long PartyID { get; set; }

		/// <summary>Whether it refuses uninvited players.</summary>
		public bool IsPrivate { get; set; }

		/// <summary>When it was created.</summary>
		public DateTime TimeCreated { get; set; }
	}

	/// <summary>One page of scene instances.</summary>
	public sealed class SceneInstancePage
	{
		/// <summary>The rows on this page.</summary>
		public IReadOnlyList<SceneInstanceAdminData> Items { get; set; } = Array.Empty<SceneInstanceAdminData>();

		/// <summary>1-based page number.</summary>
		public int Page { get; set; }

		/// <summary>Rows per page.</summary>
		public int PageSize { get; set; }

		/// <summary>Total rows matching the filter.</summary>
		public int TotalCount { get; set; }
	}
}
