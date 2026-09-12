using Godot;

namespace PereSkyroom
{
	public class PlayerStatsHud : CanvasLayer
	{
		[Export] public Texture NormalSprite;
		[Export] public Texture BoostSprite;

		private Sprite _stateSprite;
		private bool _boostEnabled;

		public override void _Ready()
		{
			_stateSprite = GetNode<Sprite>("StatsPanel/BoostStateSprite");
			ApplyState();
		}

		public void SetBoostEnabled(bool enabled)
		{
			_boostEnabled = enabled;
			ApplyState();
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
