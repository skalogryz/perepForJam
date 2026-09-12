using Godot;

namespace PereSkyroom
{
	public class WeaponHud : CanvasLayer
	{
		[Export] public Texture NormalSprite1;
		[Export] public Texture NormalSprite2;
		[Export] public Texture BoostSprite1;
		[Export] public Texture BoostSprite2;
		[Export] public Texture HookNormalSprite1;
		[Export] public Texture HookNormalSprite2;
		[Export] public Texture HookBoostSprite1;
		[Export] public Texture HookBoostSprite2;
		[Export] public Vector2 NormalCanvasOffset = Vector2.Zero;
		[Export] public Vector2 BoostCanvasOffset = Vector2.Zero;
		[Export] public Vector2 HookNormalCanvasOffset = Vector2.Zero;
		[Export] public Vector2 HookBoostCanvasOffset = Vector2.Zero;
		[Export] public Vector2 Sprite1Offset = Vector2.Zero;
		[Export] public Vector2 Sprite2Offset = Vector2.Zero;
		[Export] public float SpriteScale = 1.0f;
		[Export] public float BobHorizontalPixels = 18.0f;
		[Export] public float BobVerticalPixels = 10.0f;
		[Export] public float BobCyclesPerSecond = 1.7f;
		[Export] public float ReturnDurationSeconds = 0.2f;

		private PlayerController _player;
		private Sprite _sprite1;
		private Sprite _sprite2;
		private bool _weaponVisible = true;
		private bool _wasMoving;
		private float _bobPhase;
		private float _movementBlendElapsed;
		private float _returnElapsed;
		private Vector2 _movementStartOffset;
		private Vector2 _returnStartOffset;
		private Vector2 _animationOffset;
		private SpriteState _state = SpriteState.Unknown;

		private enum SpriteState
		{
			Unknown,
			Normal,
			Boost,
			HookNormal,
			HookBoost
		}

		public override void _Ready()
		{
			_sprite1 = GetNode<Sprite>("WeaponSprite1");
			_sprite2 = GetNode<Sprite>("WeaponSprite2");
			_returnElapsed = Mathf.Max(ReturnDurationSeconds, 0.001f);
			RefreshSpriteSet();
			UpdateSpritePositions();
		}

		public override void _Process(float delta)
		{
			if (_player == null || !IsInstanceValid(_player))
				return;

			SpriteState state = GetSpriteState();
			if (state != _state)
				RefreshSpriteSet();

			float planarSpeed = _player.PlanarMovementSpeed;
			bool moving = !_player.IsOnHook && planarSpeed > 0.05f;
			if (moving)
				UpdateMovingOffset(planarSpeed, delta);
			else
				UpdateReturnOffset(delta);

			UpdateSpritePositions();
		}

		public void SetPlayer(PlayerController player)
		{
			_player = player;
			RefreshSpriteSet();
		}

		public void SetWeaponVisible(bool visible)
		{
			_weaponVisible = visible;
			ApplySpriteVisibility();
		}

		private void UpdateMovingOffset(float planarSpeed, float delta)
		{
			if (!_wasMoving)
			{
				_wasMoving = true;
				_bobPhase = 0.0f;
				_movementBlendElapsed = 0.0f;
				_movementStartOffset = _animationOffset;
			}

			float referenceSpeed = Mathf.Max(_player.NormalWalkSpeed, 0.01f);
			float speedFactor = Mathf.Clamp(planarSpeed / referenceSpeed, 0.35f, 2.5f);
			_bobPhase += Mathf.Pi * 2.0f
				* Mathf.Max(BobCyclesPerSecond, 0.01f) * speedFactor * delta;
			Vector2 waveOffset = new Vector2(
				Mathf.Sin(_bobPhase) * BobHorizontalPixels,
				(1.0f - Mathf.Cos(_bobPhase * 2.0f)) * 0.5f * BobVerticalPixels);

			_movementBlendElapsed += delta;
			float blend = Mathf.Clamp(_movementBlendElapsed / 0.1f, 0.0f, 1.0f);
			blend = blend * blend * (3.0f - 2.0f * blend);
			_animationOffset = _movementStartOffset.LinearInterpolate(waveOffset, blend);
		}

		private void UpdateReturnOffset(float delta)
		{
			if (_wasMoving)
			{
				_wasMoving = false;
				_returnElapsed = 0.0f;
				_returnStartOffset = _animationOffset;
			}

			float duration = Mathf.Max(ReturnDurationSeconds, 0.001f);
			_returnElapsed = Mathf.Min(_returnElapsed + delta, duration);
			_animationOffset = _returnStartOffset.LinearInterpolate(
				Vector2.Zero, _returnElapsed / duration);
		}

		private SpriteState GetSpriteState()
		{
			if (_player == null || !IsInstanceValid(_player))
				return SpriteState.Normal;
			if (_player.IsOnHook)
				return _player.SpeedBoostEnabled ? SpriteState.HookBoost : SpriteState.HookNormal;
			return _player.SpeedBoostEnabled ? SpriteState.Boost : SpriteState.Normal;
		}

		private void RefreshSpriteSet()
		{
			if (_sprite1 == null || _sprite2 == null)
				return;

			_state = GetSpriteState();
			switch (_state)
			{
				case SpriteState.Boost:
					_sprite1.Texture = BoostSprite1;
					_sprite2.Texture = BoostSprite2;
					Offset = BoostCanvasOffset;
					break;
				case SpriteState.HookNormal:
					_sprite1.Texture = HookNormalSprite1;
					_sprite2.Texture = HookNormalSprite2;
					Offset = HookNormalCanvasOffset;
					break;
				case SpriteState.HookBoost:
					_sprite1.Texture = HookBoostSprite1;
					_sprite2.Texture = HookBoostSprite2;
					Offset = HookBoostCanvasOffset;
					break;
				default:
					_sprite1.Texture = NormalSprite1;
					_sprite2.Texture = NormalSprite2;
					Offset = NormalCanvasOffset;
					break;
			}

			ApplySpriteVisibility();
			UpdateSpritePositions();
		}

		private void UpdateSpritePositions()
		{
			if (_sprite1 == null || _sprite2 == null)
				return;

			Vector2 screenSize = GetViewport().Size;
			bool hasTwoSprites = _sprite1.Texture != null && _sprite2.Texture != null;
			PositionSprite(_sprite1, Sprite1Offset, hasTwoSprites ? -1.0f : 0.0f, screenSize);
			PositionSprite(_sprite2, Sprite2Offset, hasTwoSprites ? 1.0f : 0.0f, screenSize);
		}

		private void PositionSprite(Sprite sprite, Vector2 offset, float dualSide, Vector2 screenSize)
		{
			if (sprite.Texture == null)
				return;

			float scale = Mathf.Max(SpriteScale, 0.001f);
			sprite.Scale = Vector2.One * scale;
			Vector2 textureSize = sprite.Texture.GetSize() * scale;
			float automaticDualOffset = dualSide * textureSize.x * 0.28f;
			sprite.Position = new Vector2(
				screenSize.x * 0.5f + automaticDualOffset,
				screenSize.y - textureSize.y * 0.5f)
				+ offset + _animationOffset;
		}

		private void ApplySpriteVisibility()
		{
			if (_sprite1 != null)
				_sprite1.Visible = _weaponVisible && _sprite1.Texture != null;
			if (_sprite2 != null)
				_sprite2.Visible = _weaponVisible && _sprite2.Texture != null;
		}
	}
}
