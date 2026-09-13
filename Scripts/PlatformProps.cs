using Godot;
using System.Collections.Generic;

namespace PereSkyroom
{
	public class PlatformProps : StaticBody
	{
		[Export] public bool SpecialVisibility = false;
		[Export] public bool IsHook = false;
		[Export] public bool IsMoveOnPath = false;
		[Export] public NodePath MovementPath;
		[Export] public float PathMoveSpeed = 3.0f;
		[Export] public float PathReturnDurationSeconds = 0.2f;
		[Export] public Color OutlineColor = new Color(0.0f, 1.0f, 0.0f, 1.0f);
		[Export(PropertyHint.Range, "0.001,0.25,0.001")]
		public float OutlineWidth = 0.04f;

		private readonly HashSet<PlayerController> _ignoredPlayers
			= new HashSet<PlayerController>();
		private readonly HashSet<PlayerController> _attachedPlayers
			= new HashSet<PlayerController>();
		private readonly List<MeshInstance> _outlineMeshes = new List<MeshInstance>();
		private const string OutlineShaderPath = "res://Resources/platform_inverse_hull.shader";
		private static Shader _outlineShader;
		private Area _hookArea;
		private Path _movementPath;
		private float _pathLength;
		private float _pathOffset;
		private float _returnStartOffset;
		private float _returnElapsed;
		private bool _movingForward;
		private bool _returningToStart;

		public override void _Ready()
		{
			BuildInverseHullOutlines();
			SetupPathMovement();
			if (IsHook)
				BuildHookArea();

			GlobalSettings settings = GlobalSettings.inst;
			if (settings != null && IsInstanceValid(settings)
				&& !settings.IsConnected(nameof(GlobalSettings.BoostModeChanged), this, nameof(OnBoostModeChanged)))
			{
				settings.Connect(nameof(GlobalSettings.BoostModeChanged), this, nameof(OnBoostModeChanged));
			}
			if (settings != null && IsInstanceValid(settings)
				&& !settings.IsConnected(
					nameof(GlobalSettings.PlatformOutlineModeChanged),
					this,
					nameof(OnPlatformOutlineModeChanged)))
			{
				settings.Connect(
					nameof(GlobalSettings.PlatformOutlineModeChanged),
					this,
					nameof(OnPlatformOutlineModeChanged));
			}

			ApplyVisibility(GlobalSettings.BoostModeEnabled);
			ApplyOutlineVisibility(GlobalSettings.PlatformOutlineModeEnabled);
		}

		public override void _PhysicsProcess(float delta)
		{
			if (!CanMoveOnPath())
			{
				SetPhysicsProcess(false);
				return;
			}

			if (_movingForward)
			{
				float distance = Mathf.Max(0.0f, PathMoveSpeed) * delta;
				_pathOffset = Mathf.Min(_pathOffset + distance, _pathLength);
				ApplyPathOffset();
				if (_pathOffset >= _pathLength)
				{
					_movingForward = false;
					SetPhysicsProcess(false);
				}
				return;
			}

			if (_returningToStart)
			{
				float duration = Mathf.Max(0.001f, PathReturnDurationSeconds);
				_returnElapsed += delta;
				float weight = Mathf.Clamp(_returnElapsed / duration, 0.0f, 1.0f);
				_pathOffset = Mathf.Lerp(_returnStartOffset, 0.0f, weight);
				ApplyPathOffset();
				if (weight >= 1.0f)
				{
					_pathOffset = 0.0f;
					_returningToStart = false;
					SetPhysicsProcess(false);
				}
			}
		}

		public override void _ExitTree()
		{
			GlobalSettings settings = GlobalSettings.inst;
			if (settings != null && IsInstanceValid(settings)
				&& settings.IsConnected(nameof(GlobalSettings.BoostModeChanged), this, nameof(OnBoostModeChanged)))
			{
				settings.Disconnect(nameof(GlobalSettings.BoostModeChanged), this, nameof(OnBoostModeChanged));
			}
			if (settings != null && IsInstanceValid(settings)
				&& settings.IsConnected(
					nameof(GlobalSettings.PlatformOutlineModeChanged),
					this,
					nameof(OnPlatformOutlineModeChanged)))
			{
				settings.Disconnect(
					nameof(GlobalSettings.PlatformOutlineModeChanged),
					this,
					nameof(OnPlatformOutlineModeChanged));
			}

			_ignoredPlayers.Clear();
			_attachedPlayers.Clear();
		}

		public void RegisterPlayer(PlayerController player)
		{
			if (!IsHook || player == null || !IsInstanceValid(player))
				return;

			// The exception must exist on the moving body (the player). Keeping it
			// on both bodies also makes the intent explicit if a hook is animated.
			player.AddCollisionExceptionWith(this);
			AddCollisionExceptionWith(player);
		}

		public void SetPlayerAttached(PlayerController player, bool attached)
		{
			if (player == null)
				return;

			if (attached)
				_attachedPlayers.Add(player);
			else
				_attachedPlayers.Remove(player);

			if (attached)
				StartPathMovement();
			else if (_attachedPlayers.Count == 0)
				StartPathReturn();

			ApplyVisibility(GlobalSettings.BoostModeEnabled);
		}

		public void IgnorePlayerUntilExit(PlayerController player)
		{
			if (player != null && IsInstanceValid(player))
				_ignoredPlayers.Add(player);
		}

		private void BuildHookArea()
		{
			_hookArea = new Area
			{
				Name = "HookArea",
				CollisionLayer = 0,
				CollisionMask = PlayerController.PlayerTriggerLayer,
				Monitoring = true,
				Monitorable = false
			};

			foreach (Node child in GetChildren())
			{
				CollisionShape source = child as CollisionShape;
				if (source == null || source.Shape == null)
					continue;

				_hookArea.AddChild(new CollisionShape
				{
					Name = source.Name + "HookShape",
					Shape = source.Shape.Duplicate() as Shape,
					Transform = source.Transform,
					Disabled = source.Disabled
				});
				// disabling the object collission shape
				source.Disabled = true;
			}

			AddChild(_hookArea);
			_hookArea.Connect("body_entered", this, nameof(OnHookBodyEntered));
			_hookArea.Connect("body_exited", this, nameof(OnHookBodyExited));
		}

		private void OnHookBodyEntered(Node body)
		{
			PlayerController player = body as PlayerController;
			if (player == null)
				return;

			RegisterPlayer(player);
			if (!_ignoredPlayers.Contains(player))
				player.TryAttachToHook(this);
		}

		private void OnHookBodyExited(Node body)
		{
			PlayerController player = body as PlayerController;
			if (player != null)
				_ignoredPlayers.Remove(player);
		}

		private void OnBoostModeChanged(bool enabled)
		{
			ApplyVisibility(enabled);
		}

		private void OnPlatformOutlineModeChanged(bool enabled)
		{
			ApplyOutlineVisibility(enabled);
		}

		private void BuildInverseHullOutlines()
		{
			var sourceMeshes = new List<MeshInstance>();
			CollectMeshInstances(this, sourceMeshes);
			if (sourceMeshes.Count == 0)
				return;

			if (_outlineShader == null)
				_outlineShader = ResourceLoader.Load<Shader>(OutlineShaderPath);
			if (_outlineShader == null)
			{
				GD.PushError("Cannot load platform outline shader: " + OutlineShaderPath);
				return;
			}

			for (int i = 0; i < sourceMeshes.Count; i++)
			{
				MeshInstance source = sourceMeshes[i];
				if (source.Mesh == null || source.GetParent() == null)
					continue;

				var material = new ShaderMaterial { Shader = _outlineShader };
				AABB bounds = source.GetAabb();
				Vector3 halfSize = bounds.Size * 0.5f;
				float outlineWidth = Mathf.Max(OutlineWidth, 0.001f);
				Vector3 hullScale = new Vector3(
					1.0f + outlineWidth / Mathf.Max(Mathf.Abs(halfSize.x), 0.0001f),
					1.0f + outlineWidth / Mathf.Max(Mathf.Abs(halfSize.y), 0.0001f),
					1.0f + outlineWidth / Mathf.Max(Mathf.Abs(halfSize.z), 0.0001f));
				material.SetShaderParam("outline_color", OutlineColor);
				material.SetShaderParam("hull_center", bounds.Position + halfSize);
				material.SetShaderParam("hull_scale", hullScale);
				var outline = new MeshInstance
				{
					Name = source.Name + "InverseHullOutline",
					Mesh = source.Mesh,
					Transform = source.Transform,
					Layers = source.Layers,
					Visible = false,
					CastShadow = GeometryInstance.ShadowCastingSetting.Off,
					MaterialOverride = material
				};
				source.GetParent().AddChild(outline);
				_outlineMeshes.Add(outline);
			}
		}

		private static void CollectMeshInstances(Node parent, List<MeshInstance> result)
		{
			foreach (Node child in parent.GetChildren())
			{
				MeshInstance mesh = child as MeshInstance;
				if (mesh != null)
					result.Add(mesh);
				CollectMeshInstances(child, result);
			}
		}

		private void ApplyOutlineVisibility(bool enabled)
		{
			for (int i = 0; i < _outlineMeshes.Count; i++)
			{
				MeshInstance outline = _outlineMeshes[i];
				if (outline != null && IsInstanceValid(outline))
					outline.Visible = enabled;
			}
		}

		private void SetupPathMovement()
		{
			SetPhysicsProcess(false);
			if (!IsHook || !IsMoveOnPath || MovementPath == null || MovementPath.IsEmpty()
				|| !HasNode(MovementPath))
				return;

			_movementPath = GetNode(MovementPath) as Path;
			if (_movementPath == null || _movementPath.Curve == null)
			{
				_movementPath = null;
				return;
			}

			_pathLength = _movementPath.Curve.GetBakedLength();
			if (_pathLength <= 0.0001f)
			{
				_movementPath = null;
				return;
			}

			_pathOffset = 0.0f;
			ApplyPathOffset();
		}

		private bool CanMoveOnPath()
		{
			return IsHook && IsMoveOnPath && _movementPath != null
				&& IsInstanceValid(_movementPath) && _movementPath.Curve != null
				&& _pathLength > 0.0001f;
		}

		private void StartPathMovement()
		{
			if (!CanMoveOnPath())
				return;

			_movingForward = true;
			_returningToStart = false;
			SetPhysicsProcess(true);
		}

		private void StartPathReturn()
		{
			if (!CanMoveOnPath())
				return;

			_movingForward = false;
			_returningToStart = true;
			_returnStartOffset = _pathOffset;
			_returnElapsed = 0.0f;
			SetPhysicsProcess(true);
		}

		private void ApplyPathOffset()
		{
			Vector3 localPosition = _movementPath.Curve.InterpolateBaked(_pathOffset, true);
			Transform transform = GlobalTransform;
			transform.origin = _movementPath.ToGlobal(localPosition);
			GlobalTransform = transform;
		}

		private void ApplyVisibility(bool boostEnabled)
		{
			// Spatial visibility does not disable StaticBody collisions.
			Visible = _attachedPlayers.Count == 0
				&& (!SpecialVisibility || boostEnabled);
		}
	}
}
