using Godot;

namespace PereSkyroom
{
	public class Main : Spatial
	{
		private readonly Color _floorColor = new Color(0.16f, 0.19f, 0.26f);
		private readonly Color _wallColor = new Color(0.10f, 0.13f, 0.20f);
		private readonly Color _platformColor = new Color(0.18f, 0.55f, 0.72f);

		public override void _Ready()
		{
			BuildEnvironment();
			BuildRoom();
			BuildPlatforms();
			BuildTargets();
			BuildPlayer();

			foreach (string argument in OS.GetCmdlineArgs())
			{
				if (argument == "--smoke-test" || argument == "--no-window")
				{
					GD.Print("PERE_SMOKE_TEST_OK");
					GetTree().Quit();
					break;
				}
			}
		}

		private void BuildEnvironment()
		{
			var worldEnvironment = new WorldEnvironment { Name = "SkyboxEnvironment" };
			var environment = new Environment
			{
				BackgroundMode = Environment.BGMode.Sky,
				AmbientLightColor = new Color(0.46f, 0.55f, 0.75f),
				AmbientLightEnergy = 0.65f,
				TonemapMode = Environment.ToneMapper.Filmic,
				TonemapExposure = 1.15f,
				SsaoEnabled = true,
				GlowEnabled = true
			};
			var proceduralSky = new ProceduralSky
			{
				SkyTopColor = new Color(0.015f, 0.035f, 0.12f),
				SkyHorizonColor = new Color(0.42f, 0.22f, 0.48f),
				GroundBottomColor = new Color(0.02f, 0.025f, 0.06f),
				GroundHorizonColor = new Color(0.22f, 0.13f, 0.25f),
				SunLatitude = 28.0f,
				SunLongitude = 145.0f,
				SunEnergy = 3.5f,
				TextureSize = ProceduralSky.TextureSizeEnum.Size1024
			};
			environment.BackgroundSky = proceduralSky;
			environment.BackgroundEnergy = 0.8f;
			worldEnvironment.Environment = environment;
			AddChild(worldEnvironment);

			var sun = new DirectionalLight
			{
				Name = "Sun",
				LightColor = new Color(0.88f, 0.91f, 1.0f),
				LightEnergy = 1.1f,
				ShadowEnabled = true,
				RotationDegrees = new Vector3(-52.0f, -28.0f, 0.0f)
			};
			AddChild(sun);
		}

		private void BuildRoom()
		{
			AddBox("Floor", new Vector3(0, -0.5f, 0), new Vector3(24, 1, 24), _floorColor);
			AddBox("NorthWall", new Vector3(0, 4, -12), new Vector3(24, 9, 0.5f), _wallColor);
			AddBox("WestWall", new Vector3(-12, 4, 0), new Vector3(0.5f, 9, 24), _wallColor);
			AddBox("EastWall", new Vector3(12, 4, 0), new Vector3(0.5f, 9, 24), _wallColor);
			AddBox("SouthWallLow", new Vector3(0, 1, 12), new Vector3(24, 2, 0.5f), _wallColor);
		}

		private void BuildPlatforms()
		{
			AddBox("Platform01", new Vector3(-5.5f, 1.0f, 2.5f), new Vector3(4.5f, 0.5f, 4.0f), _platformColor);
			AddBox("Platform02", new Vector3(0.0f, 2.2f, -1.5f), new Vector3(4.0f, 0.5f, 4.0f), _platformColor);
			AddBox("Platform03", new Vector3(5.4f, 3.4f, -5.0f), new Vector3(4.5f, 0.5f, 4.0f), _platformColor);
			AddBox("Platform04", new Vector3(-3.5f, 4.6f, -8.2f), new Vector3(5.0f, 0.5f, 3.2f), _platformColor);
			AddBox("FloatingStep", new Vector3(6.5f, 1.2f, 5.2f), new Vector3(2.4f, 0.45f, 2.4f), new Color(0.55f, 0.3f, 0.72f));
		}

		private void BuildTargets()
		{
			AddTarget(new Vector3(-5.5f, 2.2f, 1.2f));
			AddTarget(new Vector3(0.0f, 3.4f, -2.3f));
			AddTarget(new Vector3(5.4f, 4.6f, -6.0f));
			AddTarget(new Vector3(-3.5f, 5.8f, -8.8f));
			AddTarget(new Vector3(8.8f, 1.3f, -9.5f));
		}

		private void BuildPlayer()
		{
			var player = new PlayerController { Name = "Player" };
			AddChild(player);
			player.Translation = new Vector3(0, 1.2f, 8.0f);
		}

		private void AddTarget(Vector3 position)
		{
			var target = new ShootTarget();
			AddChild(target);
			target.Translation = position;
		}

		private void AddBox(string name, Vector3 position, Vector3 size, Color color)
		{
			var body = new StaticBody { Name = name, Translation = position };
			var shape = new CollisionShape { Shape = new BoxShape { Extents = size * 0.5f } };
			var mesh = new MeshInstance
			{
				Mesh = new CubeMesh { Size = size },
				MaterialOverride = MakeMaterial(color, 0.72f)
			};
			body.AddChild(shape);
			body.AddChild(mesh);
			AddChild(body);
		}

		private SpatialMaterial MakeMaterial(Color color, float roughness)
		{
			return new SpatialMaterial
			{
				AlbedoColor = color,
				Roughness = roughness,
				Metallic = 0.12f
			};
		}
	}
}
