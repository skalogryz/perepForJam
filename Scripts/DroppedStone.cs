using Godot;

namespace PereSkyroom
{
	public class DroppedStone : RigidBody
	{
		public ulong SpawnTimeMilliseconds { get; private set; }
		public Vector3 TrackedPosition { get; private set; }
		public Vector3 PreviousTrackedPosition { get; private set; }
		public DroppedStone PreviousStone { get; set; }
		public bool PhysicsDisabled { get; private set; }
		public float SideLength { get; private set; }

		public void Initialize(float sideLength, PlayerController player)
		{
			SideLength = sideLength;
			SpawnTimeMilliseconds = OS.GetTicksMsec();
			TrackedPosition = GlobalTransform.origin;
			PreviousTrackedPosition = TrackedPosition;
			CollisionLayer = StoneDropManager.StoneCollisionLayer;
			CollisionMask = PlayerController.WorldCollisionLayer;
			CanSleep = true;

			var collision = new CollisionShape
			{
				Name = "CollisionShape",
				Shape = new BoxShape { Extents = Vector3.One * sideLength * 0.5f }
			};
			var material = new SpatialMaterial
			{
				AlbedoColor = new Color(0.0f, 1.0f, 0.0f, 1.0f),
				Roughness = 0.8f
			};
			var mesh = new MeshInstance
			{
				Name = "Mesh",
				Mesh = new CubeMesh { Size = Vector3.One * sideLength },
				MaterialOverride = material
			};
			AddChild(collision);
			AddChild(mesh);

			if (player != null)
				AddCollisionExceptionWith(player);
		}

		public void TrackPosition()
		{
			PreviousTrackedPosition = TrackedPosition;
			TrackedPosition = GlobalTransform.origin;
		}

		public bool IsMoving(float linearVelocityThreshold, float angularVelocityThreshold)
		{
			if (Sleeping)
				return false;
			return LinearVelocity.Length() > linearVelocityThreshold
				|| AngularVelocity.Length() > angularVelocityThreshold;
		}

		public void DisablePhysicsInteraction()
		{
			LinearVelocity = Vector3.Zero;
			AngularVelocity = Vector3.Zero;
			CollisionLayer = 0;
			CollisionMask = 0;
			Mode = ModeEnum.Static;
			PhysicsDisabled = true;
		}
	}
}
