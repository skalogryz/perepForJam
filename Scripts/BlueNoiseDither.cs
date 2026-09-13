using Godot;
using System;

namespace PereSkyroom
{
	public class BlueNoiseDither : CanvasLayer
	{
		private const int NoiseSize = 64;
		private const float DamagePaletteFlashDurationSeconds = 0.3f;
		private const float DamagePaletteFlashStepSeconds = 0.1f;
		private const string DitherShader = @"
shader_type canvas_item;
render_mode unshaded;

uniform sampler2D blue_noise;
uniform vec2 blue_noise_size = vec2(64.0, 64.0);
uniform vec4 dark_color : hint_color;
uniform vec4 light_color : hint_color;
uniform vec4 accent_color : hint_color;
uniform float green_threshold = 0.5;

void fragment() {
	vec3 source = texture(SCREEN_TEXTURE, SCREEN_UV).rgb;
	float luminance = dot(source, vec3(0.2126, 0.7152, 0.0722));
	vec2 screen_pixel = floor(SCREEN_UV / SCREEN_PIXEL_SIZE);
	vec2 noise_uv = (mod(screen_pixel, blue_noise_size) + vec2(0.5)) / blue_noise_size;
	float noise_threshold = texture(blue_noise, noise_uv).r;

	// vec4 selected_light_color = source.g > green_threshold ? accent_color : light_color;
	bool is_green = max(source.r, source.b) < source.g * 0.8;
	vec4 selected_light_color = (is_green) ? accent_color : light_color;

	COLOR = luminance >= noise_threshold ? selected_light_color : dark_color;
}";

		public Color DarkColor = new Color(0.035f, 0.045f, 0.075f, 1.0f);
		public Color LightColor = new Color(0.95f, 0.82f, 0.36f, 1.0f);
		public Color AccentColor = new Color(0.0f, 1.0f, 0.0f, 1.0f);
		public float GreenAccentThreshold = 0.5f;
		public bool InvertPaletteByDefault;
		public PlayerController Player;
		public AccentObjectDitherMode AccentDitherMode;

		private ColorRect _overlay;
		private ShaderMaterial _material;
		private Texture _externalNoiseTexture;
		private ImageTexture _generatedNoiseTexture;
		private Texture _activeNoiseTexture;
		private bool _enabled;
		private bool _paletteInverted;
		private bool _damagePaletteFlashActive;
		private bool _damagePaletteSwapped;
		private float _damagePaletteFlashRemaining;
		private float _damagePaletteFlashStepElapsed;
		public bool Enabled { get { return _enabled; } }
		public bool PaletteInverted { get { return _paletteInverted; } }
		private bool IsPaletteVisuallyInverted { get { return _paletteInverted != _damagePaletteSwapped; } }
		public Color EffectiveDarkColor { get { return IsPaletteVisuallyInverted ? LightColor : DarkColor; } }
		public Color EffectiveLightColor { get { return IsPaletteVisuallyInverted ? DarkColor : LightColor; } }
		public Texture NoiseTexture
		{
			get { return _externalNoiseTexture; }
			set
			{
				_externalNoiseTexture = value;
				ApplyNoiseTexture();
			}
		}
		public Texture BlueNoiseTexture { get { return _activeNoiseTexture; } }

		public override void _Ready()
		{
			Layer = 1000;
			_material = new ShaderMaterial { Shader = new Shader { Code = DitherShader } };
			ApplyNoiseTexture();
			_material.SetShaderParam("accent_color", AccentColor);
			_material.SetShaderParam("green_threshold", Mathf.Clamp(GreenAccentThreshold, 0.0f, 1.0f));
			_paletteInverted = InvertPaletteByDefault;
			ApplyEffectivePalette();

			_overlay = new ColorRect
			{
				Name = "FullScreenTwoColorDither",
				AnchorRight = 1.0f,
				AnchorBottom = 1.0f,
				MouseFilter = Control.MouseFilterEnum.Ignore,
				Material = _material,
				Visible = false
			};
			AddChild(_overlay);
			if (Player != null && IsInstanceValid(Player))
				Player.SetDitherPostProcess(this);
		}

		public override void _Process(float delta)
		{
			UpdateDamagePaletteFlash(delta);
			if (Input.IsActionJustPressed("toggle_dither"))
				SetEnabled(!_enabled);
			if (Input.IsActionJustPressed("toggle_dither_palette"))
				SetPaletteInverted(!_paletteInverted);
		}

		public void SetEnabled(bool enabled, bool notifyPlayer = true)
		{
			_enabled = enabled;
			_overlay.Visible = _enabled;
			if (!_enabled && AccentDitherMode != null && IsInstanceValid(AccentDitherMode)
				&& AccentDitherMode.Enabled)
				AccentDitherMode.SetEnabled(false, notifyPlayer);
			if (Player != null && IsInstanceValid(Player))
				Player.SetDitherMode(_enabled, notifyPlayer);
		}

		public void SetPalette(Color darkColor, Color lightColor)
		{
			DarkColor = darkColor;
			LightColor = lightColor;
			ApplyEffectivePalette();
		}

		public void SetPaletteInverted(bool inverted, bool notifyPlayer = true)
		{
			_paletteInverted = inverted;
			ApplyEffectivePalette();
			if (Player != null && IsInstanceValid(Player))
				Player.SetDitherPaletteInverted(_paletteInverted, notifyPlayer);
		}

		public void PlayDamagePaletteFlash()
		{
			_damagePaletteFlashRemaining = DamagePaletteFlashDurationSeconds;
			if (_damagePaletteFlashActive)
				return;

			_damagePaletteFlashActive = true;
			_damagePaletteSwapped = true;
			ApplyEffectivePalette();
		}

		private void UpdateDamagePaletteFlash(float delta)
		{
			if (!_damagePaletteFlashActive)
				return;

			_damagePaletteFlashRemaining -= delta;
			_damagePaletteFlashStepElapsed += delta;
			while (_damagePaletteFlashStepElapsed >= DamagePaletteFlashStepSeconds
				&& _damagePaletteFlashRemaining > 0.0f)
			{
				_damagePaletteFlashStepElapsed -= DamagePaletteFlashStepSeconds;
				_damagePaletteSwapped = !_damagePaletteSwapped;
				ApplyEffectivePalette();
			}

			if (_damagePaletteFlashRemaining > 0.0f)
				return;

			_damagePaletteFlashRemaining = 0.0f;
			_damagePaletteFlashStepElapsed = 0.0f;
			_damagePaletteFlashActive = false;
			_damagePaletteSwapped = false;
			ApplyEffectivePalette();
		}

		private void ApplyEffectivePalette()
		{
			if (_material == null)
				return;
			_material.SetShaderParam("dark_color", EffectiveDarkColor);
			_material.SetShaderParam("light_color", EffectiveLightColor);
		}

		private void ApplyNoiseTexture()
		{
			if (_externalNoiseTexture == null && _generatedNoiseTexture == null)
				_generatedNoiseTexture = CreateBlueNoiseTexture();

			_activeNoiseTexture = _externalNoiseTexture ?? _generatedNoiseTexture;
			if (_material != null)
			{
				_material.SetShaderParam("blue_noise", _activeNoiseTexture);
				_material.SetShaderParam("blue_noise_size", GetNoiseTextureSize(_activeNoiseTexture));
			}
			if (AccentDitherMode != null && IsInstanceValid(AccentDitherMode))
				AccentDitherMode.SetBlueNoiseTexture(_activeNoiseTexture);
		}

		private Vector2 GetNoiseTextureSize(Texture texture)
		{
			if (texture == null)
				return new Vector2(NoiseSize, NoiseSize);
			Vector2 size = texture.GetSize();
			return new Vector2(Mathf.Max(1.0f, size.x), Mathf.Max(1.0f, size.y));
		}

		private ImageTexture CreateBlueNoiseTexture()
		{
			int[] ranks = GenerateVoidAndClusterRanks(NoiseSize);
			var image = new Image();
			image.Create(NoiseSize, NoiseSize, false, Image.Format.Rgba8);
			image.Lock();
			float denominator = NoiseSize * NoiseSize;
			for (int y = 0; y < NoiseSize; y++)
			{
				for (int x = 0; x < NoiseSize; x++)
				{
					float value = (ranks[x + y * NoiseSize] + 0.5f) / denominator;
					image.SetPixel(x, y, new Color(value, value, value, 1.0f));
				}
			}
			image.Unlock();

			var texture = new ImageTexture();
			texture.CreateFromImage(image, 0);
			return texture;
		}

		// Ulichney-style void-and-cluster ranking. The toroidal Gaussian density
		// pushes equal-valued samples apart, producing a tileable blue-noise mask.
		private int[] GenerateVoidAndClusterRanks(int size)
		{
			int count = size * size;
			int half = count / 2;
			bool[] pattern = new bool[count];
			int[] shuffled = new int[count];
			for (int i = 0; i < count; i++)
				shuffled[i] = i;

			var random = new Random(24701);
			for (int i = count - 1; i > 0; i--)
			{
				int j = random.Next(i + 1);
				int temporary = shuffled[i];
				shuffled[i] = shuffled[j];
				shuffled[j] = temporary;
			}
			for (int i = 0; i < half; i++)
				pattern[shuffled[i]] = true;

			float[] density = BuildDensity(pattern, size);
			for (int iteration = 0; iteration < count * 3; iteration++)
			{
				int cluster = FindExtreme(density, pattern, true, true);
				pattern[cluster] = false;
				UpdateDensity(density, cluster, size, -1.0f);
				int empty = FindExtreme(density, pattern, false, false);
				if (empty == cluster)
				{
					pattern[cluster] = true;
					UpdateDensity(density, cluster, size, 1.0f);
					break;
				}
				pattern[empty] = true;
				UpdateDensity(density, empty, size, 1.0f);
			}

			bool[] optimized = (bool[])pattern.Clone();
			int[] ranks = new int[count];
			density = BuildDensity(pattern, size);
			for (int rank = half - 1; rank >= 0; rank--)
			{
				int cluster = FindExtreme(density, pattern, true, true);
				ranks[cluster] = rank;
				pattern[cluster] = false;
				UpdateDensity(density, cluster, size, -1.0f);
			}

			pattern = optimized;
			density = BuildDensity(pattern, size);
			for (int rank = half; rank < count; rank++)
			{
				int empty = FindExtreme(density, pattern, false, false);
				ranks[empty] = rank;
				pattern[empty] = true;
				UpdateDensity(density, empty, size, 1.0f);
			}
			return ranks;
		}

		private float[] BuildDensity(bool[] pattern, int size)
		{
			float[] density = new float[pattern.Length];
			for (int i = 0; i < pattern.Length; i++)
			{
				if (pattern[i])
					UpdateDensity(density, i, size, 1.0f);
			}
			return density;
		}

		private void UpdateDensity(float[] density, int point, int size, float sign)
		{
			const int radius = 5;
			const float sigmaSquaredTimesTwo = 4.5f;
			int centerX = point % size;
			int centerY = point / size;
			for (int offsetY = -radius; offsetY <= radius; offsetY++)
			{
				int y = (centerY + offsetY + size) % size;
				for (int offsetX = -radius; offsetX <= radius; offsetX++)
				{
					int x = (centerX + offsetX + size) % size;
					float distanceSquared = offsetX * offsetX + offsetY * offsetY;
					density[x + y * size] += sign * Mathf.Exp(-distanceSquared / sigmaSquaredTimesTwo);
				}
			}
		}

		private int FindExtreme(float[] density, bool[] pattern, bool occupied, bool findMaximum)
		{
			int best = -1;
			float bestValue = findMaximum ? float.NegativeInfinity : float.PositiveInfinity;
			for (int i = 0; i < density.Length; i++)
			{
				if (pattern[i] != occupied)
					continue;
				if ((findMaximum && density[i] > bestValue) || (!findMaximum && density[i] < bestValue))
				{
					best = i;
					bestValue = density[i];
				}
			}
			return best;
		}
	}
}
