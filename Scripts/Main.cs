using Godot;
using System.Collections.Generic;

namespace PereSkyroom
{
	public class Main : Spatial
	{
		[Export] public Color DitherDarkColor = new Color(0.035f, 0.045f, 0.075f, 1.0f);
		[Export] public Color DitherLightColor = new Color(0.95f, 0.82f, 0.36f, 1.0f);
		[Export] public float DitherGreenAccentThreshold = 0.5f;
		[Export] public bool DitherEnabledByDefault = false;
		[Export] public bool DitherPaletteInvertedByDefault = false;
		[Export] public bool SideCamerasEnabledByDefault = false;
		[Export] public bool PanoramicFovEnabledByDefault = false;
		[Export] public bool WeaponVisibleByDefault = true;
		[Export] public bool HudLabelsVisibleByDefault = true;
		[Export] public bool FpsVisibleByDefault = false;
		[Export] public float SpeedBoostMovementMultiplier = 3.5f;
		[Export] public float SpeedBoostJumpMultiplier = 1.0f;
		// Change this value in code to configure both side-camera yaw offsets.
		public float SideCameraAngleDegrees = 90.0f;
		public bool SmoothSideCameraTransitionEnabled = true;
		public float SideCameraTransitionDurationSeconds = 0.5f;
		// Godot perspective cameras cannot exceed 179 degrees, so values above that
		// are rendered by the three-camera panoramic compositor.
		public float PanoramicFovDegrees = 270.0f;
		public float PanoramicFovTransitionDurationSeconds = 0.5f;
		public float AccentWireframeWidthPixels = 3.0f;
		[Export] public Color AccentWireframeColor = new Color(0.0f, 1.0f, 0.0f, 1.0f);
		[Export] public bool AccentWireframeEnabledByDefault = false;
		[Export] public bool AccentObjectDitherEnabledByDefault = false;

		private Texture _ditherNoiseTexture;
		[Export]
		public Texture DitherNoiseTexture
		{
			get { return _ditherNoiseTexture; }
			set
			{
				_ditherNoiseTexture = value;
				if (_dither != null && IsInstanceValid(_dither))
					_dither.NoiseTexture = value;
			}
		}

		private readonly Color _floorColor = new Color(0.16f, 0.19f, 0.26f);
		private readonly Color _wallColor = new Color(0.10f, 0.13f, 0.20f);
		private readonly Color _platformColor = new Color(0.18f, 0.55f, 0.72f);
		private static DisplayModeState _pendingDisplayModeState;
		private static bool _verifyDisplayModesAfterReset;

		private PlayerController _player;
		private SideCameraMode _sideCameraMode;
		private PanoramicFovMode _panoramicFovMode;
		private BlueNoiseDither _dither;
		private AccentObjectDitherMode _accentDither;
		private AccentWireframeOverlay _wireframe;

		public override void _Ready()
		{
			DisplayModeState initialDisplayModes = TakeInitialDisplayModeState();
			BuildEnvironment();
			BuildRoom();
			BuildPlatforms();
			List<ShootTarget> targets = BuildTargets();
			_player = BuildPlayer();
			_sideCameraMode = BuildSideCameraMode(_player);
			_panoramicFovMode = BuildPanoramicFovMode(_player);
			_dither = BuildDitherPostProcess(_player);
			_accentDither = BuildAccentObjectDither(
				targets, _player, _sideCameraMode, _panoramicFovMode, _dither);
			_wireframe = BuildAccentWireframe(
				targets, _player, _sideCameraMode, _panoramicFovMode, _dither, _accentDither);
			ApplyDisplayModeState(initialDisplayModes);

			foreach (string argument in OS.GetCmdlineArgs())
			{
				if (argument == "--smoke-test" || argument == "--no-window")
				{
					if (_verifyDisplayModesAfterReset)
					{
						VerifyDisplayModesAfterReset();
						return;
					}
					_sideCameraMode.SetEnabled(true);
					_panoramicFovMode.SetEnabled(true);
					_dither.SetEnabled(true);
					RunSmokeTest(_wireframe, _accentDither, _dither);
					break;
				}
			}
		}

		public void ResetLevel()
		{
			_pendingDisplayModeState = CaptureDisplayModeState();
			GetTree().ReloadCurrentScene();
		}

		private DisplayModeState TakeInitialDisplayModeState()
		{
			if (_pendingDisplayModeState != null)
			{
				DisplayModeState state = _pendingDisplayModeState;
				_pendingDisplayModeState = null;
				return state;
			}

			return new DisplayModeState
			{
				DitherEnabled = DitherEnabledByDefault,
				DitherPaletteInverted = DitherPaletteInvertedByDefault,
				SideCamerasEnabled = SideCamerasEnabledByDefault,
				PanoramicFovEnabled = PanoramicFovEnabledByDefault,
				WeaponVisible = WeaponVisibleByDefault,
				HudLabelsVisible = HudLabelsVisibleByDefault,
				FpsVisible = FpsVisibleByDefault,
				AccentWireframeEnabled = AccentWireframeEnabledByDefault,
				AccentObjectDitherEnabled = AccentObjectDitherEnabledByDefault
			};
		}

		private DisplayModeState CaptureDisplayModeState()
		{
			return new DisplayModeState
			{
				DitherEnabled = _dither.Enabled,
				DitherPaletteInverted = _dither.PaletteInverted,
				SideCamerasEnabled = _sideCameraMode.IsEnabled(),
				PanoramicFovEnabled = _panoramicFovMode.IsEnabled(),
				WeaponVisible = _player.WeaponVisible,
				HudLabelsVisible = _player.HudLabelsVisible,
				FpsVisible = _player.FpsVisible,
				AccentWireframeEnabled = _wireframe.Enabled,
				AccentObjectDitherEnabled = _accentDither.Enabled
			};
		}

		private void ApplyDisplayModeState(DisplayModeState state)
		{
			_player.SetWeaponVisible(state.WeaponVisible);
			_player.SetHudLabelsVisible(state.HudLabelsVisible);
			_player.SetFpsVisible(state.FpsVisible);
			_dither.SetPaletteInverted(state.DitherPaletteInverted, false);
			_dither.SetEnabled(state.DitherEnabled, false);
			_sideCameraMode.SetEnabled(state.SideCamerasEnabled, false);
			_panoramicFovMode.SetEnabled(state.PanoramicFovEnabled, false);
			_wireframe.SetEnabled(state.AccentWireframeEnabled, false);
			_accentDither.SetEnabled(state.AccentObjectDitherEnabled, false);
		}

		private sealed class DisplayModeState
		{
			public bool DitherEnabled;
			public bool DitherPaletteInverted;
			public bool SideCamerasEnabled;
			public bool PanoramicFovEnabled;
			public bool WeaponVisible;
			public bool HudLabelsVisible;
			public bool FpsVisible;
			public bool AccentWireframeEnabled;
			public bool AccentObjectDitherEnabled;
		}

		private void BuildEnvironment()
		{
			var worldEnvironment = new WorldEnvironment { Name = "SkyboxEnvironment" };
			var environment = new Environment
			{
				BackgroundMode = Environment.BGMode.Sky,
				AmbientLightColor = new Color(0.46f, 0.55f, 0.75f),
				AmbientLightEnergy = 0.65f,
				TonemapMode = Environment.ToneMapper.Filmic,
				TonemapExposure = 1.15f,
				SsaoEnabled = true,
				GlowEnabled = true
			};
			var proceduralSky = new ProceduralSky
			{
				SkyTopColor = new Color(0.015f, 0.035f, 0.12f),
				SkyHorizonColor = new Color(0.42f, 0.22f, 0.48f),
				GroundBottomColor = new Color(0.02f, 0.025f, 0.06f),
				GroundHorizonColor = new Color(0.22f, 0.13f, 0.25f),
				SunLatitude = 28.0f,
				SunLongitude = 145.0f,
				SunEnergy = 3.5f,
				TextureSize = ProceduralSky.TextureSizeEnum.Size1024
			};
			environment.BackgroundSky = proceduralSky;
			environment.BackgroundEnergy = 0.8f;
			worldEnvironment.Environment = environment;
			AddChild(worldEnvironment);

			var sun = new DirectionalLight
			{
				Name = "Sun",
				LightColor = new Color(0.88f, 0.91f, 1.0f),
				LightEnergy = 1.1f,
				ShadowEnabled = true,
				RotationDegrees = new Vector3(-52.0f, -28.0f, 0.0f)
			};
			AddChild(sun);
		}

		private void BuildRoom()
		{
			AddBox("Floor", new Vector3(0, -0.5f, 0), new Vector3(24, 1, 24), _floorColor);
			AddBox("NorthWall", new Vector3(0, 4, -12), new Vector3(24, 9, 0.5f), _wallColor);
			AddBox("WestWall", new Vector3(-12, 4, 0), new Vector3(0.5f, 9, 24), _wallColor);
			AddBox("EastWall", new Vector3(12, 4, 0), new Vector3(0.5f, 9, 24), _wallColor);
			AddBox("SouthWallLow", new Vector3(0, 1, 12), new Vector3(24, 2, 0.5f), _wallColor);
		}

		private void BuildPlatforms()
		{
			AddBox("Platform01", new Vector3(-5.5f, 1.0f, 2.5f), new Vector3(4.5f, 0.5f, 4.0f), _platformColor);
			AddBox("Platform02", new Vector3(0.0f, 2.2f, -1.5f), new Vector3(4.0f, 0.5f, 4.0f), _platformColor);
			AddBox("Platform03", new Vector3(5.4f, 3.4f, -5.0f), new Vector3(4.5f, 0.5f, 4.0f), _platformColor);
			AddBox("Platform04", new Vector3(-3.5f, 4.6f, -8.2f), new Vector3(5.0f, 0.5f, 3.2f), _platformColor);
			AddBox("FloatingStep", new Vector3(6.5f, 1.2f, 5.2f), new Vector3(2.4f, 0.45f, 2.4f), new Color(0.55f, 0.3f, 0.72f));
		}

		private List<ShootTarget> BuildTargets()
		{
			var targets = new List<ShootTarget>();
			targets.Add(AddTarget(new Vector3(-5.5f, 2.2f, 1.2f)));
			targets.Add(AddTarget(new Vector3(0.0f, 3.4f, -2.3f)));
			targets.Add(AddTarget(new Vector3(5.4f, 4.6f, -6.0f)));
			targets.Add(AddTarget(new Vector3(-3.5f, 5.8f, -8.8f)));
			targets.Add(AddTarget(new Vector3(8.8f, 1.3f, -9.5f)));
			return targets;
		}

		private PlayerController BuildPlayer()
		{
			var player = new PlayerController
			{
				Name = "Player",
				SpeedBoostMovementMultiplier = SpeedBoostMovementMultiplier,
				SpeedBoostJumpMultiplier = SpeedBoostJumpMultiplier
			};
			AddChild(player);
			player.Translation = new Vector3(0, 1.2f, 8.0f);
			return player;
		}

		private BlueNoiseDither BuildDitherPostProcess(PlayerController player)
		{
			var dither = new BlueNoiseDither
			{
				Name = "BlueNoiseDither",
				DarkColor = DitherDarkColor,
				LightColor = DitherLightColor,
				AccentColor = AccentWireframeColor,
				GreenAccentThreshold = DitherGreenAccentThreshold,
				InvertPaletteByDefault = DitherPaletteInvertedByDefault,
				NoiseTexture = DitherNoiseTexture,
				Player = player
			};
			AddChild(dither);
			return dither;
		}

		private SideCameraMode BuildSideCameraMode(PlayerController player)
		{
			var mode = new SideCameraMode
			{
				Name = "SideCameraMode",
				Player = player,
				CameraAngleDegrees = SideCameraAngleDegrees,
				SmoothTransitionEnabled = SmoothSideCameraTransitionEnabled,
				TransitionDurationSeconds = SideCameraTransitionDurationSeconds
			};
			AddChild(mode);
			return mode;
		}

		private PanoramicFovMode BuildPanoramicFovMode(PlayerController player)
		{
			var mode = new PanoramicFovMode
			{
				Name = "PanoramicFovMode",
				Player = player,
				TargetFovDegrees = PanoramicFovDegrees,
				TransitionDurationSeconds = PanoramicFovTransitionDurationSeconds
			};
			AddChild(mode);
			return mode;
		}

		private AccentWireframeOverlay BuildAccentWireframe(
			List<ShootTarget> targets,
			PlayerController player,
			SideCameraMode sideCameraMode,
			PanoramicFovMode panoramicFovMode,
			BlueNoiseDither dither,
			AccentObjectDitherMode accentDither)
		{
			var overlay = new AccentWireframeOverlay
			{
				Name = "AccentWireframeOverlay",
				Targets = targets,
				Player = player,
				SideCameraMode = sideCameraMode,
				PanoramicFovMode = panoramicFovMode,
				Dither = dither,
				AccentDitherMode = accentDither,
				EnabledByDefault = AccentWireframeEnabledByDefault,
				LineWidthPixels = AccentWireframeWidthPixels,
				AccentColor = AccentWireframeColor
			};
			AddChild(overlay);
			return overlay;
		}

		private AccentObjectDitherMode BuildAccentObjectDither(
			List<ShootTarget> targets,
			PlayerController player,
			SideCameraMode sideCameraMode,
			PanoramicFovMode panoramicFovMode,
			BlueNoiseDither dither)
		{
			var mode = new AccentObjectDitherMode
			{
				Name = "AccentObjectDitherMode",
				Targets = targets,
				Player = player,
				SideCameraMode = sideCameraMode,
				PanoramicFovMode = panoramicFovMode,
				Dither = dither,
				AccentColor = AccentWireframeColor,
				EnabledByDefault = AccentObjectDitherEnabledByDefault
			};
			AddChild(mode);
			return mode;
		}

		private async void RunSmokeTest(
			AccentWireframeOverlay wireframe,
			AccentObjectDitherMode accentDither,
			BlueNoiseDither dither)
		{
			await ToSignal(GetTree().CreateTimer(0.35f), "timeout");
			if (wireframe.Enabled)
			{
				GD.PushError("Accent wireframe was enabled by default.");
				GetTree().Quit(1);
				return;
			}
			wireframe.SetEnabled(true);
			await ToSignal(GetTree().CreateTimer(0.35f), "timeout");
			if (wireframe.DrawPassCount <= 0)
			{
				GD.PushError("Accent wireframe did not complete a draw pass.");
				GetTree().Quit(1);
				return;
			}
			accentDither.SetEnabled(true);
			await ToSignal(GetTree().CreateTimer(0.35f), "timeout");
			if (accentDither.DrawPassCount <= 0)
			{
				GD.PushError("Accent object dithering did not complete a mask draw pass.");
				GetTree().Quit(1);
				return;
			}
			dither.SetEnabled(false);
			await ToSignal(GetTree(), "idle_frame");
			if (accentDither.Enabled)
			{
				GD.PushError("Accent object dithering stayed enabled after dithering was disabled.");
				GetTree().Quit(1);
				return;
			}
			accentDither.SetEnabled(true);
			if (accentDither.Enabled)
			{
				GD.PushError("Accent object dithering enabled while dithering was disabled.");
				GetTree().Quit(1);
				return;
			}

			_dither.SetEnabled(true, false);
			_dither.SetPaletteInverted(true, false);
			_sideCameraMode.SetEnabled(true, false);
			_panoramicFovMode.SetEnabled(true, false);
			_player.SetWeaponVisible(false);
			_player.SetHudLabelsVisible(false);
			_player.SetFpsVisible(true);
			_wireframe.SetEnabled(true, false);
			_accentDither.SetEnabled(true, false);
			_verifyDisplayModesAfterReset = true;
			ResetLevel();
		}

		private void VerifyDisplayModesAfterReset()
		{
			_verifyDisplayModesAfterReset = false;
			bool restored = _dither.Enabled
				&& _dither.PaletteInverted
				&& _sideCameraMode.IsEnabled()
				&& _panoramicFovMode.IsEnabled()
				&& !_player.WeaponVisible
				&& !_player.HudLabelsVisible
				&& _player.FpsVisible
				&& _wireframe.Enabled
				&& _accentDither.Enabled;
			if (!restored)
			{
				GD.PushError("Display modes were not preserved after resetting the level.");
				GetTree().Quit(1);
				return;
			}

			GD.Print("PERE_DITHER_SMOKE_TEST_OK");
			GetTree().Quit();
		}

		private ShootTarget AddTarget(Vector3 position)
		{
			var target = new ShootTarget();
			AddChild(target);
			target.Translation = position;
			return target;
		}

		private void AddBox(string name, Vector3 position, Vector3 size, Color color)
		{
			var body = new StaticBody { Name = name, Translation = position };
			var shape = new CollisionShape { Shape = new BoxShape { Extents = size * 0.5f } };
			var mesh = new MeshInstance
			{
				Mesh = new CubeMesh { Size = size },
				MaterialOverride = MakeMaterial(color, 0.72f)
			};
			body.AddChild(shape);
			body.AddChild(mesh);
			AddChild(body);
		}

		private SpatialMaterial MakeMaterial(Color color, float roughness)
		{
			return new SpatialMaterial
			{
				AlbedoColor = color,
				Roughness = roughness,
				Metallic = 0.12f
			};
		}
	}
}
