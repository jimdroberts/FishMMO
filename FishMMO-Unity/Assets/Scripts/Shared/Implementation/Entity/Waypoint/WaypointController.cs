using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Serializing;
using FishNet.Transporting;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Holds the waypoints a character has discovered, per scene, and moves them between the
	/// server and the owner client.
	/// </summary>
	/// <remarks>
	/// <para><b>Server.</b> <see cref="Unlock"/> is called by <see cref="UnlockWaypointAction"/> and
	/// <see cref="GrantWaypointAction"/>; it raises <see cref="IWaypointController.OnWaypointUnlocked"/>,
	/// which the interactable system answers by persisting the page and telling the owner. The
	/// load path installs rows through <see cref="Restore"/>; the save path drains
	/// <see cref="CollectDirtyPages"/> and confirms with <see cref="MarkPersisted"/>.</para>
	/// <para><b>Owner client.</b> The spawn payload carries the whole record (owner only — an
	/// observer is written an empty block, since which towns a stranger has visited is nobody's
	/// business), and <see cref="WaypointUnlockedBroadcast"/> keeps it current. The three other
	/// travel broadcasts are turned into the static events the map listens to.</para>
	/// <para><b>Framing.</b> FishNet packs every behaviour's spawn payload into one buffer with no
	/// per-behaviour framing, so the block is length-prefixed and the reader seeks to its end on
	/// any disagreement rather than returning early. See <c>FactionController.ReadPayload</c>.</para>
	/// </remarks>
	public class WaypointController : CharacterBehaviour, IWaypointController
	{
		/// <summary>Width of the byte count that frames this behaviour's spawn payload.</summary>
		private const int PAYLOAD_LENGTH_BYTES = 4;

		/// <summary>Payload shape byte: nothing follows.</summary>
		private const byte PAYLOAD_SHAPE_EMPTY = 0;

		/// <summary>Payload shape byte: the owner's full record follows.</summary>
		private const byte PAYLOAD_SHAPE_OWNER = 1;

		/// <summary>
		/// Upper bound on scenes accepted from a spawn payload. Far above any real world; exists
		/// so a corrupt count cannot drive an unbounded read loop.
		/// </summary>
		private const int MAX_PAYLOAD_SCENES = 512;

		private readonly Dictionary<string, WaypointUnlockMask> scenes = new Dictionary<string, WaypointUnlockMask>();

		/// <inheritdoc />
		public IReadOnlyCollection<string> UnlockedScenes => scenes.Keys;

		/// <summary>
		/// Clears the record. The object is pooled; a respawned character must not inherit the
		/// previous occupant's discoveries.
		/// </summary>
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);
			scenes.Clear();
		}

		/// <inheritdoc />
		public bool IsUnlocked(string sceneName, int waypointIndex)
		{
			return !string.IsNullOrEmpty(sceneName) &&
				scenes.TryGetValue(sceneName, out WaypointUnlockMask mask) &&
				mask.Contains(waypointIndex);
		}

		/// <inheritdoc />
		public bool Unlock(string sceneName, int waypointIndex)
		{
			if (string.IsNullOrEmpty(sceneName) || !WaypointUnlockMask.IsValidIndex(waypointIndex))
			{
				return false;
			}

			if (!scenes.TryGetValue(sceneName, out WaypointUnlockMask mask))
			{
				mask = new WaypointUnlockMask();
				scenes[sceneName] = mask;
			}

			if (!mask.Add(waypointIndex))
			{
				return false;
			}

			IWaypointController.OnWaypointUnlocked?.Invoke(Character, sceneName, waypointIndex);
			return true;
		}

		/// <inheritdoc />
		public void Restore(string sceneName, int page, ulong mask)
		{
			if (string.IsNullOrEmpty(sceneName) || mask == 0 || page < 0 || page >= WaypointUnlockMask.MaxPages)
			{
				return;
			}

			if (!scenes.TryGetValue(sceneName, out WaypointUnlockMask set))
			{
				set = new WaypointUnlockMask();
				scenes[sceneName] = set;
			}
			set.Restore(page, mask);
		}

		/// <inheritdoc />
		public void CollectDirtyPages(List<WaypointPageSnapshot> results)
		{
			if (results == null)
			{
				return;
			}
			foreach (KeyValuePair<string, WaypointUnlockMask> scene in scenes)
			{
				scene.Value.CollectDirtyPages(scene.Key, results);
			}
		}

		/// <inheritdoc />
		public void MarkPersisted(string sceneName, int page, ulong writtenMask)
		{
			if (!string.IsNullOrEmpty(sceneName) && scenes.TryGetValue(sceneName, out WaypointUnlockMask mask))
			{
				mask.MarkPersisted(page, writtenMask);
			}
		}

		/// <summary>
		/// Number of unlocked waypoints across every scene. Diagnostics and tests.
		/// </summary>
		public int CountUnlocked()
		{
			int count = 0;
			foreach (WaypointUnlockMask mask in scenes.Values)
			{
				for (int page = 0; page < mask.PageCount; ++page)
				{
					ulong bits = mask.GetPage(page);
					while (bits != 0)
					{
						bits &= bits - 1;
						++count;
					}
				}
			}
			return count;
		}

		#region Payload

		/// <inheritdoc />
		public override void WritePayload(NetworkConnection conn, Writer writer)
		{
			writer.Skip(PAYLOAD_LENGTH_BYTES);
			int blockStart = writer.Position;

			/* Two shapes behind a shape byte, chosen by the receiver. An observer gets an empty
			 * block: the record is the owner's alone, and the map only ever draws the local
			 * character's. See PayloadVisibility for why the validity test matters. */
			bool isOwner = PayloadVisibility.IsOwner(this, conn);
			writer.WriteUInt8Unpacked(isOwner ? PAYLOAD_SHAPE_OWNER : PAYLOAD_SHAPE_EMPTY);

			if (isOwner)
			{
				int sceneCount = 0;
				foreach (KeyValuePair<string, WaypointUnlockMask> scene in scenes)
				{
					if (!scene.Value.IsEmpty)
					{
						++sceneCount;
					}
				}

				writer.WriteInt32(sceneCount);
				foreach (KeyValuePair<string, WaypointUnlockMask> scene in scenes)
				{
					WaypointUnlockMask mask = scene.Value;
					if (mask.IsEmpty)
					{
						continue;
					}

					writer.WriteString(scene.Key);
					/* Only up to the last non-empty page. Pages are allocated to the highest
					 * index ever unlocked, so this is the whole record; empty trailing pages
					 * would be bytes carrying nothing. */
					int pageCount = mask.PageCount;
					while (pageCount > 0 && mask.GetPage(pageCount - 1) == 0)
					{
						--pageCount;
					}
					writer.WriteUInt8Unpacked((byte)pageCount);
					for (int page = 0; page < pageCount; ++page)
					{
						writer.WriteUInt64Unpacked(mask.GetPage(page));
					}
				}
			}

			writer.InsertUInt32Unpacked((uint)(writer.Position - blockStart), blockStart - PAYLOAD_LENGTH_BYTES);
		}

		/// <inheritdoc />
		public override void ReadPayload(NetworkConnection conn, Reader reader)
		{
			uint declaredLength = reader.ReadUInt32Unpacked();
			int remainingBytes = reader.Remaining;
			if (declaredLength > (uint)remainingBytes)
			{
				Log.Error("WaypointController",
					$"ReadPayload: framed length {declaredLength} exceeds the {remainingBytes} bytes remaining in the spawn payload. Discarding the remainder.");
				reader.Position += remainingBytes;
				return;
			}
			int blockLength = (int)declaredLength;
			int blockEnd = reader.Position + blockLength;

			byte shape = reader.ReadUInt8Unpacked();
			if (shape == PAYLOAD_SHAPE_OWNER)
			{
				scenes.Clear();

				int sceneCount = reader.ReadInt32();
				if (sceneCount < 0 || sceneCount > MAX_PAYLOAD_SCENES)
				{
					Log.Error("WaypointController", $"ReadPayload: scene count {sceneCount} exceeds limit {MAX_PAYLOAD_SCENES}. Aborting payload read.");
					reader.Position = blockEnd;
					return;
				}

				for (int i = 0; i < sceneCount; ++i)
				{
					string sceneName = reader.ReadStringAllocated();
					int pageCount = reader.ReadUInt8Unpacked();
					if (pageCount > WaypointUnlockMask.MaxPages)
					{
						Log.Error("WaypointController", $"ReadPayload: page count {pageCount} for scene '{sceneName}' exceeds {WaypointUnlockMask.MaxPages}. Aborting payload read.");
						scenes.Clear();
						reader.Position = blockEnd;
						return;
					}
					for (int page = 0; page < pageCount; ++page)
					{
						/* The restore path, not Unlock: this runs on spawn and must not raise
						 * OnWaypointUnlocked once per waypoint the character already knew. The map
						 * rebuilds from the record when the character is set. */
						Restore(sceneName, page, reader.ReadUInt64Unpacked());
					}
				}
			}

			if (reader.Position != blockEnd)
			{
				Log.Error("WaypointController",
					$"ReadPayload consumed {reader.Position - (blockEnd - blockLength)} of {blockLength} framed bytes. Seeking to the end of the block.");
				reader.Position = blockEnd;
			}
		}

		#endregion

#if !UNITY_SERVER
		/// <summary>
		/// Registers the owner-only broadcasts that keep the record current and surface the
		/// travel results.
		/// </summary>
		public override void OnStartCharacter()
		{
			base.OnStartCharacter();

			if (!base.IsOwner)
			{
				enabled = false;
				return;
			}

			ClientManager.RegisterBroadcast<WaypointUnlockedBroadcast>(OnClientWaypointUnlockedBroadcastReceived);
			ClientManager.RegisterBroadcast<WaypointTravelledBroadcast>(OnClientWaypointTravelledBroadcastReceived);
			ClientManager.RegisterBroadcast<WaypointTravelRefusedBroadcast>(OnClientWaypointTravelRefusedBroadcastReceived);
			ClientManager.RegisterBroadcast<WaypointOpenMapBroadcast>(OnClientWaypointOpenMapBroadcastReceived);
		}

		/// <summary>
		/// Unregisters the broadcasts. Symmetric with <see cref="OnStartCharacter"/> — a pooled
		/// respawn re-runs it, and an unmatched register would stack handlers per respawn.
		/// </summary>
		public override void OnStopCharacter()
		{
			base.OnStopCharacter();

			if (base.IsOwner)
			{
				ClientManager.UnregisterBroadcast<WaypointUnlockedBroadcast>(OnClientWaypointUnlockedBroadcastReceived);
				ClientManager.UnregisterBroadcast<WaypointTravelledBroadcast>(OnClientWaypointTravelledBroadcastReceived);
				ClientManager.UnregisterBroadcast<WaypointTravelRefusedBroadcast>(OnClientWaypointTravelRefusedBroadcastReceived);
				ClientManager.UnregisterBroadcast<WaypointOpenMapBroadcast>(OnClientWaypointOpenMapBroadcastReceived);
			}
		}

		private void OnClientWaypointUnlockedBroadcastReceived(WaypointUnlockedBroadcast msg, Channel channel)
		{
			// Unlock raises OnWaypointUnlocked on this peer when the bit is new, which is what the map wants.
			Unlock(msg.SceneName, msg.WaypointIndex);
		}

		private void OnClientWaypointTravelledBroadcastReceived(WaypointTravelledBroadcast msg, Channel channel)
		{
			IWaypointController.OnWaypointTravelled?.Invoke(Character, msg.SceneName, msg.WaypointIndex);
		}

		private void OnClientWaypointTravelRefusedBroadcastReceived(WaypointTravelRefusedBroadcast msg, Channel channel)
		{
			IWaypointController.OnWaypointTravelRefused?.Invoke(Character, msg.SceneName, msg.WaypointIndex, msg.Reason);
		}

		private void OnClientWaypointOpenMapBroadcastReceived(WaypointOpenMapBroadcast msg, Channel channel)
		{
			IWaypointController.OnWaypointMapRequested?.Invoke(Character, msg.SceneName, msg.WaypointIndex);
		}
#endif
	}
}
