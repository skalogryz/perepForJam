using Godot;

namespace PereSkyroom
{
	// A single perspective projection cannot represent 270 degrees. This mode
	// stitches three contiguous perspective views into one panoramic image.
	public class PanoramicFovMode : CanvasLayer
	{
		public PlayerController Player;
		public float TargetFovDegrees = 270.0f;
		public float TransitionDurationSeconds = 0.5f;
		public int ShadowAtlasResolution = 2048;

		private readonly Viewport[] _viewports = new Viewport[3];
		private readonly Camera[] _cameras = new Camera[3];
		private readonly TextureRect[] _views = new TextureRect[3];
		private bool _targetEnabled;
		private float _blend;

		public override void _Ready()
		{
			Layer = 600;
			for (int i = 0; i < 3; i++)
			{
				_viewports[i] = CreateViewport("PanoramicViewport" + i, out _cameras[i]);
				AddChild(_viewports[i]);
				_views[i] = CreateView("PanoramicView" + i, _viewports[i].GetTexture(), i / 3.0f, (i + 1) / 3.0f);
				AddChild(_views[i]);
			}
			ResizeViewports();
		}

		public override void _Process(float delta)
		{
			if (Input.IsActionJustPressed("toggle_panoramic_fov"))
				SetEnabled(!_targetEnabled);

			float duration = Mathf.Max(TransitionDurationSeconds, 0.001f);
			float destination = _targetEnabled ? 1.0f : 0.0f;
			_blend = Mathf.MoveToward(_blend, destination, delta / duration);

			if (_blend <= 0.0f && !_targetEnabled)
			{
				SetRenderingActive(false);
				return;
			}

			ResizeViewports();
			UpdatePanorama();
		}

		public void SetEnabled(bool enabled, bool notifyPlayer = true)
		{
			_targetEnabled = enabled;
			if (enabled)
			{
				SetRenderingActive(true);
				ResizeViewports();
				UpdatePanorama();
			}
			if (notifyPlayer && Player != null && IsInstanceValid(Player))
				Player.ShowPanoramicFovMessage(enabled, TargetFovDegrees);
		}

		public bool IsEnabled()
		{
			return _targetEnabled;
		}

		public bool IsRenderingActive()
		{
			return _targetEnabled || _blend > 0.0f;
		}

		public Camera GetCamera(int index)
		{
			return _cameras[Mathf.Clamp(index, 0, 2)];
		}

		private Viewport CreateViewport(string name, out Camera camera)
		{
			var viewport = new Viewport
			{
				Name = name,
				World = Player.GetWorld(),
				Usage = Viewport.UsageEnum.Usage3d,
				RenderTargetVFlip = true,
				RenderTargetUpdateMode = Viewport.UpdateMode.Disabled,
				HandleInputLocally = false,
				Hdr = true,
				ShadowAtlasSize = Mathf.Max(0, ShadowAtlasResolution)
			};
			camera = new Camera
			{
				Name = name + "Camera",
				Current = true,
				Far = 200.0f,
				KeepAspect = Camera.KeepAspectEnum.Width,
				CullMask = PlayerController.SceneCameraCullMask
			};
			viewport.AddChild(camera);
			return viewport;
		}

		private TextureRect CreateView(string name, Texture texture, float leftAnchor, float rightAnchor)
		{
			return new TextureRect
			{
				Name = name,
				Texture = texture,
				AnchorLeft = leftAnchor,
				AnchorRight = rightAnchor,
				AnchorBottom = 1.0f,
				Expand = true,
				StretchMode = TextureRect.StretchModeEnum.Scale,
				MouseFilter = Control.MouseFilterEnum.Ignore,
				Visible = false
			};
		}

		private void SetRenderingActive(bool active)
		{
			for (int i = 0; i < 3; i++)
			{
				_views[i].Visible = active;
				_viewports[i].RenderTargetUpdateMode = active
					? Viewport.UpdateMode.Always
					: Viewport.UpdateMode.Disabled;
			}
		}

		private void ResizeViewports()
		{
			Vector2 screenSize = GetViewport().Size;
			Vector2 thirdSize = new Vector2(
				Mathf.Max(2.0f, Mathf.Ceil(screenSize.x / 3.0f)),
				Mathf.Max(2.0f, screenSize.y));
			for (int i = 0; i < 3; i++)
			{
				if (_viewports[i].Size != thirdSize)
					_viewports[i].Size = thirdSize;
			}
		}

		private void UpdatePanorama()
		{
			if (Player == null || !IsInstanceValid(Player))
				return;

			float easedBlend = _blend * _blend * (3.0f - 2.0f * _blend);
			float normalFov = Player.GetNormalFovDegrees();
			float targetFov = Mathf.Clamp(Mathf.Abs(TargetFovDegrees), normalFov, 359.0f);
			float panoramicFov = Mathf.Lerp(normalFov, targetFov, easedBlend);
			float segmentFov = Mathf.Clamp(panoramicFov / 3.0f, 1.0f, 120.0f);
			Transform source = Player.GetViewTransform();

			_cameras[0].Fov = segmentFov;
			_cameras[1].Fov = segmentFov;
			_cameras[2].Fov = segmentFov;
			_cameras[0].GlobalTransform = RotateView(source, Mathf.Deg2Rad(segmentFov));
			_cameras[1].GlobalTransform = source;
			_cameras[2].GlobalTransform = RotateView(source, Mathf.Deg2Rad(-segmentFov));
		}

		private Transform RotateView(Transform source, float yaw)
		{
			Basis rotation = new Basis(Vector3.Up, yaw);
			return new Transform(rotation * source.basis, source.origin);
		}
	}
}
