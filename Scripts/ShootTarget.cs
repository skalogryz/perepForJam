using Godot;

namespace PereSkyroom
{
	public class ShootTarget : StaticBody
	{
		public readonly Vector3 AccentHalfExtents = new Vector3(0.5f, 0.65f, 0.18f);
		private bool _destroyed;

        public override void _Ready()
        {
            Name = "ShootTarget";
            var shape = new CollisionShape { Shape = new BoxShape { Extents = new Vector3(0.5f, 0.65f, 0.18f) } };
            AddChild(shape);

            var material = new SpatialMaterial
            {
                AlbedoColor = new Color(1.0f, 0.18f, 0.12f),
                EmissionEnabled = true,
                Emission = new Color(0.7f, 0.025f, 0.015f),
                EmissionEnergy = 1.8f,
                Metallic = 0.25f,
                Roughness = 0.32f
            };
            var mesh = new MeshInstance
            {
                Mesh = new CubeMesh { Size = new Vector3(1.0f, 1.3f, 0.35f) },
                MaterialOverride = material
            };
            AddChild(mesh);
        }

        public void Hit()
        {
            if (_destroyed)
                return;
            _destroyed = true;
            QueueFree();
        }
    }
}
