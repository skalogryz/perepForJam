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
		public int LightObjectCount;
		public int PathObjectCount;
	}

	public static class BlendLevelLoader
	{
		private sealed class MeshBuildResult
		{
			public Mesh Mesh;
			public Shape CollisionShape;
			public Vector3 LocalOffset;
			public Shape BoundingBoxCollisionShape;
			public Vector3 BoundingBoxCollisionOffset;
		}

		private static readonly Color DefaultMaterialColor = new Color(0.32f, 0.48f, 0.62f, 1.0f);
		private static readonly Color PuzzleMaterialColor = new Color(0.0f, 1.0f, 0.0f, 1.0f);

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
			var hooksByNumber = new Dictionary<int, PlatformProps>();
			var movementPathObjects = new List<BlendObjectInfo>();
			Vector3 spawn = new Vector3(0, 2, 0);
			bool hasSpawn = false;
			bool hasCameraSpawn = false;
			int meshCount = 0;
			int lightCount = 0;

			foreach (BlendObjectInfo blendObject in objects)
			{
				if (blendObject.Name.StartsWith("IGNORE_", StringComparison.OrdinalIgnoreCase))
					continue;
				if (HasPrefix(blendObject.Name, "уз"))
				{
					movementPathObjects.Add(blendObject);
					continue;
				}

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

				/*if (blendObject.Light != null)
				{
					Light light = BuildLight(blendObject, objectTransform);
					if (light != null)
					{
						root.AddChild(light);
						lightCount++;
					}
				}*/

				if (blendObject.Mesh == null || blendObject.Mesh.Vertices == null
					|| blendObject.Mesh.Vertices.Count == 0)
					continue;

				MeshBuildResult builtMesh;
				if (!meshCache.TryGetValue(blendObject.Mesh.DataBlockAddress, out builtMesh))
				{
					builtMesh = BuildMesh(blendObject.Mesh, path, textureCache);
					meshCache.Add(blendObject.Mesh.DataBlockAddress, builtMesh);
				}

				bool isHook = HasPrefix(blendObject.Name, "кр", "hook");
				bool isPuzzle = HasPrefix(blendObject.Name, "вых", "puzzle");
				bool hasCollision = !HasPrefix(blendObject.Name, "дек");
				var body = new PlatformProps
				{
					Name = SafeNodeName(blendObject.Name),
					Transform = objectTransform,
					IsHook = isHook,
					SpecialVisibility = HasPrefix(blendObject.Name, "ск", "hid")
				};
				var meshInstance = new MeshInstance
				{
					Name = "Mesh",
					Mesh = builtMesh.Mesh,
					Translation = builtMesh.LocalOffset,
					MaterialOverride = isPuzzle ? BuildPuzzleMaterial() : null
				};
				body.AddChild(meshInstance);
				if (hasCollision)
				{
					var collision = new CollisionShape
					{
						Name = "CollisionShape",
						Shape = isHook
							? builtMesh.BoundingBoxCollisionShape
							: builtMesh.CollisionShape,
						Translation = isHook
							? builtMesh.BoundingBoxCollisionOffset
							: builtMesh.LocalOffset
					};
					body.AddChild(collision);
				}
				if (isPuzzle)
					AddPuzzleTriggerField(body, builtMesh);
				root.AddChild(body);
				int hookNumber;
				if (isHook && TryFindInteger(blendObject.Name, out hookNumber)
					&& !hooksByNumber.ContainsKey(hookNumber))
				{
					hooksByNumber.Add(hookNumber, body);
				}
				meshCount++;
			}

			int pathCount = BuildMovementPaths(
				root, movementPathObjects, hooksByNumber, worldTransformCache);

			if (meshCount == 0)
			{
				root.Free();
				throw new InvalidOperationException("The selected .blend file contains no readable mesh objects.");
			}

			return new LoadedBlendLevel
			{
				Root = root,
				PlayerSpawn = spawn,
				MeshObjectCount = meshCount,
				LightObjectCount = lightCount,
				PathObjectCount = pathCount
			};
		}

		private static void AddPuzzleTriggerField(
			PlatformProps body,
			MeshBuildResult builtMesh)
		{
			BoxShape sourceBounds = builtMesh.BoundingBoxCollisionShape as BoxShape;
			if (sourceBounds == null)
				return;

			var triggerField = new TriggerField
			{
				Name = "PuzzleTrigger",
				EventName = "puzzle_event",
				ActivateTarget = body
			};
			triggerField.AddChild(new CollisionShape
			{
				Name = "CollisionShape",
				Shape = new BoxShape { Extents = sourceBounds.Extents * 2.0f },
				Translation = builtMesh.BoundingBoxCollisionOffset
			});
			body.AddChild(triggerField);
		}

		private static SpatialMaterial BuildPuzzleMaterial()
		{
			return new SpatialMaterial
			{
				AlbedoColor = PuzzleMaterialColor,
				Roughness = 0.78f,
				Metallic = 0.05f,
				FlagsUnshaded = true
			};
		}

		private static int BuildMovementPaths(
			Spatial root,
			List<BlendObjectInfo> pathObjects,
			Dictionary<int, PlatformProps> hooksByNumber,
			Dictionary<BlendObjectInfo, Transform> worldTransformCache)
		{
			var assignedHooks = new HashSet<PlatformProps>();
			int pathCount = 0;
			for (int i = 0; i < pathObjects.Count; i++)
			{
				BlendObjectInfo pathObject = pathObjects[i];
				int objectNumber;
				PlatformProps hook;
				if (!TryFindInteger(pathObject.Name, out objectNumber)
					|| !hooksByNumber.TryGetValue(objectNumber, out hook)
					|| assignedHooks.Contains(hook))
				{
					continue;
				}

				Transform pathTransform = GetWorldTransform(
					pathObject, worldTransformCache, new HashSet<BlendObjectInfo>());
				Path movementPath = BuildMovementPath(pathObject, pathTransform);
				if (movementPath == null)
					continue;

				root.AddChild(movementPath);
				hook.MovementPath = hook.GetPathTo(movementPath);
				hook.IsMoveOnPath = true;
				assignedHooks.Add(hook);
				pathCount++;
			}
			return pathCount;
		}

		private static Path BuildMovementPath(
			BlendObjectInfo pathObject,
			Transform pathTransform)
		{
			BlendCurveInfo sourceCurve = pathObject.Curve;
			if (sourceCurve == null || sourceCurve.Splines == null)
				return null;

			for (int splineIndex = 0; splineIndex < sourceCurve.Splines.Count; splineIndex++)
			{
				BlendSplineInfo spline = sourceCurve.Splines[splineIndex];
				var curve = new Curve3D();
				int pointCount = 0;
				if (spline.Type == BlendSplineType.Bezier && spline.BezierPoints != null)
				{
					for (int i = 0; i < spline.BezierPoints.Count; i++)
					{
						BlendBezierPointInfo sourcePoint = spline.BezierPoints[i];
						Vector3 position = ConvertVertex(sourcePoint.Coordinate);
						Vector3 incoming = ConvertVertex(sourcePoint.LeftHandle) - position;
						Vector3 outgoing = ConvertVertex(sourcePoint.RightHandle) - position;
						curve.AddPoint(position, incoming, outgoing);
						pointCount++;
					}
					if (spline.IsCyclic && pointCount > 1)
					{
						BlendBezierPointInfo first = spline.BezierPoints[0];
						Vector3 position = ConvertVertex(first.Coordinate);
						curve.AddPoint(
							position,
							ConvertVertex(first.LeftHandle) - position,
							ConvertVertex(first.RightHandle) - position);
					}
				}
				else if (spline.Type == BlendSplineType.Poly && spline.Points != null)
				{
					for (int i = 0; i < spline.Points.Count; i++)
					{
						curve.AddPoint(ConvertVertex(spline.Points[i].Coordinate));
						pointCount++;
					}
					if (spline.IsCyclic && pointCount > 1)
						curve.AddPoint(ConvertVertex(spline.Points[0].Coordinate));
				}

				if (pointCount >= 2)
				{
					return new Path
					{
						Name = SafeNodeName(pathObject.Name),
						Transform = pathTransform,
						Curve = curve
					};
				}
			}
			return null;
		}

		private static Light BuildLight(BlendObjectInfo blendObject, Transform objectTransform)
		{
			BlendLightInfo source = blendObject.Light;
			if (source == null)
				return null;

			Light light;
			switch (source.Type)
			{
				case BlendLightType.Sun:
					light = new DirectionalLight();
					break;
				case BlendLightType.Spot:
					light = new SpotLight
					{
						SpotRange = GetLightRange(source),
						SpotAngle = Mathf.Clamp(source.SpotSizeDegrees * 0.5f, 0.1f, 89.9f),
						SpotAngleAttenuation = Mathf.Lerp(8.0f, 0.5f,
							Mathf.Clamp(source.SpotBlend, 0.0f, 1.0f))
					};
					break;
				case BlendLightType.Point:
				case BlendLightType.Area:
				default:
					light = new OmniLight
					{
						OmniRange = GetLightRange(source)
					};
					break;
			}

			BlendColor4 sourceColor = source.Color;
			light.Name = SafeNodeName(blendObject.Name);
			light.Transform = new Transform(
				objectTransform.basis.Orthonormalized(), objectTransform.origin);
			light.LightColor = new Color(sourceColor.R, sourceColor.G, sourceColor.B, 1.0f);
			light.LightEnergy = GetLightEnergy(source);
			light.LightSpecular = Mathf.Max(0.0f, source.SpecularFactor);
			light.ShadowEnabled = source.CastsShadow;
			return light;
		}

		private static float GetLightEnergy(BlendLightInfo source)
		{
			float energy = Mathf.Max(0.0f, source.Energy)
				* Mathf.Pow(2.0f, source.Exposure);
			// Blender point, spot and area energy is expressed as power. Godot 3
			// uses a unitless multiplier, whose practical default corresponds to
			// roughly 1000 W in a modern Blender scene.
			if (source.Type != BlendLightType.Sun)
				energy *= 0.001f;
			return energy;
		}

		private static float GetLightRange(BlendLightInfo source)
		{
			return source.CutoffDistance > 0.0f
				? source.CutoffDistance
				: 20.0f;
		}

		private static MeshBuildResult BuildMesh(
			BlendMeshInfo source,
			string blendPath,
			Dictionary<string, Texture> textureCache)
		{
			Vector3 boundsMinimum;
			Vector3 boundsMaximum;
			GetLocalBounds(source, out boundsMinimum, out boundsMaximum);
			Vector3 boundsSize = GetValidCollisionSize(boundsMaximum - boundsMinimum);
			Vector3 boundsOffset = (boundsMinimum + boundsMaximum) * 0.5f;
			var boundsCollision = new BoxShape { Extents = boundsSize * 0.5f };

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
					LocalOffset = Vector3.Zero,
					BoundingBoxCollisionShape = boundsCollision,
					BoundingBoxCollisionOffset = boundsOffset
				};
			}

			return new MeshBuildResult
			{
				Mesh = CreateFallbackBoxMesh(boundsSize, BuildMaterial(source, blendPath, textureCache)),
				CollisionShape = boundsCollision,
				LocalOffset = boundsOffset,
				BoundingBoxCollisionShape = boundsCollision,
				BoundingBoxCollisionOffset = boundsOffset
			};
		}

		private static void GetLocalBounds(
			BlendMeshInfo source,
			out Vector3 minimum,
			out Vector3 maximum)
		{
			minimum = ConvertVertex(source.Vertices[0]);
			maximum = minimum;
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
		}

		private static Vector3 GetValidCollisionSize(Vector3 size)
		{
			size.x = Mathf.Max(size.x, 0.1f);
			size.y = Mathf.Max(size.y, 0.1f);
			size.z = Mathf.Max(size.z, 0.1f);
			return size;
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
				if (name.StartsWith(prefixes[i], StringComparison.OrdinalIgnoreCase)
					|| name.StartsWith(prefixes[i], StringComparison.InvariantCultureIgnoreCase))
					return true;

			return false;
		}

		private static bool TryFindInteger(string name, out int value)
		{
			value = 0;
			if (string.IsNullOrEmpty(name))
				return false;

			for (int i = 0; i < name.Length; i++)
			{
				if (name[i] < '0' || name[i] > '9')
					continue;

				long parsed = 0;
				while (i < name.Length && name[i] >= '0' && name[i] <= '9')
				{
					parsed = parsed * 10 + name[i] - '0';
					if (parsed > int.MaxValue)
						return false;
					i++;
				}
				value = (int)parsed;
				return true;
			}

			return false;
		}
	}
}
