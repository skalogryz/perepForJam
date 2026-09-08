using Godot;
using System.Collections.Generic;

namespace PereSkyroom
{
	public class TriggerField : Area
	{
		[Signal]
		public delegate void Triggered(string eventName, Node player);

		[Export] public string EventName = string.Empty;
		[Export] public Spatial ActivateTarget;
		[Export] public bool TriggerOnce = false;
		[Export] public bool Enabled = true;

		private bool _hasTriggered;
		private readonly HashSet<PlayerController> _playersInside = new HashSet<PlayerController>();

		public override void _Ready()
		{
			// The field has no physical layer of its own and only scans the
			// player's dedicated trigger layer.
			CollisionLayer = 0;
			CollisionMask = PlayerController.PlayerTriggerLayer;
			Monitorable = false;
			Monitoring = Enabled;
			Connect("body_entered", this, nameof(OnBodyEntered));
			Connect("body_exited", this, nameof(OnBodyExited));
		}

		public override void _ExitTree()
		{
			GlobalSettings.UnregisterTriggerField(this);
			_playersInside.Clear();
		}

		public void SetEnabled(bool enabled)
		{
			Enabled = enabled;
			Monitoring = enabled;
			if (enabled)
				_hasTriggered = false;
			else
			{
				GlobalSettings.UnregisterTriggerField(this);
				_playersInside.Clear();
			}
		}

		public bool ContainsPlayer(PlayerController player)
		{
			return player != null && _playersInside.Contains(player);
		}

		public void Activate(PlayerController player)
		{
			if (!Enabled || (TriggerOnce && _hasTriggered) || !ContainsPlayer(player))
				return;

			FireEvent(player);
		}

		private void OnBodyEntered(Node body)
		{
			if (!Enabled || (TriggerOnce && _hasTriggered))
				return;

			var player = body as PlayerController;
			if (player == null)
				return;

			_playersInside.Add(player);
			if (ActivateTarget != null)
			{
				GlobalSettings.RegisterTriggerField(this);
				return;
			}

			FireEvent(player);
		}

		private void OnBodyExited(Node body)
		{
			var player = body as PlayerController;
			if (player == null)
				return;

			_playersInside.Remove(player);
			if (_playersInside.Count == 0)
				GlobalSettings.UnregisterTriggerField(this);
		}

		private void FireEvent(PlayerController player)
		{
			_hasTriggered = true;
			EmitSignal(nameof(Triggered), EventName, player);
			GlobalSettings.DoTriggerEvent(EventName, player, this);

			if (TriggerOnce)
			{
				GlobalSettings.UnregisterTriggerField(this);
				Monitoring = false;
			}
		}
	}
}
