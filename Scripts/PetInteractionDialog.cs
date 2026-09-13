using Godot;

namespace PereSkyroom
{
	public class PetInteractionDialog : Control, IPlayerDialog
	{
		private const float KissHealingChance = 0.25f;
		private const float KissHealingAmount = 12.0f;
		private const float HitDamageChance = 0.10f;
		private const float HitDamageAmount = 24.0f;
		private const float HitFullHealingChance = 0.30f;

		private readonly RandomNumberGenerator _random = new RandomNumberGenerator();
		private PlayerController _player;
		private bool _choiceMade;

		public override void _Ready()
		{
			_random.Randomize();
			GetNode<Button>("Center/Panel/Buttons/KissButton").GrabFocus();
		}

		public void SetPlayer(PlayerController player)
		{
			_player = player;
		}

		public void OnKissPressed()
		{
			if (!BeginChoice())
				return;
			if (_random.Randf() < KissHealingChance
				&& _player != null && IsInstanceValid(_player))
			{
				_player.TryRestoreHealthFromKiss(KissHealingAmount);
			}
			GlobalSettings.DialogClosing();
		}

		public void OnHitPressed()
		{
			if (!BeginChoice())
				return;
			float outcome = _random.Randf();
			if (_player != null && IsInstanceValid(_player))
			{
				if (outcome < HitDamageChance)
					_player.ApplyDamage(HitDamageAmount);
				else if (outcome < HitDamageChance + HitFullHealingChance)
					_player.TryRestoreFullHealth();
			}
			GlobalSettings.DialogClosing();
		}

		public void OnNothingPressed()
		{
			if (!BeginChoice())
				return;
			GlobalSettings.DialogClosing();
		}

		private bool BeginChoice()
		{
			if (_choiceMade)
				return false;
			_choiceMade = true;
			return true;
		}
	}
}
