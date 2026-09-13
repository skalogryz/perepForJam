using Godot;

namespace PereSkyroom
{
	public class PlayerStatsHud : CanvasLayer
	{
		[Export] public Texture NormalSprite;
		[Export] public Texture BoostSprite;
		[Export] public NodePath BoostStateSpritePath;
		[Export] public NodePath HealthBarPath;
		[Export] public NodePath TemperatureBarPath;

		private Sprite _stateSprite;
		private ProgressBar _healthBar;
		private TextureProgress _temperatureBar;
		private bool _boostEnabled;

		public override void _Ready()
		{
			_stateSprite = GetAssignedNode<Sprite>(BoostStateSpritePath, nameof(BoostStateSpritePath));
			_healthBar = GetAssignedNode<ProgressBar>(HealthBarPath, nameof(HealthBarPath));
			_temperatureBar = GetAssignedNode<TextureProgress>(
				TemperatureBarPath, nameof(TemperatureBarPath));
			ApplyState();
		}

		private T GetAssignedNode<T>(NodePath path, string propertyName) where T : Node
		{
			if (path == null || path.IsEmpty())
			{
				GD.PushError("PlayerStatsHud." + propertyName + " is not assigned in the editor.");
				return null;
			}

			T node = GetNodeOrNull(path) as T;
			if (node == null)
				GD.PushError("PlayerStatsHud." + propertyName
					+ " does not reference a " + typeof(T).Name + ".");
			return node;
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
