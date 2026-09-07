using Godot;
using System.Collections.Generic;

namespace PereSkyroom
{
	// Renders an isolated object-ID/depth pass. White pixels are visible target
	// surfaces; black pixels are background or regular scene occluders.
	public class AccentObjectDitherMode : CanvasLayer
	{
		private const string CompositeShader = @"
shader_type canvas_item;
render_mode unshaded;

uniform sampler2D blue_noise;
uniform vec4 dark_color : hint_color;
uniform vec4 regular_light_color : hint_color;
uniform vec4 accent_color : hint_color;
uniform bool global_dither_enabled = false;

void fragment() {
	vec4 screen = texture(SCREEN_TEXTURE, SCREEN_UV);
	float object_mask = texture(TEXTURE, UV).r;
	vec3 result = screen.rgb;
	if (object_mask > 0.5) {
		if (global_dither_enabled) {
			float light_distance = distance(screen.rgb, regular_light_color.rgb);
			float dark_distance = distance(screen.rgb, dark_color.rgb);
			if (light_distance <= dark_distance) {
				result = accent_color.rgb;
			}
		} else {
			float luminance = dot(screen.rgb, vec3(0.2126, 0.7152, 0.0722));
			vec2 screen_pixel = floor(SCREEN_UV / SCREEN_PIXEL_SIZE);
			vec2 noise_uv = (mod(screen_pixel, vec2(64.0)) + vec2(0.5)) / 64.0;
			float threshold = texture(blue_noise, noise_uv).r;
			result = luminance >= threshold ? accent_color.rgb : dark_color.rgb;
		}
	}
	COLOR = vec4(result, screen.a);
}";

		public List<ShootTarget> Targets;
		public PlayerController Player;
		public SideCameraMode SideCameraMode;
		public PanoramicFovMode PanoramicFovMode;
		public BlueNoiseDither Dither;
		public Color AccentColor = new Color(0.0f, 1.0f, 0.0f, 1.0f);
		public bool EnabledByDefault;
		public bool Enabled { get; private set; }
		public int DrawPassCount { get; private set; }

		private readonly Viewport[] _maskViewports = new Viewport[3];
		private readonly Camera[] _maskCameras = new Camera[3];
		private readonly TextureRect[] _compositeViews = new TextureRect[3];
		private readonly List<ProxyMesh> _proxies = new List<ProxyMesh>();
		private ShaderMaterial _compositeMaterial;

		public override void _Ready()
		{
			Layer = 1050;
			if (Dither != null && IsInstanceValid(Dither))
				Dither.AccentDitherMode = this;
			var sourceMeshes = new List<MeshInstance>();
			CollectSourceMeshes(GetParent(), sourceMeshes);

			var maskWorld = new World();
			for (int i = 0; i < 3; i++)
			{
				_maskViewports[i] = CreateMaskViewport("AccentMaskViewport" + i, maskWorld, out _maskCameras[i]);
				AddChild(_maskViewports[i]);
			}

			Spatial maskRoot = BuildMaskScene(sourceMeshes);
			_maskViewports[0].AddChild(maskRoot);
			BuildCompositeMaterial();
			for (int i = 0; i < 3; i++)
			{
				_compositeViews[i] = CreateCompositeView(
					"AccentComposite" + i, _maskViewports[i].GetTexture());
				AddChild(_compositeViews[i]);
			}
			SetEnabled(EnabledByDefault);
		}

		public override void _Process(float delta)
		{
			if (Enabled && (Dither == null || !IsInstanceValid(Dither) || !Dither.Enabled))
				SetEnabled(false);

			if (Input.IsActionJustPressed("toggle_accent_object_dither")
				&& Dither != null && IsInstanceValid(Dither) && Dither.Enabled)
				SetEnabled(!Enabled);
			if (!Enabled)
				return;

			UpdateProxyTransforms();
			UpdateLayoutAndCameras();
			_compositeMaterial.SetShaderParam("global_dither_enabled", Dither != null && Dither.Enabled);
			DrawPassCount++;
		}

		public void SetEnabled(bool enabled)
		{
			if (enabled && (Dither == null || !IsInstanceValid(Dither) || !Dither.Enabled))
				return;

			Enabled = enabled;
			if (!enabled)
			{
				for (int i = 0; i < 3; i++)
				{
					if (_compositeViews[i] != null) _compositeViews[i].Visible = false;
					if (_maskViewports[i] != null)
						_maskViewports[i].RenderTargetUpdateMode = Viewport.UpdateMode.Disabled;
				}
			}
			else if (_compositeViews[0] != null)
			{
				UpdateProxyTransforms();
				UpdateLayoutAndCameras();
			}
			if (Player != null && IsInstanceValid(Player))
				Player.ShowAccentDitherMessage(enabled);
		}

		private Viewport CreateMaskViewport(string name, World world, out Camera camera)
		{
			var viewport = new Viewport
			{
				Name = name,
				World = world,
				Usage = Viewport.UsageEnum.Usage3dNoEffects,
				RenderTargetVFlip = true,
				RenderTargetUpdateMode = Viewport.UpdateMode.Disabled,
				HandleInputLocally = false,
				Hdr = false
			};
			camera = new Camera { Name = name + "Camera", Current = true, Far = 200.0f };
			viewport.AddChild(camera);
			return viewport;
		}

		private Spatial BuildMaskScene(List<MeshInstance> sourceMeshes)
		{
			var root = new Spatial { Name = "AccentMaskScene" };
			var environmentNode = new WorldEnvironment { Name = "BlackMaskEnvironment" };
			environmentNode.Environment = new Environment
			{
				BackgroundMode = Environment.BGMode.Color,
				BackgroundColor = Colors.Black,
				BackgroundEnergy = 0.0f,
				AmbientLightEnergy = 0.0f
			};
			root.AddChild(environmentNode);

			SpatialMaterial blackMaterial = CreateMaskMaterial(Colors.Black);
			SpatialMaterial whiteMaterial = CreateMaskMaterial(Colors.White);
			foreach (MeshInstance source in sourceMeshes)
			{
				bool special = HasTargetAncestor(source);
				var proxy = new MeshInstance
				{
					Name = "Mask_" + source.Name,
					Mesh = source.Mesh,
					MaterialOverride = special ? whiteMaterial : blackMaterial,
					CastShadow = GeometryInstance.ShadowCastingSetting.Off
				};
				root.AddChild(proxy);
				_proxies.Add(new ProxyMesh { Source = source, Proxy = proxy });
			}
			return root;
		}

		private SpatialMaterial CreateMaskMaterial(Color color)
		{
			return new SpatialMaterial
			{
				AlbedoColor = color,
				FlagsUnshaded = true,
				ParamsCullMode = SpatialMaterial.CullMode.Disabled
			};
		}

		private void BuildCompositeMaterial()
		{
			_compositeMaterial = new ShaderMaterial { Shader = new Shader { Code = CompositeShader } };
			_compositeMaterial.SetShaderParam("blue_noise", Dither.BlueNoiseTexture);
			_compositeMaterial.SetShaderParam("dark_color", Dither.DarkColor);
			_compositeMaterial.SetShaderParam("regular_light_color", Dither.LightColor);
			_compositeMaterial.SetShaderParam("accent_color", AccentColor);
		}

		private TextureRect CreateCompositeView(string name, Texture maskTexture)
		{
			return new TextureRect
			{
				Name = name,
				Texture = maskTexture,
				Material = _compositeMaterial,
				AnchorBottom = 1.0f,
				Expand = true,
				StretchMode = TextureRect.StretchModeEnum.Scale,
				MouseFilter = Control.MouseFilterEnum.Ignore,
				Visible = false
			};
		}

		private void UpdateLayoutAndCameras()
		{
			int cameraCount = 1;
			if (PanoramicFovMode != null && PanoramicFovMode.IsRenderingActive()) cameraCount = 3;
			else if (SideCameraMode != null && SideCameraMode.IsEnabled()) cameraCount = 2;

			Vector2 screenSize = GetViewport().Size;
			for (int i = 0; i < 3; i++)
			{
				bool used = i < cameraCount;
				_compositeViews[i].Visible = used;
				_maskViewports[i].RenderTargetUpdateMode = used
					? Viewport.UpdateMode.Always
					: Viewport.UpdateMode.Disabled;
				if (!used) continue;

				float left = i / (float)cameraCount;
				float right = (i + 1) / (float)cameraCount;
				_compositeViews[i].AnchorLeft = left;
				_compositeViews[i].AnchorRight = right;
				Vector2 segmentSize = new Vector2(
					Mathf.Max(2.0f, Mathf.Ceil(screenSize.x / cameraCount)),
					Mathf.Max(2.0f, screenSize.y));
				_maskViewports[i].Size = segmentSize;
				CopyCamera(GetSourceCamera(cameraCount, i), _maskCameras[i]);
			}
		}

		private Camera GetSourceCamera(int cameraCount, int index)
		{
			if (cameraCount == 3) return PanoramicFovMode.GetCamera(index);
			if (cameraCount == 2) return SideCameraMode.GetCamera(index);
			return Player.GetViewCamera();
		}

		private void CopyCamera(Camera source, Camera destination)
		{
			destination.GlobalTransform = source.GlobalTransform;
			destination.Fov = source.Fov;
			destination.Near = source.Near;
			destination.Far = source.Far;
			destination.KeepAspect = source.KeepAspect;
		}

		private void UpdateProxyTransforms()
		{
			foreach (ProxyMesh item in _proxies)
			{
				bool valid = item.Source != null && IsInstanceValid(item.Source) && !item.Source.IsQueuedForDeletion();
				item.Proxy.Visible = valid && item.Source.Visible;
				if (valid) item.Proxy.GlobalTransform = item.Source.GlobalTransform;
			}
		}

		private void CollectSourceMeshes(Node node, List<MeshInstance> result)
		{
			var mesh = node as MeshInstance;
			if (mesh != null) result.Add(mesh);
			foreach (Node child in node.GetChildren())
				CollectSourceMeshes(child, result);
		}

		private bool HasTargetAncestor(Node node)
		{
			Node current = node;
			while (current != null)
			{
				if (current is ShootTarget) return true;
				current = current.GetParent();
			}
			return false;
		}

		private class ProxyMesh
		{
			public MeshInstance Source;
			public MeshInstance Proxy;
		}
	}
}
