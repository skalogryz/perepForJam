using Godot;
using System.Collections.Generic;

namespace PereSkyroom
{
	public class PlatformProps : StaticBody
	{
		[Export] public bool SpecialVisibility = false;
		[Export] public bool IsHook = false;

		private readonly HashSet<PlayerController> _ignoredPlayers
			= new HashSet<PlayerController>();
		private readonly HashSet<PlayerController> _attachedPlayers
			= new HashSet<PlayerController>();
		private Area _hookArea;

		public override void _Ready()
		{
			if (IsHook)
				BuildHookArea();

			GlobalSettings settings = GlobalSettings.inst;
			if (settings != null && IsInstanceValid(settings)
				&& !settings.IsConnected(nameof(GlobalSettings.BoostModeChanged), this, nameof(OnBoostModeChanged)))
			{
				settings.Connect(nameof(GlobalSettings.BoostModeChanged), this, nameof(OnBoostModeChanged));
			}

			ApplyVisibility(GlobalSettings.BoostModeEnabled);
		}

		public override void _ExitTree()
		{
			GlobalSettings settings = GlobalSettings.inst;
			if (settings != null && IsInstanceValid(settings)
				&& settings.IsConnected(nameof(GlobalSettings.BoostModeChanged), this, nameof(OnBoostModeChanged)))
			{
				settings.Disconnect(nameof(GlobalSettings.BoostModeChanged), this, nameof(OnBoostModeChanged));
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

		private void ApplyVisibility(bool boostEnabled)
		{
			// Spatial visibility does not disable StaticBody collisions.
			Visible = _attachedPlayers.Count == 0
				&& (!SpecialVisibility || boostEnabled);
		}
	}
}
