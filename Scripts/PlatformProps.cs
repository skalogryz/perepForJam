using Godot;

namespace PereSkyroom
{
	public class PlatformProps : StaticBody
	{
		[Export] public bool SpecialVisibility = false;

		public override void _Ready()
		{
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
		}

		private void OnBoostModeChanged(bool enabled)
		{
			ApplyVisibility(enabled);
		}

		private void ApplyVisibility(bool boostEnabled)
		{
			// Spatial visibility does not disable StaticBody collisions.
			Visible = !SpecialVisibility || boostEnabled;
		}
	}
}
