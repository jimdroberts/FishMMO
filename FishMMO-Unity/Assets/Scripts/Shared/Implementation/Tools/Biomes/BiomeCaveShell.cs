using System;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// The forms a generated cave is carved in, as a biome template names them for its cave shell (<see cref="BiomeCaveShell"/>).
	/// The same members, by the same numbers, as the cave shaper's own form (CaveForm, editor only), which a test keeps equal.
	/// </summary>
	public enum BiomeCaveForm : byte
	{
		/// <summary>A walk-in cave system.</summary>
		Cave = 0,
		/// <summary>A short passage opening into a broad chamber.</summary>
		Grotto = 1,
		/// <summary>A cave opened by the waves.</summary>
		SeaCave = 2,
		/// <summary>A cave in ice.</summary>
		IceCave = 3,
		/// <summary>A drained lava conduit.</summary>
		LavaTube = 4,
		/// <summary>A worked mine.</summary>
		Mine = 5,
	}

	/// <summary>
	/// The rock a cave of one form is carved in, in one biome (<see cref="BiomeTemplate.CaveShells"/>): the stone of its walls,
	/// roof and floor, and with it its formations (dripstone, fallen blocks, spoil), which are always the shell's rock.
	/// </summary>
	/// <remarks>
	/// <see cref="Rock"/> is a rock type name (Basalt, Limestone…), Ice for a cave in glacier ice, or <see cref="Ground"/>: the
	/// rock the ground is made of where the cave is cut, as the biome takes it (the planet geology's rock where the biome
	/// accepts it, else its own rock), which is what a cave, grotto, sea cave or mine has always been cut in.
	/// </remarks>
	[Serializable]
	public struct BiomeCaveShell
	{
		/// <summary>The shell rock meaning "the ground's own rock here, by the biome's rock rule".</summary>
		public const string Ground = "Ground";

		[Tooltip("The cave's form.")]
		public BiomeCaveForm Form;
		[Tooltip("The rock the cave is carved in: Ground (the rock the ground is made of there, by this biome's Own Rock and Accepted Rocks), a rock type name (Basalt, Limestone, Granite…), or Ice. Its formations are the same rock.")]
		public string Rock;

		public BiomeCaveShell(BiomeCaveForm form, string rock)
		{
			Form = form;
			Rock = rock;
		}

		public override string ToString() => $"{Form}={Rock}";
	}
}
