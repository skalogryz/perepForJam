using Godot;

namespace PereSkyroom
{
	public class TriggerField : Area
	{
		[Signal]
		public delegate void Triggered(string eventName, Node player);

		[Export] public string EventName = string.Empty;
		[Export] public bool TriggerOnce = false;
		[Export] public bool Enabled = true;

		private bool _hasTriggered;

		public override void _Ready()
		{
			// The field has no physical layer of its own and only scans the
			// player's dedicated trigger layer.
			CollisionLayer = 0;
			CollisionMask = PlayerController.PlayerTriggerLayer;
			Monitorable = false;
			Monitoring = Enabled;
			Connect("body_entered", this, nameof(OnBodyEntered));
		}

		public void SetEnabled(bool enabled)
		{
			Enabled = enabled;
			Monitoring = enabled;
			if (enabled)
				_hasTriggered = false;
		}

		private void OnBodyEntered(Node body)
		{
			if (!Enabled || (TriggerOnce && _hasTriggered))
				return;

			var player = body as PlayerController;
			if (player == null)
				return;

			_hasTriggered = true;
			EmitSignal(nameof(Triggered), EventName, player);

			GlobalSettings.DoTriggerEvent(EventName, player, this);

			if (TriggerOnce)
				Monitoring = false;
		}
	}
}
