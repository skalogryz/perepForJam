using Godot;
using System.Collections.Generic;
using IOFile = System.IO.File;
using IOPath = System.IO.Path;

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
		[Export] public float StoneSideLength = 0.25f;
		[Export] public float StoneSpawnIntervalSeconds = 3.0f;
		[Export] public float StoneEvaluationDelaySeconds = 3.0f;
		[Export] public int MaximumStoneCount = 20;
		[Export] public PackedScene PetScene;
		[Export] public PackedScene MovingPetScene;
		[Export] public PackedScene GameOverScene;
		[Export] public PackedScene GameCompleteScene;
		[Export] public float PetPlayerReachDistance = 2.0f;
		[Export] public float PetStoneSearchDistance = 3.0f;
		[Export] public float PetSideLength = 0.5f;
		[Export] public float PetScale = 1.0f;
		[Export] public float PetMoveSpeed = 3.5f;
		[Export] public float PetJumpSpeed = 7.0f;
		[Export] public float PetFallY = -15.0f;
		[Export] public string BlendLevelPathOnStartup = string.Empty;
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
		[Export] public bool PlatformOutlinesEnabledByDefault = true;

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
		private static string _pendingBlendLevelPath;

		private Spatial _levelRoot;
		private Environment _mainSceneEnvironment;
		private float _mainSceneAmbientLightEnergy;
		private DirectionalLight _mainSceneSun;
		private PlayerController _player;
		private WeaponHud _weaponHud;
		private StoneDropManager _stoneDropManager;
		private Pet _pet;
		private SideCameraMode _sideCameraMode;
		private PanoramicFovMode _panoramicFovMode;
		private BlueNoiseDither _dither;
		private AccentObjectDitherMode _accentDither;
		private AccentWireframeOverlay _wireframe;
		private FileDialog _blendFileDialog;
		private Input.MouseModeEnum _mouseModeBeforeFileDialog;
		private string _loadedBlendLevelPath;
		private CanvasLayer _dialogueHud;
		private Node _activeDialogUi;
		private bool _treeWasPausedBeforeDialog;
		private Input.MouseModeEnum _mouseModeBeforeDialog;
		private PauseModeEnum _mainPauseModeBeforeDialog;
		private PauseModeEnum _dialogueHudPauseModeBeforeDialog;
		private readonly Dictionary<Node, PauseModeEnum> _childPauseModesBeforeDialog
			= new Dictionary<Node, PauseModeEnum>();
		private bool _gameOverStarted;
		private bool _gameCompleted;
		private bool _platformOutlinesEnabled;
		private bool _hasQuickSave;
		private Vector3 _quickSavedPlayerPosition;
		private Vector3 _quickSavedPetPosition;

		public override void _Ready()
		{
			DisplayModeState initialDisplayModes = TakeInitialDisplayModeState();
			_levelRoot = new Spatial { Name = "RuntimeLevel" };
			AddChild(_levelRoot);
			MoveSceneLevelNodesToRuntimeRoot();
			BuildEnvironment();
			BuildRoom();
			BuildPlatforms();
			List<ShootTarget> targets = BuildTargets();
			_player = BuildPlayer();
			_player.ResetFullHealthRestoreLimit();
			_player.ResetKissHealingCooldown();
			_weaponHud = GetNode<WeaponHud>("WeaponHUD");
			_weaponHud.ResetDeathPresentation();
			_player.SetWeaponHud(_weaponHud);
			_player.Connect(nameof(PlayerController.Died), this, nameof(OnPlayerDied));
			_weaponHud.Connect(
				nameof(WeaponHud.DeathFadeCompleted), this, nameof(OnDeathFadeCompleted));
			RegisterPlayerWithPlatforms(_levelRoot, _player);
			_stoneDropManager = BuildStoneDropManager(_player);
			_pet = BuildPet(_player, _stoneDropManager);
			_sideCameraMode = BuildSideCameraMode(_player);
			_panoramicFovMode = BuildPanoramicFovMode(_player);
			_player.SetPanoramicFovMode(_panoramicFovMode);
			_dither = BuildDitherPostProcess(_player);
			_accentDither = BuildAccentObjectDither(
				targets, _player, _sideCameraMode, _panoramicFovMode, _dither);
			_wireframe = BuildAccentWireframe(
				targets, _player, _sideCameraMode, _panoramicFovMode, _dither, _accentDither);
			ApplyDisplayModeState(initialDisplayModes);
			SetupBlendFileDialog();
			SetupDialogueHud();

			if (!string.IsNullOrEmpty(_pendingBlendLevelPath))
			{
				string path = _pendingBlendLevelPath;
				_pendingBlendLevelPath = null;
				LoadBlendLevel(path, false);
			}
			else
			{
				string adjacentLevelPath = FindAdjacentExecutableLevel();
				if (!string.IsNullOrEmpty(adjacentLevelPath))
					LoadBlendLevel(adjacentLevelPath, false);
				else if (!string.IsNullOrEmpty(BlendLevelPathOnStartup))
					LoadBlendLevel(BlendLevelPathOnStartup, false);
			}

			foreach (string argument in OS.GetCmdlineArgs())
			{
				if (argument.StartsWith("--blend-level="))
				{
					LoadBlendLevel(argument.Substring("--blend-level=".Length), false);
					continue;
				}
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

		private static string FindAdjacentExecutableLevel()
		{
			// Browser exports do not have a native executable directory and must not
			// probe the host file system for an external Blender file.
			if (OS.HasFeature("HTML5") || OS.HasFeature("web"))
				return null;

			string executablePath = OS.GetExecutablePath();
			if (string.IsNullOrWhiteSpace(executablePath))
				return null;
			string executableDirectory = IOPath.GetDirectoryName(executablePath);
			if (string.IsNullOrWhiteSpace(executableDirectory))
				return null;

			string levelPath = IOPath.Combine(executableDirectory, "level.blend");
			return IOFile.Exists(levelPath) ? levelPath : null;
		}

		private void MoveSceneLevelNodesToRuntimeRoot()
		{
			Node roomCenterTrigger = GetNodeOrNull("RoomCenterTrigger");
			if (roomCenterTrigger == null)
				return;

			RemoveChild(roomCenterTrigger);
			_levelRoot.AddChild(roomCenterTrigger);
		}

		public override void _UnhandledInput(InputEvent inputEvent)
		{
			var key = inputEvent as InputEventKey;
			if (key == null || !key.Pressed || key.Echo)
				return;
			if (_gameOverStarted || _gameCompleted)
				return;
			if (Input.IsActionJustPressed("quick_save"))
			{
				if (!IsDialogActive())
					QuickSavePositions();
				GetTree().SetInputAsHandled();
				return;
			}
			if (Input.IsActionJustPressed("quick_load"))
			{
				if (!IsDialogActive())
					QuickLoadPositions();
				GetTree().SetInputAsHandled();
				return;
			}
			if (Input.IsActionJustPressed("toggle_platform_outlines"))
			{
				SetPlatformOutlinesEnabled(!_platformOutlinesEnabled, true);
				GetTree().SetInputAsHandled();
				return;
			}
			if (key.Scancode == (uint)KeyList.Escape && IsDialogActive())
			{
				CloseDialog();
				GetTree().SetInputAsHandled();
				return;
			}
			if (key.Scancode != (uint)KeyList.F1)
				return;

			ShowBlendFileDialog();
			GetTree().SetInputAsHandled();
		}

		public override void _ExitTree()
		{
			DisconnectDialogueSignals();
			if (IsDialogActive())
				CloseDialog();
		}

		private void SetupDialogueHud()
		{
			_dialogueHud = GetNode<CanvasLayer>("DialogueHUD");
			foreach (Node child in _dialogueHud.GetChildren())
			{
				_dialogueHud.RemoveChild(child);
				child.QueueFree();
			}
			_activeDialogUi = null;
			GlobalSettings settings = GlobalSettings.inst;
			if (settings == null || !IsInstanceValid(settings))
			{
				GD.PushError("GlobalSettings is unavailable; dialogue events cannot be connected.");
				return;
			}

			if (!settings.IsConnected(nameof(GlobalSettings.TriggerEvent), this, nameof(OnTriggerEvent)))
				settings.Connect(nameof(GlobalSettings.TriggerEvent), this, nameof(OnTriggerEvent));
			if (!settings.IsConnected(nameof(GlobalSettings.DialogCloseRequested), this, nameof(OnDialogCloseRequested)))
				settings.Connect(nameof(GlobalSettings.DialogCloseRequested), this, nameof(OnDialogCloseRequested));
			if (!settings.IsConnected(nameof(GlobalSettings.GameCompletionRequested), this, nameof(OnGameCompletionRequested)))
				settings.Connect(nameof(GlobalSettings.GameCompletionRequested), this, nameof(OnGameCompletionRequested));
		}

		private void DisconnectDialogueSignals()
		{
			GlobalSettings settings = GlobalSettings.inst;
			if (settings == null || !IsInstanceValid(settings))
				return;
			if (settings.IsConnected(nameof(GlobalSettings.TriggerEvent), this, nameof(OnTriggerEvent)))
				settings.Disconnect(nameof(GlobalSettings.TriggerEvent), this, nameof(OnTriggerEvent));
			if (settings.IsConnected(nameof(GlobalSettings.DialogCloseRequested), this, nameof(OnDialogCloseRequested)))
				settings.Disconnect(nameof(GlobalSettings.DialogCloseRequested), this, nameof(OnDialogCloseRequested));
			if (settings.IsConnected(nameof(GlobalSettings.GameCompletionRequested), this, nameof(OnGameCompletionRequested)))
				settings.Disconnect(nameof(GlobalSettings.GameCompletionRequested), this, nameof(OnGameCompletionRequested));
		}

		private void OnTriggerEvent(string eventName, Node player, Node triggerField)
		{
			if (IsDialogActive() || string.IsNullOrEmpty(eventName))
				return;

			GlobalSettings settings = GlobalSettings.inst;
			if (settings == null || !IsInstanceValid(settings))
				return;

			DialogDescr dialog = null;
			foreach (DialogDescr candidate in settings.Dialogs)
			{
				if (candidate != null && candidate.Name == eventName)
				{
					dialog = candidate;
					break;
				}
			}
			if (dialog == null || string.IsNullOrWhiteSpace(dialog.UIScene))
				return;

			PackedScene packedUi = ResourceLoader.Load<PackedScene>(dialog.UIScene);
			if (packedUi == null)
			{
				GD.PushError("Cannot load dialogue UI scene: " + dialog.UIScene);
				return;
			}

			Node dialogUi = packedUi.Instance();
			IPlayerDialog playerDialog = dialogUi as IPlayerDialog;
			if (playerDialog != null)
				playerDialog.SetPlayer(player as PlayerController);
			ShowDialog(dialogUi);
		}

		private void ShowDialog(Node dialogUi)
		{
			if (dialogUi == null || _dialogueHud == null || IsDialogActive())
				return;

			_treeWasPausedBeforeDialog = GetTree().Paused;
			_mouseModeBeforeDialog = Input.MouseMode;
			_mainPauseModeBeforeDialog = PauseMode;
			_dialogueHudPauseModeBeforeDialog = _dialogueHud.PauseMode;
			_childPauseModesBeforeDialog.Clear();
			foreach (Node child in GetChildren())
			{
				if (child == _dialogueHud)
					continue;
				_childPauseModesBeforeDialog[child] = child.PauseMode;
				child.PauseMode = PauseModeEnum.Stop;
			}

			PauseMode = PauseModeEnum.Process;
			_dialogueHud.PauseMode = PauseModeEnum.Process;
			dialogUi.PauseMode = PauseModeEnum.Process;
			MoveNestedCanvasLayersAboveDialogueBackground(dialogUi);
			_dialogueHud.AddChild(dialogUi);
			_activeDialogUi = dialogUi;
			Input.MouseMode = Input.MouseModeEnum.Visible;
			GetTree().Paused = true;
		}

		private void OnDialogCloseRequested()
		{
			CloseDialog();
		}

		private void OnGameCompletionRequested()
		{
			if (_gameOverStarted || _gameCompleted
				|| _player == null || !IsInstanceValid(_player) || _player.IsDead)
			{
				return;
			}
			if (GameCompleteScene == null)
			{
				GD.PushError("GameCompleteScene is not assigned in Main.tscn.");
				return;
			}

			_gameCompleted = true;
			if (IsDialogActive())
				CloseDialog();
			ShowDialog(GameCompleteScene.Instance());
		}

		private void OnPlayerDied()
		{
			if (_gameOverStarted || _gameCompleted)
				return;

			_gameOverStarted = true;
			if (IsDialogActive())
				CloseDialog();
			_weaponHud.BeginDeathFade();
		}

		private void OnDeathFadeCompleted()
		{
			if (!_gameOverStarted)
				return;
			if (GameOverScene == null)
			{
				GD.PushError("GameOverScene is not assigned in Main.tscn.");
				return;
			}

			ShowDialog(GameOverScene.Instance());
		}

		private void CloseDialog()
		{
			if (!IsDialogActive())
				return;

			foreach (Node child in _dialogueHud.GetChildren())
			{
				_dialogueHud.RemoveChild(child);
				child.QueueFree();
			}
			_activeDialogUi = null;

			foreach (KeyValuePair<Node, PauseModeEnum> entry in _childPauseModesBeforeDialog)
			{
				if (entry.Key != null && IsInstanceValid(entry.Key))
					entry.Key.PauseMode = entry.Value;
			}
			_childPauseModesBeforeDialog.Clear();
			_dialogueHud.PauseMode = _dialogueHudPauseModeBeforeDialog;
			PauseMode = _mainPauseModeBeforeDialog;
			Input.MouseMode = _mouseModeBeforeDialog;
			GetTree().Paused = _treeWasPausedBeforeDialog;
		}

		private bool IsDialogActive()
		{
			return _activeDialogUi != null && IsInstanceValid(_activeDialogUi);
		}

		private void MoveNestedCanvasLayersAboveDialogueBackground(Node parent)
		{
			foreach (Node child in parent.GetChildren())
			{
				CanvasLayer nestedLayer = child as CanvasLayer;
				if (nestedLayer != null)
					nestedLayer.Layer = _dialogueHud.Layer + Mathf.Max(nestedLayer.Layer, 0) + 1;
				MoveNestedCanvasLayersAboveDialogueBackground(child);
			}
		}

		public void ResetLevel()
		{
			_pendingDisplayModeState = CaptureDisplayModeState();
			_pendingBlendLevelPath = _loadedBlendLevelPath;
			if (_stoneDropManager != null && IsInstanceValid(_stoneDropManager))
				_stoneDropManager.ClearStones();
			GetTree().ReloadCurrentScene();
		}

		private void QuickSavePositions()
		{
			if (_player == null || !IsInstanceValid(_player)
				|| _pet == null || !IsInstanceValid(_pet) || _player.IsDead)
			{
				return;
			}

			_quickSavedPlayerPosition = _player.GlobalTransform.origin;
			_quickSavedPetPosition = _pet.GlobalTransform.origin;
			_hasQuickSave = true;
			_player.ShowSystemMessage("QUICK SAVE");
		}

		private void QuickLoadPositions()
		{
			if (_gameOverStarted || _player == null || !IsInstanceValid(_player)
				|| _pet == null || !IsInstanceValid(_pet) || _player.IsDead)
			{
				return;
			}

			if (!_hasQuickSave)
			{
				_player.ShowSystemMessage("NO QUICK SAVE");
				return;
			}

			_player.RestoreQuickSavePosition(_quickSavedPlayerPosition);
			_pet.RestoreQuickSavePosition(_quickSavedPetPosition);
			_player.ApplyDamage(1.0f);
			if (!_player.IsDead)
				_player.ShowSystemMessage("QUICK LOAD: -1 HEALTH");
		}

		private void SetupBlendFileDialog()
		{
			var layer = new CanvasLayer { Name = "BlendFileDialogLayer", Layer = 1400 };
			_blendFileDialog = new FileDialog
			{
				Name = "BlendFileDialog",
				Mode = FileDialog.ModeEnum.OpenFile,
				Access = FileDialog.AccessEnum.Filesystem,
				Filters = new[] { "*.blend ; Blender scene" }
			};
			layer.AddChild(_blendFileDialog);
			AddChild(layer);
			_blendFileDialog.Connect("file_selected", this, nameof(OnBlendFileSelected));
			_blendFileDialog.Connect("popup_hide", this, nameof(OnBlendFileDialogHidden));
		}

		private void ShowBlendFileDialog()
		{
			if (_blendFileDialog == null || _blendFileDialog.Visible)
				return;
			_mouseModeBeforeFileDialog = Input.MouseMode;
			Input.MouseMode = Input.MouseModeEnum.Visible;
			_blendFileDialog.PopupCenteredRatio(0.82f);
		}

		private void OnBlendFileDialogHidden()
		{
			Input.MouseMode = _mouseModeBeforeFileDialog;
		}

		private void OnBlendFileSelected(string path)
		{
			LoadBlendLevel(path, true);
		}

		private bool LoadBlendLevel(string path, bool showMessage)
		{
			try
			{
				LoadedBlendLevel loaded = BlendLevelLoader.Load(path);
				Spatial oldLevel = _levelRoot;
				_levelRoot = loaded.Root;
				AddChild(_levelRoot);
				SetMainSceneLightingEnabled(loaded.LightObjectCount == 0);
				if (oldLevel != null && IsInstanceValid(oldLevel))
					oldLevel.QueueFree();

				var noTargets = new List<ShootTarget>();
				_wireframe.Targets = noTargets;
				_accentDither.Targets = noTargets;
				_player.PlaceAt(loaded.PlayerSpawn);
				_stoneDropManager.ClearStones();
				_pet.RespawnAtPlayer();
				_hasQuickSave = false;
				_loadedBlendLevelPath = path;
				_player.ResetFullHealthRestoreLimit();
				_player.ResetKissHealingCooldown();
				GD.Print("Loaded .blend level: " + path + " ("
					+ loaded.MeshObjectCount + " mesh objects, "
					+ loaded.LightObjectCount + " light objects, "
					+ loaded.PathObjectCount + " movement paths).");
				if (showMessage)
					_player.ShowSystemMessage("BLEND LEVEL LOADED: " + loaded.MeshObjectCount + " OBJECTS");
				return true;
			}
			catch (System.Exception exception)
			{
				GD.PushError("Cannot load .blend level '" + path + "': " + exception);
				if (showMessage)
					_player.ShowSystemMessage("BLEND LOAD FAILED");
				return false;
			}
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
				AccentObjectDitherEnabled = AccentObjectDitherEnabledByDefault,
				PlatformOutlinesEnabled = PlatformOutlinesEnabledByDefault
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
				AccentObjectDitherEnabled = _accentDither.Enabled,
				PlatformOutlinesEnabled = _platformOutlinesEnabled
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
			SetPlatformOutlinesEnabled(state.PlatformOutlinesEnabled, false);
		}

		private void SetPlatformOutlinesEnabled(bool enabled, bool showMessage)
		{
			_platformOutlinesEnabled = enabled;
			GlobalSettings.SetPlatformOutlineMode(enabled);
			if (showMessage && _player != null && IsInstanceValid(_player))
			{
				_player.ShowSystemMessage(
					enabled ? "PLATFORM OUTLINES ENABLED" : "PLATFORM OUTLINES DISABLED");
			}
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
			public bool PlatformOutlinesEnabled;
		}

		private void BuildEnvironment()
		{
			var worldEnvironment = new WorldEnvironment { Name = "SkyboxEnvironment" };
			_mainSceneEnvironment = new Environment
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
			_mainSceneEnvironment.BackgroundSky = proceduralSky;
			_mainSceneEnvironment.BackgroundEnergy = 0.8f;
			_mainSceneAmbientLightEnergy = _mainSceneEnvironment.AmbientLightEnergy;
			worldEnvironment.Environment = _mainSceneEnvironment;
			AddChild(worldEnvironment);

			_mainSceneSun = new DirectionalLight
			{
				Name = "Sun",
				LightColor = new Color(0.88f, 0.91f, 1.0f),
				LightEnergy = 1.1f,
				ShadowEnabled = true,
				RotationDegrees = new Vector3(-52.0f, -28.0f, 0.0f)
			};
			AddChild(_mainSceneSun);
		}

		private void SetMainSceneLightingEnabled(bool enabled)
		{
			if (_mainSceneSun != null && IsInstanceValid(_mainSceneSun))
				_mainSceneSun.Visible = enabled;
			if (_mainSceneEnvironment != null)
			{
				_mainSceneEnvironment.AmbientLightEnergy = enabled
					? _mainSceneAmbientLightEnergy
					: 0.0f;
			}
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
			AddPlatform("Platform01", new Vector3(-5.5f, 1.0f, 2.5f), new Vector3(4.5f, 0.5f, 4.0f), _platformColor);
			AddPlatform("Platform02", new Vector3(0.0f, 2.2f, -1.5f), new Vector3(4.0f, 0.5f, 4.0f), _platformColor);
			AddPlatform("Platform03", new Vector3(5.4f, 3.4f, -5.0f), new Vector3(4.5f, 0.5f, 4.0f), _platformColor);
			AddPlatform("Platform04", new Vector3(-3.5f, 4.6f, -8.2f), new Vector3(5.0f, 0.5f, 3.2f), _platformColor);
			AddPlatform("FloatingStep", new Vector3(6.5f, 1.2f, 5.2f), new Vector3(2.4f, 0.45f, 2.4f), new Color(0.55f, 0.3f, 0.72f), true);
			AddPlatform(
				"HookPlatform",
				new Vector3(0.0f, 2.75f, 5.2f),
				new Vector3(2.5f, 0.5f, 1.2f),
				new Color(0.05f, 0.85f, 0.32f),
				false,
				true);
		}

		private List<ShootTarget> BuildTargets()
		{
			var targets = new List<ShootTarget>();
			targets.Add(AddTarget(new Vector3(-5.5f, 2.2f, 1.2f)));
			targets.Add(AddTarget(new Vector3(0.0f, 3.4f, -2.3f)));
			targets.Add(AddTarget(new Vector3(5.4f, 4.6f, -6.0f)));
			targets.Add(AddTarget(new Vector3(-3.5f, 5.8f, -8.8f)));
			ShootTarget lastTarget = AddTarget(new Vector3(8.8f, 1.3f, -9.5f));
			targets.Add(lastTarget);
			AddTargetTriggerField(lastTarget);
			return targets;
		}

		private void AddTargetTriggerField(ShootTarget target)
		{
			var triggerField = new TriggerField
			{
				Name = "ActivationTrigger",
				EventName = "last_target_activated",
				ActivateTarget = target
			};
			triggerField.AddChild(new CollisionShape
			{
				Shape = new BoxShape { Extents = new Vector3(2.5f, 2.5f, 2.5f) }
			});
			target.AddChild(triggerField);
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

		private StoneDropManager BuildStoneDropManager(PlayerController player)
		{
			var manager = new StoneDropManager
			{
				Name = "StoneDropManager",
				Player = player,
				StoneSideLength = StoneSideLength,
				SpawnIntervalSeconds = StoneSpawnIntervalSeconds,
				EvaluationDelaySeconds = StoneEvaluationDelaySeconds,
				MaximumStoneCount = MaximumStoneCount
			};
			AddChild(manager);
			return manager;
		}

		private Pet BuildPet(PlayerController player, StoneDropManager stoneManager)
		{
			Pet pet = null;
			Node stationaryVisual = null;
			Node movingVisual = null;
			if (PetScene != null)
			{
				Node instance = PetScene.Instance();
				pet = instance as Pet;
				if (pet == null)
					stationaryVisual = instance;
			}
			if (pet == null)
				pet = new Pet();
			if (MovingPetScene != null)
				movingVisual = MovingPetScene.Instance();

			pet.Name = "Pet";
			pet.Player = player;
			pet.StoneManager = stoneManager;
			pet.SideLength = PetSideLength;
			pet.PlayerReachDistance = PetPlayerReachDistance;
			pet.StoneSearchDistance = PetStoneSearchDistance;
			pet.MoveSpeed = PetMoveSpeed;
			pet.UniformVisualScale = PetScale;
			pet.JumpSpeed = PetJumpSpeed;
			pet.FallY = PetFallY;
			pet.UseDefaultVisual = stationaryVisual == null && PetScene == null;
			pet.StationaryVisual = stationaryVisual;
			pet.MovingVisual = movingVisual;
			if (stationaryVisual != null)
				pet.AddChild(stationaryVisual);
			if (movingVisual != null)
				pet.AddChild(movingVisual);
			AddChild(pet);
			pet.GlobalTransform = new Transform(
				Basis.Identity,
				player.GlobalTransform.origin
					+ Vector3.Right * Mathf.Max(PetPlayerReachDistance * 0.6f, 0.75f)
					+ Vector3.Up * (Mathf.Max(PetSideLength, 0.05f) * 0.5f + 0.05f));
			return pet;
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
				&& _accentDither.Enabled
				&& _platformOutlinesEnabled
				&& GlobalSettings.PlatformOutlineModeEnabled;
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
			_levelRoot.AddChild(target);
			target.Translation = position;
			return target;
		}

		private void AddBox(string name, Vector3 position, Vector3 size, Color color)
		{
			var body = new StaticBody { Name = name, Translation = position };
			AddBoxGeometry(body, size, color);
		}

		private void AddPlatform(
			string name,
			Vector3 position,
			Vector3 size,
			Color color,
			bool specialVisibility = false,
			bool isHook = false)
		{
			var body = new PlatformProps
			{
				Name = name,
				Translation = position,
				SpecialVisibility = specialVisibility,
				IsHook = isHook
			};
			AddBoxGeometry(body, size, color);
		}

		private static void RegisterPlayerWithPlatforms(Node root, PlayerController player)
		{
			if (root == null || player == null)
				return;

			PlatformProps platform = root as PlatformProps;
			if (platform != null)
				platform.RegisterPlayer(player);

			foreach (Node child in root.GetChildren())
				RegisterPlayerWithPlatforms(child, player);
		}

		private void AddBoxGeometry(StaticBody body, Vector3 size, Color color)
		{
			var shape = new CollisionShape { Shape = new BoxShape { Extents = size * 0.5f } };
			var mesh = new MeshInstance
			{
				Mesh = new CubeMesh { Size = size },
				MaterialOverride = MakeMaterial(color, 0.72f)
			};
			body.AddChild(shape);
			body.AddChild(mesh);
			_levelRoot.AddChild(body);
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
