using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// Reads and writes a character's explored-territory maps on the local machine.
	/// </summary>
	/// <remarks>
	/// <para><b>Why it never goes to the server.</b> Exploration is a per-character convenience
	/// that changes several times a second and is worth nothing to anybody else. Sending it would
	/// mean a write path, a table, a migration and a sync broadcast for data whose entire purpose
	/// is to decide which pixels are dark. A player who reinstalls loses their revealed map and
	/// re-walks it, which is a fair trade for a subsystem that costs the server nothing.</para>
	///
	/// <para><b>What the signature is for, and what it is not.</b> Each file carries an HMAC over
	/// its contents, keyed from the character and a build constant. That detects a file edited by
	/// hand or copied from another character, and it makes the failure loud instead of silent — a
	/// mismatched file is discarded and exploration starts again rather than being trusted. It is
	/// explicitly <b>not</b> a defence against a determined player: the key is in the binary, so
	/// anybody willing to extract it can forge a fully-revealed map.</para>
	///
	/// <para>That is acceptable because of where the value lives. Revealing the map early gains a
	/// player nothing the game rewards — the Cartography skill this feeds must be advanced by the
	/// <b>server</b>, from the positions it already receives, and never from anything read out of
	/// this file. Keep it that way and forging the file buys a nicer-looking minimap and no
	/// progression at all.</para>
	/// </remarks>
	public static class FogOfWarStore
	{
		/// <summary>Folder, under the install directory, holding every character's maps.</summary>
		public const string DirectoryName = "Cartography";

		/// <summary>Extension used for a single scene's explored map.</summary>
		private const string FileExtension = ".fow";

		/// <summary>Magic bytes identifying the format.</summary>
		private static readonly byte[] Magic = Encoding.ASCII.GetBytes("FMFOW");

		/// <summary>Format version written into new files.</summary>
		/// <summary>
		/// Format version. Two is the chunk grid; one was the per-cell coverage field.
		/// </summary>
		/// <remarks>
		/// A version-one file is discarded rather than converted. Its cells recorded coverage of
		/// four-metre squares, and there is no honest way to decide which chunks that makes
		/// explored — the answer would be a threshold nobody can defend, applied to data whose
		/// whole point was that it was not a yes or no.
		/// </remarks>
		private const byte CurrentVersion = 2;

		/// <summary>Length of the trailing signature, in bytes.</summary>
		private const int SignatureLength = 32;

		/// <summary>
		/// Build constant mixed into every signing key.
		/// </summary>
		/// <remarks>
		/// Not a secret in any meaningful sense — it ships inside the client. Its job is to make
		/// the signature specific to this game rather than to make it unforgeable; see the class
		/// remarks for why that is the right amount of effort to spend here.
		/// </remarks>
		private const string KeySalt = "FishMMO.Cartography.v1";

		/// <summary>
		/// The folder holding one character's explored maps.
		/// </summary>
		/// <param name="characterID">The character's ID.</param>
		/// <returns>An absolute path. The folder may not exist yet.</returns>
		public static string CharacterDirectory(long characterID)
		{
			return Path.Combine(Constants.GetWorkingDirectory(), DirectoryName, characterID.ToString());
		}

		/// <summary>
		/// The file holding one character's explored map for one scene.
		/// </summary>
		/// <param name="characterID">The character's ID.</param>
		/// <param name="sceneName">The scene's name.</param>
		/// <returns>An absolute path. The file may not exist yet.</returns>
		public static string FilePath(long characterID, string sceneName)
		{
			return Path.Combine(CharacterDirectory(characterID), SanitizeSceneName(sceneName) + FileExtension);
		}

		/// <summary>
		/// Loads a character's explored map for a scene.
		/// </summary>
		/// <param name="characterID">The character's ID.</param>
		/// <param name="sceneName">The scene's name.</param>
		/// <param name="worldRect">The rectangle the map must cover.</param>
		/// <param name="chunkSize">The chunk size the map must use.</param>
		/// <returns>
		/// The stored map, or null when there is no usable file. A null return is the normal case
		/// for a character who has not been to the scene before and is not an error.
		/// </returns>
		public static FogOfWarMap Load(long characterID, string sceneName, Rect worldRect, float chunkSize)
		{
			string path = FilePath(characterID, sceneName);

			try
			{
				if (!File.Exists(path))
				{
					return null;
				}

				byte[] file = File.ReadAllBytes(path);
				if (file.Length <= SignatureLength)
				{
					Log.Warning("FogOfWarStore", $"Explored map '{path}' is too short to be valid; starting again for this scene.");
					return null;
				}

				int bodyLength = file.Length - SignatureLength;
				if (!VerifySignature(file, bodyLength, characterID))
				{
					Log.Warning("FogOfWarStore", $"Explored map '{path}' does not match its signature — it was edited, truncated, or copied from another character. Discarding it and starting again for this scene.");
					return null;
				}

				using (MemoryStream stream = new MemoryStream(file, 0, bodyLength, false))
				using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8))
				{
					for (int i = 0; i < Magic.Length; ++i)
					{
						if (reader.ReadByte() != Magic[i])
						{
							Log.Warning("FogOfWarStore", $"Explored map '{path}' is not a FishMMO map file; ignoring it.");
							return null;
						}
					}

					byte version = reader.ReadByte();
					if (version != CurrentVersion)
					{
						/* Discarded, not migrated. A map is re-earned by walking rather than by a
						 * conversion routine, and for the one older version that ever existed there
						 * is nothing honest to convert it into — see CurrentVersion. */
						Log.Debug("FogOfWarStore", $"Explored map '{path}' is version {version}, this build writes version {CurrentVersion}; starting again for this scene.");
						return null;
					}

					long storedCharacter = reader.ReadInt64();
					string storedScene = reader.ReadString();
					float storedX = reader.ReadSingle();
					float storedY = reader.ReadSingle();
					float storedWidth = reader.ReadSingle();
					float storedHeight = reader.ReadSingle();
					float storedChunkSize = reader.ReadSingle();
					int storedChunksX = reader.ReadInt32();
					int storedChunksZ = reader.ReadInt32();

					if (storedCharacter != characterID ||
						!string.Equals(storedScene, sceneName, StringComparison.Ordinal))
					{
						Log.Warning("FogOfWarStore", $"Explored map '{path}' belongs to a different character or scene; ignoring it.");
						return null;
					}

					/* The scene's bounds are derived from its boundary volumes and can legitimately
					 * change when a level designer moves one. A grid that no longer lines up with
					 * the world cannot be reused: every chunk would name a different patch of
					 * ground than it did when it was written. */
					if (!Mathf.Approximately(storedChunkSize, chunkSize) ||
						!Mathf.Approximately(storedX, worldRect.xMin) ||
						!Mathf.Approximately(storedY, worldRect.yMin) ||
						!Mathf.Approximately(storedWidth, worldRect.width) ||
						!Mathf.Approximately(storedHeight, worldRect.height))
					{
						Log.Debug("FogOfWarStore", $"Explored map '{path}' was recorded against different scene bounds; starting again for this scene.");
						return null;
					}

					/* Checked against the file AND against the grid the header describes. The
					 * signature already rules out a hostile file, but a length taken on trust would
					 * still turn a truncated one into a large pointless allocation. */
					int chunkLength = reader.ReadInt32();
					if (chunkLength < 0 || chunkLength > bodyLength ||
						chunkLength != storedChunksX * storedChunksZ)
					{
						Log.Warning("FogOfWarStore", $"Explored map '{path}' declares an impossible payload length; ignoring it.");
						return null;
					}

					byte[] chunks = reader.ReadBytes(chunkLength);

					FogOfWarMap map = FogOfWarMap.FromChunks(worldRect, chunkSize, chunks);
					if (map == null)
					{
						Log.Warning("FogOfWarStore", $"Explored map '{path}' holds the wrong number of chunks for its grid; ignoring it.");
					}
					return map;
				}
			}
			catch (Exception exception)
			{
				/* Swallowed on purpose, and only here. A map that cannot be read costs the player
				 * some re-walked ground; letting the exception out of a panel's character-set path
				 * would abort world entry over a cosmetic file. The path is logged so a real
				 * permissions or disk problem is still visible. */
				Log.Warning("FogOfWarStore", $"Could not read explored map '{path}': {exception.Message}. Starting again for this scene.");
				return null;
			}
		}

		/// <summary>
		/// Writes a character's explored map for a scene.
		/// </summary>
		/// <param name="characterID">The character's ID.</param>
		/// <param name="sceneName">The scene's name.</param>
		/// <param name="map">The map to write.</param>
		/// <returns>True when the file was written.</returns>
		public static bool Save(long characterID, string sceneName, FogOfWarMap map)
		{
			if (map == null)
			{
				return false;
			}

			string path = FilePath(characterID, sceneName);

			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(path));

				byte[] body;
				using (MemoryStream stream = new MemoryStream())
				using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8))
				{
					writer.Write(Magic);
					writer.Write(CurrentVersion);
					writer.Write(characterID);
					writer.Write(sceneName);
					writer.Write(map.WorldRect.xMin);
					writer.Write(map.WorldRect.yMin);
					writer.Write(map.WorldRect.width);
					writer.Write(map.WorldRect.height);
					writer.Write(map.ChunkSize);
					writer.Write(map.ChunksX);
					writer.Write(map.ChunksZ);

					/* Written raw. One byte per chunk is a hundred-odd bytes for a whole scene —
					 * smaller than the header in front of it — so the GZip pass the per-cell grid
					 * needed has been dropped along with the grid. */
					writer.Write(map.Chunks.Length);
					writer.Write(map.Chunks);

					writer.Flush();
					body = stream.ToArray();
				}

				byte[] signature = Sign(body, body.Length, characterID);

				/* Written to a temporary file and moved into place. The save runs on a debounce
				 * and on quit, so a crash or a pulled power cable lands in the middle of one often
				 * enough to matter; a half-written file would fail its signature check and throw
				 * away every scene the character had explored, which is a far worse outcome than
				 * losing the last few seconds of walking. */
				string temporary = path + ".tmp";
				using (FileStream file = File.Create(temporary))
				{
					file.Write(body, 0, body.Length);
					file.Write(signature, 0, signature.Length);
				}

				if (File.Exists(path))
				{
					File.Delete(path);
				}
				File.Move(temporary, path);

				map.ClearDirty();
				return true;
			}
			catch (Exception exception)
			{
				Log.Warning("FogOfWarStore", $"Could not write explored map '{path}': {exception.Message}.");
				return false;
			}
		}

		/// <summary>
		/// Deletes every explored map belonging to a character.
		/// </summary>
		/// <param name="characterID">The character's ID.</param>
		/// <remarks>
		/// Offered so the options panel can give the player a way to start their maps again, and
		/// so a character deleted on the server does not leave its maps behind forever.
		/// </remarks>
		public static void DeleteAll(long characterID)
		{
			string directory = CharacterDirectory(characterID);
			try
			{
				if (Directory.Exists(directory))
				{
					Directory.Delete(directory, true);
				}
			}
			catch (Exception exception)
			{
				Log.Warning("FogOfWarStore", $"Could not delete explored maps in '{directory}': {exception.Message}.");
			}
		}


		/// <summary>
		/// Signs a file body.
		/// </summary>
		/// <param name="body">The buffer holding the body.</param>
		/// <param name="length">How many bytes of the buffer make up the body.</param>
		/// <param name="characterID">The character the file belongs to.</param>
		/// <returns>The signature.</returns>
		private static byte[] Sign(byte[] body, int length, long characterID)
		{
			using (HMACSHA256 hmac = new HMACSHA256(BuildKey(characterID)))
			{
				return hmac.ComputeHash(body, 0, length);
			}
		}

		/// <summary>
		/// Checks a file's trailing signature against its body.
		/// </summary>
		/// <param name="file">The whole file.</param>
		/// <param name="bodyLength">How many leading bytes make up the signed body.</param>
		/// <param name="characterID">The character the file should belong to.</param>
		/// <returns>True when the signature matches.</returns>
		private static bool VerifySignature(byte[] file, int bodyLength, long characterID)
		{
			byte[] expected = Sign(file, bodyLength, characterID);

			/* Constant time over the whole array. There is no attacker to time here — the file is
			 * on the player's own disk — but a comparison that returns early is the kind of thing
			 * that gets copied into a context where it matters, and the cost of not doing that is
			 * thirty-two iterations. */
			int difference = 0;
			for (int i = 0; i < SignatureLength; ++i)
			{
				difference |= expected[i] ^ file[bodyLength + i];
			}
			return difference == 0;
		}

		/// <summary>
		/// Derives the signing key for a character.
		/// </summary>
		/// <param name="characterID">The character's ID.</param>
		/// <returns>The key bytes.</returns>
		private static byte[] BuildKey(long characterID)
		{
			using (SHA256 sha = SHA256.Create())
			{
				return sha.ComputeHash(Encoding.UTF8.GetBytes(KeySalt + ":" + characterID.ToString()));
			}
		}

		/// <summary>
		/// Makes a scene name safe to use as a file name.
		/// </summary>
		/// <param name="sceneName">The scene name.</param>
		/// <returns>A name containing no path or invalid characters.</returns>
		/// <remarks>
		/// Scene names in this project contain spaces ("StartScene A"), which are fine, but the
		/// name reaches this from server-supplied character state and is being turned into a path.
		/// Replacing rather than rejecting keeps a scene whose name is later changed working; the
		/// worst case is two scenes sharing a file, which shows up as one map, not as a write
		/// outside the folder.
		/// </remarks>
		private static string SanitizeSceneName(string sceneName)
		{
			if (string.IsNullOrWhiteSpace(sceneName))
			{
				return "unknown";
			}

			char[] characters = sceneName.ToCharArray();
			char[] invalid = Path.GetInvalidFileNameChars();

			for (int i = 0; i < characters.Length; ++i)
			{
				if (Array.IndexOf(invalid, characters[i]) >= 0 ||
					characters[i] == Path.DirectorySeparatorChar ||
					characters[i] == Path.AltDirectorySeparatorChar ||
					characters[i] == '.')
				{
					characters[i] = '_';
				}
			}

			return new string(characters);
		}
	}
}
