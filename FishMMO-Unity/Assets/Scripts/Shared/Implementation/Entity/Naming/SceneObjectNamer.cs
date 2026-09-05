using UnityEngine;
using FishNet.Object;
using FishNet.Connection;
using FishNet.Serializing;
using FishMMO.Logging;
using FishMMO.Shared.Core;
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared
{
	/// <summary>
	/// Gives a scene object a generated name — a character, city, dungeon, point of interest or
	/// legendary item name — from the name generator, using the object's race and the settings
	/// authored on the prefab.
	///
	/// <para>The server rolls a seed and a gender and ships those (4 + 1 bytes); every peer then
	/// regenerates the same name from the same templates, so the name itself never crosses the wire.
	/// The race comes from the object's <see cref="IFactionController"/> unless the settings override
	/// it, which is how one prefab names itself correctly at any spawner.</para>
	/// </summary>
	public class SceneObjectNamer : NetworkBehaviour
	{
		[SerializeField]
		private SceneObjectNamingSettings settings = new SceneObjectNamingSettings();

		/// <summary>
		/// The seed the name is generated from. Zero means no name was generated and the authored
		/// name stands.
		/// </summary>
		private int nameSeed;

		/// <summary>
		/// The gender behind a character name; also what <see cref="NPC"/> picks its model set with.
		/// </summary>
		private CharacterGender selectedGender = CharacterGender.Unspecified;

		/// <summary>
		/// The biome the name was read from, by cached-object ID; 0 when the mode uses none.
		/// </summary>
		private int biomeID;

		/// <summary>
		/// The climate variant the biome was read under, as an index into
		/// <see cref="SceneObjectNameResolver.VariantsFor"/> plus one; 0 for none.
		/// </summary>
		private byte variantIndex;

		/// <summary>
		/// True once the seed and gender have been rolled (server) or received (client).
		/// </summary>
		private bool nameGenerated;

		/// <summary>
		/// The GameObject's authored name, captured before any generated name replaces it.
		/// </summary>
		private string authoredName;

		/// <summary>
		/// The name the generator produced, or null while the authored name stands.
		/// </summary>
		private string generatedName;

		/// <summary>
		/// The authored naming settings.
		/// </summary>
		public SceneObjectNamingSettings Settings => settings;

		/// <summary>
		/// The gender behind this object's generated name.
		/// </summary>
		public CharacterGender SelectedGender => selectedGender;

		/// <summary>
		/// The seed the name was generated from, or zero when none was.
		/// </summary>
		public int NameSeed => nameSeed;

		/// <summary>
		/// The generated name, or null while the authored name stands.
		/// </summary>
		public string GeneratedName => generatedName;

		/// <summary>
		/// The name this object currently shows: the generated one when there is one, else the authored one.
		/// </summary>
		public string DisplayName => string.IsNullOrEmpty(generatedName) ? authoredName : generatedName;

		/// <summary>
		/// Captures the authored name so a pooled reuse has something to fall back to.
		/// </summary>
		private void Awake()
		{
			authoredName = gameObject.name;
		}

		/// <summary>
		/// Rolls the seed and gender, then applies the name. Runs on every spawn including pool reuse.
		/// </summary>
		public override void OnStartServer()
		{
			base.OnStartServer();

			GenerateNameIfNeeded();
			ApplyGeneratedName();
		}

		/// <summary>
		/// Applies the name received in the payload.
		/// </summary>
		/// <remarks>
		/// Not done in <see cref="ReadPayload"/>: this behaviour precedes <c>FactionController</c> in
		/// every NPC prefab's component order, so when its payload is read the faction has not yet read
		/// the race a spawner may have overridden. By the time start callbacks run every behaviour's
		/// payload has been read and the race is correct.
		/// </remarks>
		public override void OnStartClient()
		{
			base.OnStartClient();

			if (!base.IsServerInitialized)
			{
				ApplyGeneratedName();
			}
		}

		/// <summary>
		/// Forgets the generated name so the next occupant of this pool slot draws its own.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <see cref="nameGenerated"/> is a once-only latch and <see cref="GenerateNameIfNeeded"/>
		/// returns early on it, so without this every NPC that ever came out of a given pool slot
		/// would wear the name the first occupant drew — on the server, in the spawn payload, and
		/// therefore on every client's name label too.
		/// </para>
		/// <para>
		/// The GameObject name is put back rather than left alone: a prefab whose settings cannot
		/// produce a name makes <see cref="ApplyGeneratedName"/> keep the current name, and the
		/// previous occupant's name would then survive as this object's identity.
		/// </para>
		/// </remarks>
		/// <param name="asServer">True if called on the server.</param>
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);

			nameGenerated = false;
			nameSeed = 0;
			selectedGender = CharacterGender.Unspecified;
			biomeID = 0;
			variantIndex = 0;
			generatedName = null;

			if (!string.IsNullOrEmpty(authoredName))
			{
				gameObject.name = authoredName;
			}
		}

		/// <summary>
		/// Ensures the seed and gender have been rolled and returns the gender. Server only; on a
		/// client the gender arrives in the payload.
		/// </summary>
		/// <returns>The selected generated name gender.</returns>
		public CharacterGender EnsureGeneratedGender()
		{
			GenerateNameIfNeeded();
			return selectedGender;
		}

		/// <summary>
		/// Reads the seed and gender, and for a biome-driven mode the biome and climate variant. The
		/// length follows only from the authored settings, identical on both peers, so the behaviours
		/// after this one in the payload stay aligned whatever the values are.
		/// </summary>
		/// <param name="connection">Network connection.</param>
		/// <param name="reader">Network reader for payload.</param>
		public override void ReadPayload(NetworkConnection connection, Reader reader)
		{
			nameSeed = reader.ReadInt32();
			selectedGender = (CharacterGender)reader.ReadUInt8Unpacked();
			if (settings.UsesBiome)
			{
				biomeID = reader.ReadInt32();
				variantIndex = reader.ReadUInt8Unpacked();
			}
			nameGenerated = true;
		}

		/// <summary>
		/// Writes the seed and gender (five bytes), plus the biome ID and variant (five more) when the
		/// mode names from a biome — so a client never has to agree with the server about a biome map
		/// or the weather to reproduce the name.
		/// </summary>
		/// <param name="connection">Network connection.</param>
		/// <param name="writer">Network writer for payload.</param>
		public override void WritePayload(NetworkConnection connection, Writer writer)
		{
			GenerateNameIfNeeded();

			writer.WriteInt32(nameSeed);
			writer.WriteUInt8Unpacked((byte)selectedGender);
			if (settings.UsesBiome)
			{
				writer.WriteInt32(biomeID);
				writer.WriteUInt8Unpacked(variantIndex);
			}
		}

		/// <summary>
		/// Rolls the seed and gender once on the server.
		/// </summary>
		private void GenerateNameIfNeeded()
		{
			if (nameGenerated)
			{
				return;
			}

			RaceTemplate race = SceneObjectNameResolver.ResolveRace(settings, GetComponent<IFactionController>());
			nameSeed = SceneObjectNameResolver.DeriveSeed(settings, authoredName);
			selectedGender = settings.Mode == SceneObjectNamingMode.Character
				? SceneObjectNameResolver.ResolveGender(settings.GenderPolicy, race, SceneObjectNameResolver.GenderRng(nameSeed))
				: CharacterGender.Unspecified;
			if (settings.UsesBiome)
			{
				WorldSceneSettings.TryGetForScene(gameObject.scene, out WorldSceneSettings scene);
				BiomeTemplate biome = SceneObjectNameResolver.ResolveBiome(settings, transform.position, scene, out BiomeClimateVariant variant);
				biomeID = BiomeRegistry.IDOf(biome);
				variantIndex = SceneObjectNameResolver.VariantIndexOf(biome, scene, variant);
			}
			nameGenerated = true;
		}

		/// <summary>
		/// Regenerates the name from the seed and gender and applies it to the object and, on a
		/// client, to its name label. Keeps the authored name when the settings cannot name the object.
		/// </summary>
		private void ApplyGeneratedName()
		{
			if (!nameGenerated || nameSeed == 0)
			{
				return;
			}

			RaceTemplate race = SceneObjectNameResolver.ResolveRace(settings, GetComponent<IFactionController>());
			// A merchant is titled as a merchant unless the settings say otherwise.
			Interactable interactable = GetComponent<Interactable>();
			string autoProfession = interactable == null || string.IsNullOrWhiteSpace(interactable.Title) ? null : interactable.Title;
			BiomeTemplate biome = null;
			BiomeClimateVariant variant = null;
			if (settings.UsesBiome && biomeID != 0)
			{
				WorldSceneSettings.TryGetForScene(gameObject.scene, out WorldSceneSettings scene);
				BiomeRegistry.TryGetByID(biomeID, out biome);
				variant = SceneObjectNameResolver.VariantAt(biome, scene, variantIndex);
			}
			if (!SceneObjectNameResolver.TryBuild(settings, race, nameSeed, selectedGender, out string name, out string error, autoProfession, biome, variant))
			{
				Log.Warning("SceneObjectNamer", $"'{authoredName}' keeps its authored name: {error}.");
				return;
			}

			generatedName = name;
			gameObject.name = name;

#if !UNITY_SERVER
			/* The label is optional on a character, and a character is optional on the object: the
			 * three interactable NPC prefabs carry this component with a label, a Switch carries it
			 * with neither. */
			ICharacter character = transform.GetComponent<ICharacter>();
			if (character != null && character.CharacterNameLabel != null)
			{
				character.CharacterNameLabel.text = name;
			}
#endif
		}
	}
}
