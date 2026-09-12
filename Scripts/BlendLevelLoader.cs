using BlenderReader;
using Godot;
using System;
using System.Collections.Generic;
using IOFile = System.IO.File;
using IOPath = System.IO.Path;

namespace PereSkyroom
{
	public sealed class LoadedBlendLevel
	{
		public Spatial Root;
		public Vector3 PlayerSpawn;
		public int MeshObjectCount;
	}

	public static class BlendLevelLoader
	{
		private sealed class MeshBuildResult
		{
			public Mesh Mesh;
			public Shape CollisionShape;
			public Vector3 LocalOffset;
		}

		private static readonly Color DefaultMaterialColor = new Color(0.32f, 0.48f, 0.62f, 1.0f);

		public static LoadedBlendLevel Load(string path)
		{
			BlendDocument document = new BlendFileReader().Read(path);
			IReadOnlyList<BlendObjectInfo> objects = document.Scenes.Count > 0
				? document.Scenes[0].Objects
				: document.Objects;

			var root = new Spatial { Name = "BlendLevel" };
			var meshCache = new Dictionary<ulong, MeshBuildResult>();
			var textureCache = new Dictionary<string, Texture>(StringComparer.OrdinalIgnoreCase);
			var worldTransformCache = new Dictionary<BlendObjectInfo, Transform>();
			Vector3 spawn = new Vector3(0, 2, 0);
			bool hasSpawn = false;
			bool hasCameraSpawn = false;
			int meshCount = 0;

			foreach (BlendObjectInfo blendObject in objects)
			{
				if (blendObject.Name.StartsWith("IGNORE_", StringComparison.OrdinalIgnoreCase))
					continue;

				Transform objectTransform = GetWorldTransform(
					blendObject, worldTransformCache, new HashSet<BlendObjectInfo>());
				Vector3 objectPosition = objectTransform.origin;
				if (blendObject.Name.StartsWith("SPAWN_", StringComparison.OrdinalIgnoreCase))
				{
					spawn = objectPosition;
					hasSpawn = true;
					continue;
				}

				if (!hasSpawn && !hasCameraSpawn && blendObject.Type == "Camera")
				{
					spawn = objectPosition;
					hasCameraSpawn = true;
				}

				if (blendObject.Mesh == null || blendObject.Mesh.Vertices == null
					|| blendObject.Mesh.Vertices.Count == 0)
					continue;

				MeshBuildResult builtMesh;
				if (!meshCache.TryGetValue(blendObject.Mesh.DataBlockAddress, out builtMesh))
				{
					builtMesh = BuildMesh(blendObject.Mesh, path, textureCache);
					meshCache.Add(blendObject.Mesh.DataBlockAddress, builtMesh);
				}

				var body = new PlatformProps
				{
					Name = SafeNodeName(blendObject.Name),
					Transform = objectTransform,
					IsHook = HasPrefix(blendObject.Name, "кр", "hook"),
					SpecialVisibility = HasPrefix(blendObject.Name, "ск", "hid")
				};
				var meshInstance = new MeshInstance
				{
					Name = "Mesh",
					Mesh = builtMesh.Mesh,
					Translation = builtMesh.LocalOffset
				};
				var collision = new CollisionShape
				{
					Name = "CollisionShape",
					Shape = builtMesh.CollisionShape,
					Translation = builtMesh.LocalOffset
				};
				body.AddChild(meshInstance);
				body.AddChild(collision);
				root.AddChild(body);
				meshCount++;
			}

			if (meshCount == 0)
			{
				root.Free();
				throw new InvalidOperationException("The selected .blend file contains no readable mesh objects.");
			}

			return new LoadedBlendLevel
			{
				Root = root,
				PlayerSpawn = spawn,
				MeshObjectCount = meshCount
			};
		}

		private static MeshBuildResult BuildMesh(
			BlendMeshInfo source,
			string blendPath,
			Dictionary<string, Texture> textureCache)
		{
			if (source.TriangleIndices != null && source.TriangleIndices.Count >= 3)
			{
				var surface = new SurfaceTool();
				surface.Begin(Mesh.PrimitiveType.Triangles);
				BlendUvMapInfo uvMap = SelectUvMap(source);
				bool hasCornerData = source.TriangleCornerIndices != null
					&& source.TriangleCornerIndices.Count == source.TriangleIndices.Count;
				bool hasCornerNormals = hasCornerData && source.CornerNormals != null
					&& source.CornerNormals.Count == source.CornerCount;
				bool hasVertexNormals = source.VertexNormals != null
					&& source.VertexNormals.Count == source.Vertices.Count;
				bool hasUvs = hasCornerData && uvMap != null && uvMap.Coordinates != null
					&& uvMap.Coordinates.Count == source.CornerCount;

				for (int triangleStart = 0; triangleStart < source.TriangleIndices.Count; triangleStart += 3)
				{
					for (int outputCorner = 0; outputCorner < 3; outputCorner++)
					{
						int sourceCorner = outputCorner == 1 ? 2 : outputCorner == 2 ? 1 : 0;
						int triangleElement = triangleStart + sourceCorner;
						int vertexIndex = source.TriangleIndices[triangleElement];
						if (vertexIndex < 0 || vertexIndex >= source.Vertices.Count)
							throw new InvalidOperationException("The .blend mesh contains an invalid vertex index.");

						int cornerIndex = hasCornerData ? source.TriangleCornerIndices[triangleElement] : -1;
						if (hasCornerNormals)
						{
							if (cornerIndex < 0 || cornerIndex >= source.CornerNormals.Count)
								throw new InvalidOperationException("The .blend mesh contains an invalid corner index.");
							surface.AddNormal(ConvertDirection(source.CornerNormals[cornerIndex]));
						}
						else if (hasVertexNormals)
							surface.AddNormal(ConvertDirection(source.VertexNormals[vertexIndex]));

						if (hasUvs)
						{
							BlendVector2 uv = uvMap.Coordinates[cornerIndex];
							surface.AddUv(new Vector2(uv.X, 1.0f - uv.Y));
						}
						surface.AddVertex(ConvertVertex(source.Vertices[vertexIndex]));
					}
				}
				if (!hasCornerNormals && !hasVertexNormals)
					surface.GenerateNormals();
				ArrayMesh mesh = surface.Commit();
				mesh.SurfaceSetMaterial(0, BuildMaterial(source, blendPath, textureCache));
				return new MeshBuildResult
				{
					Mesh = mesh,
					CollisionShape = mesh.CreateTrimeshShape(),
					LocalOffset = Vector3.Zero
				};
			}

			Vector3 minimum = ConvertVertex(source.Vertices[0]);
			Vector3 maximum = minimum;
			for (int i = 1; i < source.Vertices.Count; i++)
			{
				Vector3 vertex = ConvertVertex(source.Vertices[i]);
				minimum.x = Mathf.Min(minimum.x, vertex.x);
				minimum.y = Mathf.Min(minimum.y, vertex.y);
				minimum.z = Mathf.Min(minimum.z, vertex.z);
				maximum.x = Mathf.Max(maximum.x, vertex.x);
				maximum.y = Mathf.Max(maximum.y, vertex.y);
				maximum.z = Mathf.Max(maximum.z, vertex.z);
			}

			Vector3 size = maximum - minimum;
			size.x = Mathf.Max(size.x, 0.1f);
			size.y = Mathf.Max(size.y, 0.1f);
			size.z = Mathf.Max(size.z, 0.1f);
			return new MeshBuildResult
			{
				Mesh = CreateFallbackBoxMesh(size, BuildMaterial(source, blendPath, textureCache)),
				CollisionShape = new BoxShape { Extents = size * 0.5f },
				LocalOffset = (minimum + maximum) * 0.5f
			};
		}

		private static CubeMesh CreateFallbackBoxMesh(Vector3 size, Material material)
		{
			return new CubeMesh { Size = size, Material = material };
		}

		private static BlendUvMapInfo SelectUvMap(BlendMeshInfo mesh)
		{
			if (mesh.UvMaps == null || mesh.UvMaps.Count == 0)
				return null;
			for (int i = 0; i < mesh.UvMaps.Count; i++)
				if (mesh.UvMaps[i] != null && mesh.UvMaps[i].Name == "UVMap")
					return mesh.UvMaps[i];
			return mesh.UvMaps[0];
		}

		private static SpatialMaterial BuildMaterial(
			BlendMeshInfo mesh,
			string blendPath,
			Dictionary<string, Texture> textureCache)
		{
			BlendMaterialInfo source = null;
			if (mesh.Materials != null)
				for (int i = 0; i < mesh.Materials.Count && source == null; i++)
					source = mesh.Materials[i];

			var material = new SpatialMaterial
			{
				AlbedoColor = DefaultMaterialColor,
				Roughness = 0.78f,
				Metallic = 0.05f
			};
			if (source == null)
				return material;

			float alpha = Mathf.Clamp(source.Alpha, 0.0f, 1.0f);
			material.ResourceName = source.Name;
			material.AlbedoColor = new Color(
				source.BaseColor.R, source.BaseColor.G, source.BaseColor.B, alpha);
			material.Metallic = Mathf.Clamp(source.Metallic, 0.0f, 1.0f);
			material.Roughness = Mathf.Clamp(source.Roughness, 0.0f, 1.0f);
			material.FlagsTransparent = alpha < 0.999f;

			Texture texture = LoadFirstExternalTexture(source, blendPath, textureCache);
			if (texture != null)
				material.AlbedoTexture = texture;
			return material;
		}

		private static Texture LoadFirstExternalTexture(
			BlendMaterialInfo material,
			string blendPath,
			Dictionary<string, Texture> textureCache)
		{
			if (material.Textures == null)
				return null;
			for (int i = 0; i < material.Textures.Count; i++)
			{
				BlendTextureReference reference = material.Textures[i];
				if (reference == null || reference.Storage != BlendImageStorage.External
					|| string.IsNullOrEmpty(reference.FilePath))
					continue;

				string path = ResolveTexturePath(blendPath, reference.FilePath);
				Texture cached;
				if (textureCache.TryGetValue(path, out cached))
					return cached;
				if (!IOFile.Exists(path))
					continue;

				var image = new Image();
				if (image.Load(path) != Error.Ok)
					continue;
				var texture = new ImageTexture();
				texture.CreateFromImage(image);
				textureCache[path] = texture;
				return texture;
			}
			return null;
		}

		private static string ResolveTexturePath(string blendPath, string texturePath)
		{
			string normalized = texturePath.Replace('/', IOPath.DirectorySeparatorChar);
			string blendDirectory = IOPath.GetDirectoryName(IOPath.GetFullPath(blendPath));
			if (normalized.StartsWith("//", StringComparison.Ordinal)
				|| normalized.StartsWith("\\\\", StringComparison.Ordinal))
				normalized = normalized.Substring(2);
			if (!IOPath.IsPathRooted(normalized))
				normalized = IOPath.Combine(blendDirectory, normalized);
			return IOPath.GetFullPath(normalized);
		}

		private static Transform GetWorldTransform(
			BlendObjectInfo blendObject,
			Dictionary<BlendObjectInfo, Transform> cache,
			HashSet<BlendObjectInfo> active)
		{
			Transform cached;
			if (cache.TryGetValue(blendObject, out cached))
				return cached;
			if (!active.Add(blendObject))
				throw new InvalidOperationException("The .blend object hierarchy contains a cycle.");

			Transform result = ConvertTransform(blendObject.Transform);
			if (blendObject.Parent != null)
				result = GetWorldTransform(blendObject.Parent, cache, active) * result;
			active.Remove(blendObject);
			cache[blendObject] = result;
			return result;
		}

		private static Transform ConvertTransform(BlendTransform source)
		{
			Basis rotation = new Basis(Vector3.Up, source.RotationZ)
				* new Basis(new Vector3(0, 0, -1), source.RotationY)
				* new Basis(Vector3.Right, source.RotationX);
			Vector3 scale = new Vector3(source.ScaleX, source.ScaleZ, source.ScaleY);
			return new Transform(rotation.Scaled(scale), ConvertPosition(source));
		}

		private static Vector3 ConvertPosition(BlendTransform value)
		{
			return new Vector3(value.X, value.Z, -value.Y);
		}

		private static Vector3 ConvertVertex(BlendVector3 value)
		{
			return new Vector3(value.X, value.Z, -value.Y);
		}

		private static Vector3 ConvertDirection(BlendVector3 value)
		{
			return ConvertVertex(value).Normalized();
		}

		private static string SafeNodeName(string name)
		{
			return string.IsNullOrEmpty(name) ? "BlendObject" : name.Replace("/", "_");
		}

		private static bool HasPrefix(string name, params string[] prefixes)
		{
			if (string.IsNullOrEmpty(name))
				return false;

			for (int i = 0; i < prefixes.Length; i++)
				if (name.StartsWith(prefixes[i], StringComparison.OrdinalIgnoreCase))
					return true;

			return false;
		}
	}
}
