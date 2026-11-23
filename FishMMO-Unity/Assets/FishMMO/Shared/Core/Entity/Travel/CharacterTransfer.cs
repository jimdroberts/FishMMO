using UnityEngine;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Where a character is being sent: an open-world scene, optionally one channel of it, and a pose in it.
	/// </summary>
	/// <remarks>
	/// <para><b>The channel is a scene row ID.</b> A character's <c>SceneHandle</c> already holds the row ID of the
	/// scene instance it belongs to, and the world server routes a reconnecting character to that exact instance
	/// when it has room, falling back to any other instance of the same scene when it has not. A destination with
	/// <see cref="SceneID"/> 0 asks for no particular channel.</para>
	/// <para>Instances (dungeons) are never a destination: a character enters one only through the instance
	/// routing, never by naming its scene here.</para>
	/// </remarks>
	public readonly struct TransferDestination
	{
		/// <summary>The open-world scene to arrive in.</summary>
		public readonly string SceneName;

		/// <summary>The scene row ID (channel) to prefer, or 0 for whichever channel the world server picks.</summary>
		public readonly long SceneID;

		/// <summary>The arrival position.</summary>
		public readonly Vector3 Position;

		/// <summary>The arrival rotation.</summary>
		public readonly Quaternion Rotation;

		public TransferDestination(string sceneName, long sceneID, Vector3 position, Quaternion rotation)
		{
			SceneName = sceneName;
			SceneID = sceneID;
			Position = position;
			Rotation = rotation;
		}
	}

	/// <summary>What happened to a transfer request.</summary>
	public enum TransferOutcome : byte
	{
		/// <summary>Nothing moved; see the <see cref="TransferRefusal"/>.</summary>
		Refused = 0,

		/// <summary>The destination is in the scene instance the character is standing in: it was moved there.</summary>
		MovedLocally = 1,

		/// <summary>The character was saved and released; the client is on its way to the destination through the world server.</summary>
		Transferring = 2,
	}

	/// <summary>Why a transfer was refused.</summary>
	public enum TransferRefusal : byte
	{
		None = 0,

		/// <summary>No transfer host is installed: this peer is not a scene server.</summary>
		NoHost = 1,

		/// <summary>The character has no live connection, or this server does not hold it.</summary>
		NotConnected = 2,

		/// <summary>The character is not loaded, or is already on its way somewhere.</summary>
		NotReady = 3,

		/// <summary>The destination scene is not one this world knows.</summary>
		UnknownScene = 4,

		/// <summary>The character has no motor to move.</summary>
		NoMotor = 5,

		/// <summary>The character has no bind point to go to.</summary>
		NoBindPoint = 6,
	}

	/// <summary>
	/// The scene server's character transfer, as shared code sees it: the one way to move a character to a place,
	/// in this scene or another.
	/// </summary>
	/// <remarks>
	/// Implemented by the scene server's character system, which installs itself as <see cref="CharacterTransfer.Host"/>
	/// at start. Gameplay code (travel spells, actions) lives in shared assemblies that cannot see the server, so it
	/// goes through <see cref="CharacterTransfer"/>.
	/// </remarks>
	public interface ICharacterTransferHost
	{
		/// <summary>
		/// Sends <paramref name="character"/> to <paramref name="destination"/>: a local move when the destination is in
		/// the scene instance it is standing in, otherwise a save, release and hand-off to the world server.
		/// </summary>
		/// <remarks>Gates nothing about combat or the caller's own rules; the caller decides whether the move is allowed.</remarks>
		TransferOutcome Transfer(IPlayerCharacter character, in TransferDestination destination, out TransferRefusal refusal);

		/// <summary>
		/// Sends <paramref name="character"/> to its bind point, the way a respawn at the bind point does (including the
		/// cloud-world landing rule when the bind point is open air in the scene it is standing in).
		/// </summary>
		TransferOutcome TransferToBindPoint(IPlayerCharacter character, out TransferRefusal refusal);
	}

	/// <summary>
	/// Where shared code finds the <see cref="ICharacterTransferHost"/>.
	/// </summary>
	public static class CharacterTransfer
	{
		/// <summary>The installed host; null on clients and before the scene server starts.</summary>
		public static ICharacterTransferHost Host { get; set; }

		/// <inheritdoc cref="ICharacterTransferHost.Transfer"/>
		public static TransferOutcome Transfer(IPlayerCharacter character, in TransferDestination destination, out TransferRefusal refusal)
		{
			ICharacterTransferHost host = Host;
			if (host == null)
			{
				refusal = TransferRefusal.NoHost;
				return TransferOutcome.Refused;
			}
			return host.Transfer(character, destination, out refusal);
		}

		/// <inheritdoc cref="ICharacterTransferHost.TransferToBindPoint"/>
		public static TransferOutcome TransferToBindPoint(IPlayerCharacter character, out TransferRefusal refusal)
		{
			ICharacterTransferHost host = Host;
			if (host == null)
			{
				refusal = TransferRefusal.NoHost;
				return TransferOutcome.Refused;
			}
			return host.TransferToBindPoint(character, out refusal);
		}

		/// <summary>
		/// Whether a destination is a local move for a character standing in <paramref name="currentScene"/>: the same scene
		/// name, and either no channel asked for, the character's own channel, or the character is inside an instance
		/// (whose scene name is its own and has no channels).
		/// </summary>
		/// <param name="currentScene">The scene the character is physically in (<c>CurrentSceneName()</c>).</param>
		/// <param name="currentSceneID">The character's scene row ID (<c>SceneHandle</c>).</param>
		/// <param name="inInstance">Whether the character is inside an instance.</param>
		/// <param name="destinationScene">The destination scene name.</param>
		/// <param name="destinationSceneID">The destination channel, or 0.</param>
		public static bool IsLocalMove(string currentScene, long currentSceneID, bool inInstance, string destinationScene, long destinationSceneID)
		{
			if (string.IsNullOrEmpty(currentScene) || !string.Equals(currentScene, destinationScene, System.StringComparison.Ordinal))
			{
				return false;
			}
			return inInstance || destinationSceneID == 0 || destinationSceneID == currentSceneID;
		}
	}
}
