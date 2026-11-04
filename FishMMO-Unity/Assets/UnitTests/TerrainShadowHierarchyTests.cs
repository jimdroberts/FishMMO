using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using FishMMO.Client;

namespace FishMMO.UnitTests
{
	public class TerrainShadowHierarchyTests
	{
		[Test]
		public void EmptySpaceInsideTheCombinedBoundsDoesNotKeepTheDraw()
		{
			var pieces = new Bounds[32];
			for (int i = 0; i < pieces.Length; i++) pieces[i] = new Bounds(new Vector3(i < 16 ? -20 : 20, (i % 4) * .2f, (i % 8) * .1f), Vector3.one * .1f);
			var tree = new TerrainShadowHierarchy(pieces);
			var enclosing = pieces[0]; foreach (var piece in pieces) enclosing.Encapsulate(piece);
			using var planes = new NativeArray<Plane>(new[] { new Plane(Vector3.right,3), new Plane(Vector3.left,3),new Plane(Vector3.up,3),new Plane(Vector3.down,3),new Plane(Vector3.forward,3),new Plane(Vector3.back,3) }, Allocator.Temp);
			using var splits = new NativeArray<CullingSplit>(new[] { new CullingSplit { cullingPlaneOffset=0, cullingPlaneCount=6 } }, Allocator.Temp);
			Assert.That(TerrainShadowCullingMath.BoundsMask(enclosing, planes, splits), Is.EqualTo(1));
			Assert.That(tree.SplitMask(FishInstance.From(Matrix4x4.identity),planes,splits,1,1), Is.Zero);
		}

		[TestCase(IndexFormat.UInt16)]
		[TestCase(IndexFormat.UInt32)]
		public void MeshHierarchyUsesTheSelectedSubmeshAndBaseVertex(IndexFormat format)
		{
			var mesh = new Mesh { indexFormat=format };
			try
			{
				mesh.vertices = new[] { new Vector3(-21,0,0),new Vector3(-19,0,0),new Vector3(-20,2,0),new Vector3(19,0,0),new Vector3(21,0,0),new Vector3(20,2,0) };
				mesh.subMeshCount=2;mesh.SetIndices(new[] {0,1,2},MeshTopology.Triangles,0,false);mesh.SetIndices(new[] {0,1,2},MeshTopology.Triangles,1,false,3);
				mesh.bounds=new Bounds(Vector3.zero,Vector3.one*100);
				using var planes=new NativeArray<Plane>(new[] {new Plane(Vector3.right,-10)},Allocator.Temp);
				using var splits=new NativeArray<CullingSplit>(new[] {new CullingSplit {cullingPlaneOffset=0,cullingPlaneCount=1}},Allocator.Temp);
				var instance=FishInstance.From(Matrix4x4.identity);
				Assert.That(TerrainShadowHierarchy.FromMesh(mesh,0).SplitMask(instance,planes,splits,1,1),Is.Zero);
				Assert.That(TerrainShadowHierarchy.FromMesh(mesh,1).SplitMask(instance,planes,splits,1,1),Is.EqualTo(1));
				mesh.UploadMeshData(true);
				Assert.That(TerrainShadowHierarchy.FromMesh(mesh,1),Is.Null,"unreadable meshes retain the existing part path");
			}
			finally { Object.DestroyImmediate(mesh); }
		}

		[Test]
		public void BiasPaddingKeepsNearPlaneGeometryAndEmptyMeshesStayEmpty()
		{
			var tree=new TerrainShadowHierarchy(new[] {new Bounds(Vector3.left*.5f,Vector3.one*.1f)});
			using var planes=new NativeArray<Plane>(new[] {new Plane(Vector3.right,0)},Allocator.Temp);
			using var splits=new NativeArray<CullingSplit>(new[] {new CullingSplit {cullingPlaneOffset=0,cullingPlaneCount=1}},Allocator.Temp);
			var instance=FishInstance.From(Matrix4x4.identity);
			Assert.That(tree.SplitMask(instance,planes,splits,1,0),Is.Zero);
			Assert.That(tree.SplitMask(instance,planes,splits,1,1),Is.EqualTo(1));
			Assert.That(new TerrainShadowHierarchy(new Bounds[0]).SplitMask(instance,planes,splits,1,1),Is.Zero);
		}

		[TestCase(0)]
		[TestCase(1)]
		public void HierarchyNeverRejectsAVisiblePrimitiveUnderTransformedInstances(int seed)
		{
			var random=new System.Random(seed);
			float R(float range)=>(float)(random.NextDouble()*2-1)*range;
			var pieces=new Bounds[200];
			for(int i=0;i<pieces.Length;i++)pieces[i]=new Bounds(new Vector3(R(40),R(40),R(40)),new Vector3(1+Mathf.Abs(R(3)),1+Mathf.Abs(R(3)),1+Mathf.Abs(R(3))));
			var tree=new TerrainShadowHierarchy(pieces);
			var instance=FishInstance.From(Matrix4x4.TRS(new Vector3(3,-2,5),Quaternion.Euler(20,55,10),new Vector3(-1.2f,.7f,2)));
			Vector3[] directions={Vector3.right,Vector3.left,Vector3.up,Vector3.down,Vector3.forward,Vector3.back};
			for(int query=0;query<24;query++)
			{
				var origin=new Vector3(R(35),R(35),R(35));
				var rawPlanes=new Plane[36];var rawSplits=new CullingSplit[6];
				for(int f=0;f<6;f++)
				{
					var rotation=Quaternion.LookRotation(directions[f],Mathf.Abs(directions[f].y)>.9f?Vector3.forward:Vector3.up);
					var view=Matrix4x4.Scale(new Vector3(1,1,-1))*Matrix4x4.TRS(origin,rotation,Vector3.one).inverse;
					var p=GeometryUtility.CalculateFrustumPlanes(Matrix4x4.Perspective(92,1,.1f,12)*view);
					for(int i=0;i<6;i++)rawPlanes[f*6+i]=p[i];
					rawSplits[f]=new CullingSplit {cullingPlaneOffset=f*6,cullingPlaneCount=6};
				}
				using var planes=new NativeArray<Plane>(rawPlanes,Allocator.Temp);
				using var splits=new NativeArray<CullingSplit>(rawSplits,Allocator.Temp);
				ushort expected=0;
				foreach(var piece in pieces)
				{
					var world=TerrainShadowCullingMath.WorldBounds(instance,piece);world.Expand(2);
					expected|=TerrainShadowCullingMath.BoundsMask(world,planes,splits);
				}
				ushort actual=tree.SplitMask(instance,planes,splits,63,1);
				Assert.That(actual&expected,Is.EqualTo(expected),$"query {query}: a visible primitive was discarded");
			}
		}
	}
}
