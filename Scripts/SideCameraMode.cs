using Godot;

namespace PereSkyroom
{
	public class SideCameraMode : CanvasLayer
	{
		public PlayerController Player;
		public float CameraAngleDegrees = 90.0f;
		public bool SmoothTransitionEnabled = true;
		public float TransitionDurationSeconds = 0.5f;

		private Viewport _leftViewport;
		private Viewport _rightViewport;
		private Camera _leftCamera;
		private Camera _rightCamera;
		private TextureRect _leftView;
		private TextureRect _rightView;
		private bool _enabled;
		private float _transitionElapsed;

		public override void _Ready()
		{
			Layer = 500;
			_leftViewport = CreateCameraViewport("LeftViewport", out _leftCamera);
			_rightViewport = CreateCameraViewport("RightViewport", out _rightCamera);
			AddChild(_leftViewport);
			AddChild(_rightViewport);

			_leftView = CreateScreenHalf("LeftView", _leftViewport.GetTexture(), 0.0f, 0.5f);
			_rightView = CreateScreenHalf("RightView", _rightViewport.GetTexture(), 0.5f, 1.0f);
			AddChild(_leftView);
			AddChild(_rightView);
			ResizeViewports();
		}

		public override void _Process(float delta)
		{
			if (Input.IsActionJustPressed("toggle_side_cameras"))
				SetEnabled(!_enabled);

			if (!_enabled || Player == null || !IsInstanceValid(Player))
				return;

			ResizeViewports();
			if (SmoothTransitionEnabled)
				_transitionElapsed = Mathf.Min(_transitionElapsed + delta, Mathf.Max(TransitionDurationSeconds, 0.001f));
			UpdateCameraTransforms();
		}

		public void SetEnabled(bool enabled, bool notifyPlayer = true)
		{
			_enabled = enabled;
			_transitionElapsed = enabled && SmoothTransitionEnabled ? 0.0f : Mathf.Max(TransitionDurationSeconds, 0.001f);
			_leftView.Visible = enabled;
			_rightView.Visible = enabled;
			_leftViewport.RenderTargetUpdateMode = enabled ? Viewport.UpdateMode.Always : Viewport.UpdateMode.Disabled;
			_rightViewport.RenderTargetUpdateMode = enabled ? Viewport.UpdateMode.Always : Viewport.UpdateMode.Disabled;

			if (enabled)
			{
				ResizeViewports();
				UpdateCameraTransforms();
			}
			if (Player != null && IsInstanceValid(Player))
				Player.SetSideCameraMode(enabled, notifyPlayer);
		}

		public bool IsEnabled()
		{
			return _enabled;
		}

		public Camera GetCamera(int index)
		{
			return index == 0 ? _leftCamera : _rightCamera;
		}

		private Viewport CreateCameraViewport(string name, out Camera camera)
		{
			var viewport = new Viewport
			{
				Name = name,
				World = Player.GetWorld(),
				Usage = Viewport.UsageEnum.Usage3d,
				RenderTargetVFlip = true,
				RenderTargetUpdateMode = Viewport.UpdateMode.Disabled,
				HandleInputLocally = false,
				Hdr = true
			};
			camera = new Camera
			{
				Name = name + "Camera",
				Current = true,
				Fov = 78.0f,
				Far = 200.0f,
				CullMask = PlayerController.SceneCameraCullMask
			};
			viewport.AddChild(camera);
			return viewport;
		}

		private TextureRect CreateScreenHalf(string name, Texture texture, float leftAnchor, float rightAnchor)
		{
			return new TextureRect
			{
				Name = name,
				Texture = texture,
				AnchorLeft = leftAnchor,
				AnchorRight = rightAnchor,
				AnchorBottom = 1.0f,
				Expand = true,
				StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
				MouseFilter = Control.MouseFilterEnum.Ignore,
				Visible = false
			};
		}

		private void ResizeViewports()
		{
			Vector2 screenSize = GetViewport().Size;
			Vector2 halfSize = new Vector2(Mathf.Max(2.0f, Mathf.Floor(screenSize.x * 0.5f)), Mathf.Max(2.0f, screenSize.y));
			if (_leftViewport.Size != halfSize)
			{
				_leftViewport.Size = halfSize;
				_rightViewport.Size = halfSize;
			}
		}

		private void UpdateCameraTransforms()
		{
			Transform source = Player.GetViewTransform();
			float targetAngle = Mathf.Clamp(Mathf.Abs(CameraAngleDegrees), 0.0f, 180.0f);
			float angle = targetAngle;
			if (SmoothTransitionEnabled)
			{
				float duration = Mathf.Max(TransitionDurationSeconds, 0.001f);
				float progress = Mathf.Clamp(_transitionElapsed / duration, 0.0f, 1.0f);
				float easedProgress = progress * progress * (3.0f - 2.0f * progress);
				angle = targetAngle * easedProgress;
			}
			_leftCamera.GlobalTransform = RotateView(source, Mathf.Deg2Rad(angle));
			_rightCamera.GlobalTransform = RotateView(source, Mathf.Deg2Rad(-angle));
		}

		private Transform RotateView(Transform source, float yaw)
		{
			Basis rotation = new Basis(Vector3.Up, yaw);
			return new Transform(rotation * source.basis, source.origin);
		}
	}
}
