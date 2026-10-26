using System.Linq;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;

namespace FishMMO.UnitTests.WorldDesign
{
	public class ScreeProductionMeshTests
	{
		// The previous mesh tests used seed 1234; the art generator uses DefaultSeed and lighter LODs.
		[TestCase(ProceduralArtCatalogue.DefaultSeed)]
		[TestCase(1234)]
		public void ScreeAtShippedResolutions_HasSoundNormals_ClosedEdges_AndFitsBudget(int seed)
		{
			int[] resolutions = ProceduralArtCatalogue.FormationResolution;
			foreach (RockType type in RockTypes.All)
			{
				foreach (FormationShape shape in type.Shapes.Where(s => s.Kind == FormationKind.Scree))
				{
					for (int variant = 0; variant < RockVariantCounts.Of(type.Name); variant++)
					{
						for (int lod = 0; lod < resolutions.Length; lod++)
						{
							MeshBuilder mesh = RockFormations.Build(in type, in shape, resolutions[lod], variant, seed);
							string label = $"{type.Name}/{shape.Name}, seed {seed}, variant {variant}, LOD{lod}";
							Assert.That(mesh.Validate(true), Is.Empty, label);
							Assert.That(RockVariantTests.ClosureProblem(mesh), Is.Null.Or.Empty, label);
							Assert.That(mesh.TriangleCount, Is.LessThanOrEqualTo(RockFormations.TriangleBudget[lod]), label);
						}
					}
				}
			}
		}
	}
}
