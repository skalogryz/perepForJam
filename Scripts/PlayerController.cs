using Godot;

namespace PereSkyroom
{
    public class PlayerController : KinematicBody
    {
		public const uint WeaponVisualLayer = 1u << 19;
		public const uint SceneCameraCullMask = uint.MaxValue ^ WeaponVisualLayer;

        private const float WalkSpeed = 7.0f;
        private const float FlySpeed = 10.0f;
        private const float JumpSpeed = 8.2f;
        private const float Gravity = 22.0f;
        private const float MouseSensitivity = 0.10f;

        private Spatial _head;
		private Camera _camera;
		private MeshInstance _gun;
		private Viewport _weaponViewport;
		private Camera _weaponCamera;
		private CanvasLayer _weaponOverlay;
		private TextureRect _weaponView;
		private CanvasLayer _hud;
        private Label _modeLabel;
        private Label _scoreLabel;
		private Label _ditherLabel;
		private Label _sideCameraLabel;
        private Label _messageLabel;
		private Label _fpsLabel;
        private Vector3 _velocity = Vector3.Zero;
        private float _pitch;
		private bool _flightMode;
		private bool _ditherEnabled;
		private bool _ditherPaletteInverted;
		private int _score;
		private bool _fpsVisible;
		private float _fpsRefreshTimer;

        public override void _Ready()
        {
            BuildBody();
            BindHud();
            Input.MouseMode = Input.MouseModeEnum.Captured;
            UpdateHud();
        }

        public override void _UnhandledInput(InputEvent inputEvent)
        {
            var mouseMotion = inputEvent as InputEventMouseMotion;
            if (mouseMotion != null && Input.MouseMode == Input.MouseModeEnum.Captured)
            {
                RotateY(Mathf.Deg2Rad(-mouseMotion.Relative.x * MouseSensitivity));
                _pitch = Mathf.Clamp(_pitch - mouseMotion.Relative.y * MouseSensitivity, -88.0f, 88.0f);
                _head.RotationDegrees = new Vector3(_pitch, 0, 0);
            }

            var key = inputEvent as InputEventKey;
			if (key != null && key.Pressed && !key.Echo)
			{
				if (key.Scancode == (uint)KeyList.Escape)
				{
					Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
						? Input.MouseModeEnum.Visible
						: Input.MouseModeEnum.Captured;
				}
				else if (key.Scancode == (uint)KeyList.F3)
				{
					_fpsVisible = !_fpsVisible;
					_fpsLabel.Visible = _fpsVisible;
					_fpsRefreshTimer = 0.0f;
				}
			}

            var mouseButton = inputEvent as InputEventMouseButton;
            if (mouseButton != null && mouseButton.Pressed && mouseButton.ButtonIndex == (int)ButtonList.Left)
            {
                if (Input.MouseMode == Input.MouseModeEnum.Visible)
                    Input.MouseMode = Input.MouseModeEnum.Captured;
            }
        }

        public override void _PhysicsProcess(float delta)
        {
            if (Input.IsActionJustPressed("toggle_flight"))
            {
                _flightMode = !_flightMode;
                _velocity = Vector3.Zero;
                UpdateHud();
                ShowMessage(_flightMode ? "FLIGHT ENABLED" : "FLIGHT DISABLED");
            }

			if (Input.IsActionJustPressed("toggle_weapon_visibility"))
			{
				SetWeaponVisible(!_weaponView.Visible);
				ShowMessage(_weaponView.Visible ? "WEAPON SHOWN" : "WEAPON HIDDEN");
			}

			if (Input.IsActionJustPressed("toggle_hud_labels"))
				_hud.Visible = !_hud.Visible;

            Vector2 input = new Vector2(
                Input.GetActionStrength("move_right") - Input.GetActionStrength("move_left"),
                Input.GetActionStrength("move_backward") - Input.GetActionStrength("move_forward"));
            if (input.Length() > 1.0f)
                input = input.Normalized();

            Vector3 wishDirection = (GlobalTransform.basis.x * input.x + GlobalTransform.basis.z * input.y).Normalized();
            float speed = _flightMode ? FlySpeed : WalkSpeed;
            _velocity.x = wishDirection.x * speed;
            _velocity.z = wishDirection.z * speed;

            if (_flightMode)
            {
                float vertical = Input.GetActionStrength("jump_or_up") - Input.GetActionStrength("fly_down");
                _velocity.y = vertical * FlySpeed;
                _velocity = MoveAndSlide(_velocity, Vector3.Up, false, 4, Mathf.Deg2Rad(55.0f));
            }
            else
            {
                if (IsOnFloor() && Input.IsActionJustPressed("jump_or_up"))
                    _velocity.y = JumpSpeed;
                else
                    _velocity.y -= Gravity * delta;
                _velocity = MoveAndSlide(_velocity, Vector3.Up, true, 4, Mathf.Deg2Rad(55.0f));
            }

            if (Input.IsActionJustPressed("shoot") && Input.MouseMode == Input.MouseModeEnum.Captured)
                Shoot();

            if (Translation.y < -15.0f)
            {
                Translation = new Vector3(0, 2, 8);
                _velocity = Vector3.Zero;
            }
        }

		public override void _Process(float delta)
		{
			UpdateWeaponCamera();
			ResizeWeaponViewport();
			if (!_fpsVisible)
				return;

			_fpsRefreshTimer -= delta;
			if (_fpsRefreshTimer <= 0.0f)
			{
				_fpsLabel.Text = "FPS: " + Engine.GetFramesPerSecond();
				_fpsRefreshTimer = 0.2f;
			}
		}

        private void BuildBody()
        {
            var collider = new CollisionShape
            {
                Shape = new CapsuleShape { Radius = 0.42f, Height = 1.05f },
                Translation = new Vector3(0, 0.95f, 0)
            };
            AddChild(collider);

            _head = new Spatial { Name = "Head", Translation = new Vector3(0, 1.55f, 0) };
            AddChild(_head);
			_camera = new Camera
			{
				Name = "Camera",
				Current = true,
				Fov = 78.0f,
				Far = 200.0f,
				KeepAspect = Camera.KeepAspectEnum.Width,
				CullMask = SceneCameraCullMask
			};
            _head.AddChild(_camera);
			BuildWeaponView();
		}

		private void BuildWeaponView()
		{
			_weaponViewport = new Viewport
			{
				Name = "WeaponViewport",
				World = GetWorld(),
				Usage = Viewport.UsageEnum.Usage3dNoEffects,
				TransparentBg = true,
				RenderTargetVFlip = true,
				RenderTargetUpdateMode = Viewport.UpdateMode.Always,
				HandleInputLocally = false,
				Hdr = false
			};
			AddChild(_weaponViewport);

			_weaponCamera = new Camera
			{
				Name = "WeaponCamera",
				Current = true,
				Fov = _camera.Fov,
				Near = 0.01f,
				Far = 10.0f,
				KeepAspect = _camera.KeepAspect,
				CullMask = WeaponVisualLayer
			};
			_weaponViewport.AddChild(_weaponCamera);

			_gun = new MeshInstance
			{
				Name = "Blaster",
				Mesh = new CubeMesh { Size = new Vector3(0.22f, 0.18f, 0.7f) },
				Translation = new Vector3(0.42f, -0.32f, -0.75f),
				Layers = WeaponVisualLayer,
                MaterialOverride = new SpatialMaterial
                {
                    AlbedoColor = new Color(0.08f, 0.1f, 0.14f),
                    Metallic = 0.8f,
                    Roughness = 0.25f
				}
			};
			_camera.AddChild(_gun);

			_weaponOverlay = new CanvasLayer { Name = "WeaponOverlay", Layer = 700 };
			AddChild(_weaponOverlay);
			_weaponView = new TextureRect
			{
				Name = "WeaponView",
				Texture = _weaponViewport.GetTexture(),
				AnchorRight = 1.0f,
				AnchorBottom = 1.0f,
				Expand = true,
				StretchMode = TextureRect.StretchModeEnum.Scale,
				MouseFilter = Control.MouseFilterEnum.Ignore
			};
			_weaponOverlay.AddChild(_weaponView);
			UpdateWeaponCamera();
			ResizeWeaponViewport();
		}

		private void UpdateWeaponCamera()
		{
			if (_weaponCamera == null || _camera == null)
				return;
			_weaponCamera.GlobalTransform = _camera.GlobalTransform;
			_weaponCamera.Fov = _camera.Fov;
			_weaponCamera.KeepAspect = _camera.KeepAspect;
		}

		private void ResizeWeaponViewport()
		{
			if (_weaponViewport == null)
				return;
			Vector2 screenSize = GetViewport().Size;
			if (_weaponViewport.Size != screenSize)
				_weaponViewport.Size = screenSize;
		}

		private void SetWeaponVisible(bool visible)
		{
			_gun.Visible = visible;
			_weaponView.Visible = visible;
			_weaponViewport.RenderTargetUpdateMode = visible
				? Viewport.UpdateMode.Always
				: Viewport.UpdateMode.Disabled;
		}

        private void BindHud()
        {
			Node hudOwner = GetParent();
			_hud = hudOwner.GetNode<CanvasLayer>("HUD");
			_modeLabel = _hud.GetNode<Label>("ModeLabel");
			_scoreLabel = _hud.GetNode<Label>("ScoreLabel");
			_ditherLabel = _hud.GetNode<Label>("DitherLabel");
			_sideCameraLabel = _hud.GetNode<Label>("SideCameraLabel");
			_messageLabel = _hud.GetNode<Label>("MessageLabel");
			_fpsLabel = _hud.GetNode<Label>("FpsLabel");
			_fpsVisible = _fpsLabel.Visible;
        }

        private void Shoot()
        {
            Vector3 from = _camera.GlobalTransform.origin;
            Vector3 to = from + (-_camera.GlobalTransform.basis.z * 100.0f);
            var exclude = new Godot.Collections.Array { this };
            Godot.Collections.Dictionary hit = GetWorld().DirectSpaceState.IntersectRay(from, to, exclude);
            if (hit.Count == 0)
            {
                ShowMessage("MISS");
                return;
            }

            var target = hit["collider"] as ShootTarget;
            if (target != null)
            {
                target.Hit();
                _score++;
                UpdateHud();
                ShowMessage("TARGET HIT!");
            }
            else
            {
                ShowMessage("IMPACT");
            }
        }

        private async void ShowMessage(string text)
        {
            _messageLabel.Text = text;
            await ToSignal(GetTree().CreateTimer(0.7f), "timeout");
            if (IsInstanceValid(_messageLabel) && _messageLabel.Text == text)
                _messageLabel.Text = string.Empty;
        }

        private void UpdateHud()
        {
            _modeLabel.Text = _flightMode ? "MODE: FLIGHT" : "MODE: WALK";
            _modeLabel.Modulate = _flightMode ? new Color(0.45f, 0.95f, 1.0f) : new Color(0.65f, 1.0f, 0.65f);
            _scoreLabel.Text = "TARGETS HIT: " + _score + " / 5";
        }

		public void SetDitherMode(bool enabled)
		{
			_ditherEnabled = enabled;
			UpdateDitherLabel();
			_ditherLabel.Modulate = enabled ? new Color(1.0f, 0.86f, 0.42f) : new Color(0.72f, 0.76f, 0.84f);
			ShowMessage(enabled ? "2-COLOR DITHER ENABLED" : "2-COLOR DITHER DISABLED");
		}

		public void SetDitherPaletteInverted(bool inverted)
		{
			_ditherPaletteInverted = inverted;
			UpdateDitherLabel();
			ShowMessage(inverted ? "DITHER PALETTE INVERTED" : "DITHER PALETTE NORMAL");
		}

		private void UpdateDitherLabel()
		{
			if (!_ditherEnabled)
			{
				_ditherLabel.Text = "DITHER: OFF";
				return;
			}
			_ditherLabel.Text = _ditherPaletteInverted
				? "DITHER: BLUE-NOISE / INVERTED"
				: "DITHER: BLUE-NOISE / 2 COLORS";
		}

		public Transform GetViewTransform()
		{
			return _camera.GlobalTransform;
		}

		public Camera GetViewCamera()
		{
			return _camera;
		}

		public float GetNormalFovDegrees()
		{
			return _camera.Fov;
		}

		public void ShowPanoramicFovMessage(bool enabled, float targetFov)
		{
			ShowMessage(enabled ? "PANORAMIC FOV " + Mathf.Round(targetFov) + "°" : "NORMAL FOV");
		}

		public void ShowAccentDitherMessage(bool enabled)
		{
			ShowMessage(enabled ? "ACCENT OBJECT DITHER ENABLED" : "ACCENT OBJECT DITHER DISABLED");
		}

		public void SetSideCameraMode(bool enabled)
		{
			_sideCameraLabel.Text = enabled ? "CAMERAS: SIDE VIEWS" : "CAMERAS: FORWARD";
			_sideCameraLabel.Modulate = enabled ? new Color(0.48f, 0.9f, 1.0f) : new Color(0.72f, 0.76f, 0.84f);
			ShowMessage(enabled ? "SIDE CAMERAS ENABLED" : "FORWARD CAMERA ENABLED");
		}
    }
}
