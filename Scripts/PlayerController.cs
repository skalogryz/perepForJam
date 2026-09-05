using Godot;

namespace PereSkyroom
{
    public class PlayerController : KinematicBody
    {
        private const float WalkSpeed = 7.0f;
        private const float FlySpeed = 10.0f;
        private const float JumpSpeed = 8.2f;
        private const float Gravity = 22.0f;
        private const float MouseSensitivity = 0.10f;

        private Spatial _head;
        private Camera _camera;
        private Label _modeLabel;
        private Label _scoreLabel;
		private Label _ditherLabel;
        private Label _messageLabel;
		private Label _fpsLabel;
        private Vector3 _velocity = Vector3.Zero;
        private float _pitch;
        private bool _flightMode;
        private int _score;
		private bool _fpsVisible;
		private float _fpsRefreshTimer;

        public override void _Ready()
        {
            BuildBody();
            BuildHud();
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
            _camera = new Camera { Name = "Camera", Current = true, Fov = 78.0f, Far = 200.0f };
            _head.AddChild(_camera);

            var gun = new MeshInstance
            {
                Name = "Blaster",
                Mesh = new CubeMesh { Size = new Vector3(0.22f, 0.18f, 0.7f) },
                Translation = new Vector3(0.42f, -0.32f, -0.75f),
                MaterialOverride = new SpatialMaterial
                {
                    AlbedoColor = new Color(0.08f, 0.1f, 0.14f),
                    Metallic = 0.8f,
                    Roughness = 0.25f
                }
            };
            _camera.AddChild(gun);
        }

        private void BuildHud()
        {
            var hud = new CanvasLayer { Name = "HUD" };
            AddChild(hud);

            var help = NewLabel("WASD — move   SPACE — jump/up   CTRL — down\nF — flight   B — 2-color blue-noise   F3 — FPS   LMB — shoot   ESC — cursor", 18);
            help.RectPosition = new Vector2(24, 20);
            hud.AddChild(help);

            _modeLabel = NewLabel(string.Empty, 20);
            _modeLabel.RectPosition = new Vector2(24, 88);
            hud.AddChild(_modeLabel);

            _scoreLabel = NewLabel(string.Empty, 20);
            _scoreLabel.RectPosition = new Vector2(24, 116);
            hud.AddChild(_scoreLabel);

			_ditherLabel = NewLabel("DITHER: OFF", 20);
			_ditherLabel.RectPosition = new Vector2(24, 144);
			hud.AddChild(_ditherLabel);

            _messageLabel = NewLabel(string.Empty, 25);
            _messageLabel.AnchorLeft = 0.5f;
            _messageLabel.AnchorRight = 0.5f;
            _messageLabel.RectPosition = new Vector2(-125, 54);
            _messageLabel.RectSize = new Vector2(250, 36);
            _messageLabel.Align = Label.AlignEnum.Center;
            hud.AddChild(_messageLabel);

			_fpsLabel = NewLabel("FPS: --", 20);
			_fpsLabel.AnchorLeft = 1.0f;
			_fpsLabel.AnchorRight = 1.0f;
			_fpsLabel.RectPosition = new Vector2(-190, 20);
			_fpsLabel.RectSize = new Vector2(160, 32);
			_fpsLabel.Align = Label.AlignEnum.Right;
			_fpsLabel.Visible = false;
			hud.AddChild(_fpsLabel);

            var crosshair = NewLabel("+", 30);
            crosshair.AnchorLeft = 0.5f;
            crosshair.AnchorTop = 0.5f;
            crosshair.AnchorRight = 0.5f;
            crosshair.AnchorBottom = 0.5f;
            crosshair.RectPosition = new Vector2(-10, -20);
            hud.AddChild(crosshair);
        }

        private Label NewLabel(string text, int size)
        {
            var label = new Label { Text = text };
            label.RectScale = new Vector2(size / 14.0f, size / 14.0f);
            label.AddColorOverride("font_color", new Color(0.9f, 0.96f, 1.0f));
            label.AddColorOverride("font_color_shadow", new Color(0, 0, 0, 0.85f));
            label.AddConstantOverride("shadow_offset_x", 2);
            label.AddConstantOverride("shadow_offset_y", 2);
            return label;
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
			_ditherLabel.Text = enabled ? "DITHER: BLUE-NOISE / 2 COLORS" : "DITHER: OFF";
			_ditherLabel.Modulate = enabled ? new Color(1.0f, 0.86f, 0.42f) : new Color(0.72f, 0.76f, 0.84f);
			ShowMessage(enabled ? "2-COLOR DITHER ENABLED" : "2-COLOR DITHER DISABLED");
		}
    }
}
