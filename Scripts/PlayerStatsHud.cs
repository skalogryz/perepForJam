using Godot;

namespace PereSkyroom
{
	public class PlayerStatsHud : CanvasLayer
	{
		[Export] public Texture NormalSprite;
		[Export] public Texture BoostSprite;

		private Sprite _stateSprite;
		private ProgressBar _healthBar;
		private ProgressBar _temperatureBar;
		private bool _boostEnabled;

		public override void _Ready()
		{
			_stateSprite = GetNode<Sprite>("StatsPanel/BoostStateSprite");
			_healthBar = GetNode<ProgressBar>("StatsPanel/HealthBar");
			_temperatureBar = GetNode<ProgressBar>("StatsPanel/TemperatureBar");
			ApplyState();
		}

		public void SetBoostEnabled(bool enabled)
		{
			_boostEnabled = enabled;
			ApplyState();
		}

		public void SetHealth(float health, float maximumHealth)
		{
			if (_healthBar == null || !IsInstanceValid(_healthBar))
				return;
			_healthBar.MaxValue = Mathf.Max(maximumHealth, 1.0f);
			_healthBar.Value = Mathf.Clamp(health, 0.0f, maximumHealth);
		}

		public void SetTemperature(float temperature)
		{
			if (_temperatureBar == null || !IsInstanceValid(_temperatureBar))
				return;
			_temperatureBar.MinValue = 0.0f;
			_temperatureBar.MaxValue = 100.0f;
			_temperatureBar.Value = Mathf.Clamp(temperature, 0.0f, 100.0f);
		}

		private void ApplyState()
		{
			if (_stateSprite == null || !IsInstanceValid(_stateSprite))
				return;

			_stateSprite.Texture = _boostEnabled ? BoostSprite : NormalSprite;
			_stateSprite.Visible = _stateSprite.Texture != null;
		}
	}
}
