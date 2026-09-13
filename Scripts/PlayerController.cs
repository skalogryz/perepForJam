using Godot;

namespace PereSkyroom
{
	public class PlayerController : KinematicBody
	{
		[Signal]
		public delegate void Died();

		public const uint WorldCollisionLayer = 1u << 0;
		public const uint PlayerTriggerLayer = 1u << 1;
		public const uint SceneCameraCullMask = uint.MaxValue;

        private const float WalkSpeed = 7.0f;
        private const float FlySpeed = 10.0f;
        private const float JumpSpeed = 8.2f;
        private const float Gravity = 22.0f;
		private const float MouseSensitivity = 0.10f;
		private const float InteractionDistance = 100.0f;
		public const float MaximumHealth = 120.0f;
		private const float MaximumTemperature = 100.0f;
		private const float BoostHeatDurationSeconds = 7.0f;
		private const float NormalCoolingDurationSeconds = 4.0f;
		private const float OverheatDamage = 40.0f;
		private const int MaximumFullHealthRestoresPerGame = 2;
		private const ulong KissHealingCooldownMilliseconds = 2UL * 60UL * 1000UL;

		public float SpeedBoostMovementMultiplier = 3.5f;
		public float SpeedBoostJumpMultiplier = 1.0f;

		private Spatial _head;
		private Camera _camera;
		private WeaponHud _weaponHud;
		private BlueNoiseDither _ditherPostProcess;
		private PanoramicFovMode _panoramicFovMode;
		private CanvasLayer _hud;
        private Label _modeLabel;
        private Label _scoreLabel;
		private Label _ditherLabel;
		private Label _sideCameraLabel;
		private Label _messageLabel;
		private Label _fpsLabel;
		private PlayerStatsHud _playerStatsHud;
        private Vector3 _velocity = Vector3.Zero;
        private float _pitch;
		private bool _flightMode;
		private bool _speedBoostEnabled;
		private bool _ditherEnabled;
		private bool _ditherPaletteInverted;
		private int _score;
		private bool _fpsVisible;
		private float _fpsRefreshTimer;
		private PlatformProps _hookPlatform;
		private bool _weaponVisible = true;
		private float _health = MaximumHealth;
		private float _temperature;
		private bool _isDead;
		private int _fullHealthRestoresRemaining = MaximumFullHealthRestoresPerGame;
		private bool _kissHealingCooldownActive;
		private ulong _lastKissHealingMilliseconds;

		public bool WeaponVisible { get { return _weaponVisible; } }
		public bool SpeedBoostEnabled { get { return _speedBoostEnabled; } }
		public float Health { get { return _health; } }
		public float Temperature { get { return _temperature; } }
		public bool IsDead { get { return _isDead; } }
		public float PlanarMovementSpeed { get { return new Vector2(_velocity.x, _velocity.z).Length(); } }
		public float NormalWalkSpeed { get { return WalkSpeed; } }
		public bool HudLabelsVisible { get { return _hud != null && _hud.Visible; } }
		public bool FpsVisible { get { return _fpsVisible; } }
		public bool IsOnHook
		{
			get { return _hookPlatform != null && IsInstanceValid(_hookPlatform); }
		}
		public Vector3 HeadGlobalPosition
		{
			get
			{
				return _head != null && IsInstanceValid(_head)
					? _head.GlobalTransform.origin
					: GlobalTransform.origin + Vector3.Up * 1.55f;
			}
		}

        public override void _Ready()
        {
            BuildBody();
            BindHud();
			GlobalSettings.SetBoostMode(_speedBoostEnabled);
            Input.MouseMode = Input.MouseModeEnum.Captured;
            UpdateHud();
			UpdatePlayerStatsHud();
        }

		public override void _UnhandledInput(InputEvent inputEvent)
		{
			if (_isDead)
				return;

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
				bool isEnter = key.Scancode == (uint)KeyList.Enter
					|| key.Scancode == (uint)KeyList.KpEnter;
				if (key.Alt && isEnter)
				{
					OS.WindowFullscreen = !OS.WindowFullscreen;
					GetTree().SetInputAsHandled();
				}
				else if (key.Scancode == (uint)KeyList.Escape)
				{
					Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
						? Input.MouseModeEnum.Visible
						: Input.MouseModeEnum.Captured;
				}
				else if (key.Scancode == (uint)KeyList.F3)
					SetFpsVisible(!_fpsVisible);
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
			if (_isDead)
			{
				_velocity = Vector3.Zero;
				return;
			}

#if !EXPORT_RELEASE
			if (Input.IsActionJustPressed("reset_level"))
			{
				Main main = GetParent() as Main;
				if (main != null)
					main.ResetLevel();
				else
					GetTree().ReloadCurrentScene();
				return;
			}
#endif

			if (Input.IsActionJustPressed("toggle_speed_boost"))
				SetSpeedBoostEnabled(!_speedBoostEnabled, true);

			UpdateTemperature(delta);

			if (Input.IsActionJustPressed("interact"))
			{
				GlobalSettings.ActivateTarget(GetLookedAtSpatial(), this);
			}

			if (Input.IsActionJustPressed("toggle_flight"))
            {
#if !EXPORT_RELEASE
				_flightMode = !_flightMode;
                _velocity = Vector3.Zero;
                UpdateHud();
                ShowMessage(_flightMode ? "FLIGHT ENABLED" : "FLIGHT DISABLED");
#endif
			}

#if !EXPORT_RELEASE
			if (Input.IsActionJustPressed("toggle_weapon_visibility"))
				SetWeaponVisible(!WeaponVisible, true);
#endif

#if !EXPORT_RELEASE
			if (Input.IsActionJustPressed("toggle_hud_labels"))
			{
				#if GODOT_EXPORT
				SetHudLabelsVisible(false);
				#else
				SetHudLabelsVisible(!HudLabelsVisible);
				#endif
			}
#endif

			if (IsOnHook)
			{
				ProcessHookMovement(delta);
				if (Input.IsActionJustPressed("shoot") && Input.MouseMode == Input.MouseModeEnum.Captured)
					Shoot();
				return;
			}
			if (_hookPlatform != null)
				_hookPlatform = null;

            Vector2 input = new Vector2(
                Input.GetActionStrength("move_right") - Input.GetActionStrength("move_left"),
                Input.GetActionStrength("move_backward") - Input.GetActionStrength("move_forward"));
            if (input.Length() > 1.0f)
                input = input.Normalized();

            Vector3 wishDirection = (GlobalTransform.basis.x * input.x + GlobalTransform.basis.z * input.y).Normalized();
			float speedMultiplier = _speedBoostEnabled ? SpeedBoostMovementMultiplier : 1.0f;
			float speed = (_flightMode ? FlySpeed : WalkSpeed) * speedMultiplier;
            _velocity.x = wishDirection.x * speed;
            _velocity.z = wishDirection.z * speed;

#if !EXPORT_RELEASE
			if (_flightMode)
            {
                float vertical = Input.GetActionStrength("jump_or_up") - Input.GetActionStrength("fly_down");
				_velocity.y = vertical * FlySpeed * speedMultiplier;
                _velocity = MoveAndSlide(_velocity, Vector3.Up, false, 4, Mathf.Deg2Rad(55.0f));
            }
            else
			{
				if (IsOnFloor() && Input.IsActionJustPressed("jump_or_up"))
				{
					float jumpMultiplier = _speedBoostEnabled ? SpeedBoostJumpMultiplier : 1.0f;
					_velocity.y = JumpSpeed * jumpMultiplier;
				}
                else
                    _velocity.y -= Gravity * delta;
                _velocity = MoveAndSlide(_velocity, Vector3.Up, true, 4, Mathf.Deg2Rad(55.0f));
            }
#endif

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
			CollisionLayer = WorldCollisionLayer | PlayerTriggerLayer;
			CollisionMask = WorldCollisionLayer;

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
		}

		public void SetWeaponHud(WeaponHud weaponHud)
		{
			_weaponHud = weaponHud;
			if (_weaponHud == null || !IsInstanceValid(_weaponHud))
				return;
			_weaponHud.SetPlayer(this);
			_weaponHud.SetWeaponVisible(_weaponVisible);
		}

		public void SetDitherPostProcess(BlueNoiseDither ditherPostProcess)
		{
			_ditherPostProcess = ditherPostProcess;
		}

		public void SetPanoramicFovMode(PanoramicFovMode panoramicFovMode)
		{
			_panoramicFovMode = panoramicFovMode;
		}

		public void SetWeaponVisible(bool visible, bool showMessage = false)
		{
			_weaponVisible = visible;
			if (_weaponHud != null && IsInstanceValid(_weaponHud))
				_weaponHud.SetWeaponVisible(visible);
			if (showMessage)
				ShowMessage(visible ? "WEAPON SHOWN" : "WEAPON HIDDEN");
		}

		public void SetHudLabelsVisible(bool visible)
		{
			_hud.Visible = visible;
		}

		public void SetFpsVisible(bool visible)
		{
			_fpsVisible = visible;
			_fpsLabel.Visible = visible;
			_fpsRefreshTimer = 0.0f;
		}

		public void PlaceAt(Vector3 position)
		{
			DetachFromHook(false);
			Translation = position;
			Rotation = Vector3.Zero;
			_pitch = 0.0f;
			if (_head != null)
				_head.Rotation = Vector3.Zero;
			_velocity = Vector3.Zero;
		}

		public void RestoreQuickSavePosition(Vector3 position)
		{
			if (_isDead)
				return;

			DetachFromHook(false);
			Transform transform = GlobalTransform;
			transform.origin = position;
			GlobalTransform = transform;
			_velocity = Vector3.Zero;
		}

		public bool TryAttachToHook(PlatformProps hook)
		{
			if (hook == null || !IsInstanceValid(hook) || !hook.IsHook || IsOnHook)
				return false;

			_hookPlatform = hook;
			_velocity = Vector3.Zero;
			hook.SetPlayerAttached(this, true);
			SnapHeadToHook();
			return true;
		}

		private void ProcessHookMovement(float delta)
		{
			if (Input.IsActionJustPressed("fly_down"))
			{
				DetachFromHook(true);
				return;
			}

			if (Input.IsActionJustPressed("jump_or_up"))
			{
				Vector3 jumpDirection = GetHookJumpDirection();
				float movementMultiplier = _speedBoostEnabled ? SpeedBoostMovementMultiplier : 1.0f;
				float jumpMultiplier = _speedBoostEnabled ? SpeedBoostJumpMultiplier : 1.0f;
				DetachFromHook(true);
				_velocity = jumpDirection * WalkSpeed * movementMultiplier;
				_velocity.y = JumpSpeed * jumpMultiplier;
				_velocity = MoveAndSlide(_velocity, Vector3.Up, true, 4, Mathf.Deg2Rad(55.0f));
				return;
			}

			Vector3 headOffset = HeadGlobalPosition - GlobalTransform.origin;
			Vector3 desiredOrigin = _hookPlatform.GlobalTransform.origin - headOffset;
			Vector3 displacement = desiredOrigin - GlobalTransform.origin;
			if (displacement.LengthSquared() > 0.000001f)
			{
				KinematicCollision collision = MoveAndCollide(displacement, true, true, true);
				if (collision != null && collision.Collider is StaticBody
					&& collision.Collider != _hookPlatform)
				{
					DetachFromHook(true);
					return;
				}
			}

			Transform transform = GlobalTransform;
			transform.origin = desiredOrigin;
			GlobalTransform = transform;
			_velocity = Vector3.Zero;
		}

		private Vector3 GetHookJumpDirection()
		{
			Vector2 input = new Vector2(
				Input.GetActionStrength("move_right") - Input.GetActionStrength("move_left"),
				Input.GetActionStrength("move_backward") - Input.GetActionStrength("move_forward"));
			if (input.Length() > 1.0f)
				input = input.Normalized();

			Vector3 direction = GlobalTransform.basis.x * input.x + GlobalTransform.basis.z * input.y;
			direction.y = 0.0f;
			if (direction.LengthSquared() <= 0.0001f)
			{
				direction = -GlobalTransform.basis.z;
				direction.y = 0.0f;
			}
			return direction.Normalized();
		}

		private void SnapHeadToHook()
		{
			if (!IsOnHook)
				return;
			Vector3 headOffset = HeadGlobalPosition - GlobalTransform.origin;
			Transform transform = GlobalTransform;
			transform.origin = _hookPlatform.GlobalTransform.origin - headOffset;
			GlobalTransform = transform;
		}

		private void DetachFromHook(bool ignoreUntilExit)
		{
			PlatformProps hook = _hookPlatform;
			_hookPlatform = null;
			_velocity = Vector3.Zero;
			if (hook == null || !IsInstanceValid(hook))
				return;

			hook.SetPlayerAttached(this, false);
			if (ignoreUntilExit)
				hook.IgnorePlayerUntilExit(this);
		}

		public void ShowSystemMessage(string text)
		{
			ShowMessage(text);
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

			_playerStatsHud = hudOwner.GetNode<PlayerStatsHud>("PlayerStatsHUD");
        }

		private void UpdatePlayerStatsHud()
		{
			_playerStatsHud.SetBoostEnabled(_speedBoostEnabled);
			_playerStatsHud.SetHealth(_health, MaximumHealth);
			_playerStatsHud.SetTemperature(_temperature);
		}

		private void SetSpeedBoostEnabled(bool enabled, bool showMessage)
		{
			if (_speedBoostEnabled == enabled)
				return;

			_speedBoostEnabled = enabled;
			if (_panoramicFovMode != null && IsInstanceValid(_panoramicFovMode))
				_panoramicFovMode.SetEnabled(_speedBoostEnabled, false);
			GlobalSettings.SetBoostMode(_speedBoostEnabled);
			UpdatePlayerStatsHud();
			if (showMessage)
				ShowMessage(_speedBoostEnabled ? "SPEED BOOST ENABLED" : "NORMAL SPEED");
		}

		private void UpdateTemperature(float delta)
		{
			if (_speedBoostEnabled)
			{
				_temperature += MaximumTemperature / BoostHeatDurationSeconds * delta;
				if (_temperature >= MaximumTemperature)
				{
					_temperature = MaximumTemperature;
					SetSpeedBoostEnabled(false, false);
					ApplyDamage(OverheatDamage);
					ShowMessage("BOOST OVERHEAT: -40 HEALTH");
				}
			}
			else
			{
				_temperature = Mathf.Max(
					0.0f,
					_temperature - MaximumTemperature / NormalCoolingDurationSeconds * delta);
			}

			UpdatePlayerStatsHud();
		}

		public void ApplyDamage(float damage)
		{
			if (damage <= 0.0f || _isDead)
				return;
			_health = Mathf.Max(0.0f, _health - damage);
			if (_ditherPostProcess != null && IsInstanceValid(_ditherPostProcess))
				_ditherPostProcess.PlayDamagePaletteFlash();
			UpdatePlayerStatsHud();

			if (_health > 0.0f)
				return;

			_isDead = true;
			_velocity = Vector3.Zero;
			DetachFromHook(false);
			SetSpeedBoostEnabled(false, false);
			EmitSignal(nameof(Died));
		}

		public void RestoreHealth(float amount)
		{
			if (amount <= 0.0f || _isDead)
				return;

			_health = Mathf.Min(MaximumHealth, _health + amount);
			UpdatePlayerStatsHud();
		}

		public bool TryRestoreHealthFromKiss(float amount)
		{
			if (amount <= 0.0f || _isDead || _health >= MaximumHealth)
				return false;

			ulong now = OS.GetTicksMsec();
			if (_kissHealingCooldownActive
				&& now - _lastKissHealingMilliseconds < KissHealingCooldownMilliseconds)
			{
				return false;
			}

			_health = Mathf.Min(MaximumHealth, _health + amount);
			_kissHealingCooldownActive = true;
			_lastKissHealingMilliseconds = now;
			UpdatePlayerStatsHud();
			return true;
		}

		public bool TryRestoreFullHealth()
		{
			if (_isDead || _health >= MaximumHealth || _fullHealthRestoresRemaining <= 0)
				return false;

			_fullHealthRestoresRemaining--;
			_health = MaximumHealth;
			UpdatePlayerStatsHud();
			return true;
		}

		public void ResetFullHealthRestoreLimit()
		{
			_fullHealthRestoresRemaining = MaximumFullHealthRestoresPerGame;
		}

		public void ResetKissHealingCooldown()
		{
			_kissHealingCooldownActive = false;
			_lastKissHealingMilliseconds = 0UL;
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

		private Spatial GetLookedAtSpatial()
		{
			Vector3 from = _camera.GlobalTransform.origin;
			Vector3 to = from + (-_camera.GlobalTransform.basis.z * InteractionDistance);
			var exclude = new Godot.Collections.Array { this };
			Godot.Collections.Dictionary hit = GetWorld().DirectSpaceState.IntersectRay(from, to, exclude);
			if (hit.Count == 0)
				return null;

			return hit["collider"] as Spatial;
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

		public void SetDitherMode(bool enabled, bool showMessage = true)
		{
			_ditherEnabled = enabled;
			UpdateDitherLabel();
			_ditherLabel.Modulate = enabled ? new Color(1.0f, 0.86f, 0.42f) : new Color(0.72f, 0.76f, 0.84f);
			if (showMessage)
				ShowMessage(enabled ? "2-COLOR DITHER ENABLED" : "2-COLOR DITHER DISABLED");
		}

		public void SetDitherPaletteInverted(bool inverted, bool showMessage = true)
		{
			_ditherPaletteInverted = inverted;
			UpdateDitherLabel();
			if (showMessage)
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

		public void ShowAccentWireframeMessage(bool enabled)
		{
			ShowMessage(enabled ? "ACCENT WIREFRAME ENABLED" : "ACCENT WIREFRAME DISABLED");
		}

		public void SetSideCameraMode(bool enabled, bool showMessage = true)
		{
			_sideCameraLabel.Text = enabled ? "CAMERAS: SIDE VIEWS" : "CAMERAS: FORWARD";
			_sideCameraLabel.Modulate = enabled ? new Color(0.48f, 0.9f, 1.0f) : new Color(0.72f, 0.76f, 0.84f);
			if (showMessage)
				ShowMessage(enabled ? "SIDE CAMERAS ENABLED" : "FORWARD CAMERA ENABLED");
		}
    }
}
