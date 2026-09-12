using Godot;
using System.Collections.Generic;

namespace PereSkyroom
{
	public class Pet : KinematicBody
	{
		public const uint PetCollisionLayer = 1u << 3;
		private const float PlayerFacingSpeedDegreesPerSecond = 720.0f;

		public PlayerController Player;
		public StoneDropManager StoneManager;
		public float SideLength = 0.5f;
		public float PlayerReachDistance = 2.0f;
		public float StoneSearchDistance = 3.0f;
		public float MoveSpeed = 3.5f;
		public float Gravity = 22.0f;
		public float JumpSpeed = 7.0f;
		public float VerticalPlaneThreshold = 0.3f;
		public float StoneReachDistance = 0.4f;
		public float FallY = -15.0f;
		public float TriggerRadius = 1.5f;
		public float UniformVisualScale = 1.0f;
		public string TriggerEventName = "pet_activated";
		public bool UseDefaultVisual = true;

		private readonly List<DroppedStone> _stoneCandidates = new List<DroppedStone>();
		private DroppedStone _targetStone;
		private Vector3 _velocity;
		private bool _targetRequiresJump;
		private bool _jumpStartedForTarget;
		private bool _collisionlessJumpInProgress;
		private float _jumpLandingCenterY;

		public override void _Ready()
		{
			CollisionLayer = PetCollisionLayer;
			CollisionMask = PlayerController.WorldCollisionLayer;
			if (Player != null && IsInstanceValid(Player))
				AddCollisionExceptionWith(Player);

			BuildPhysicalBody();
			BuildTriggerField();
		}

		public override void _PhysicsProcess(float delta)
		{
			if (Player == null || !IsInstanceValid(Player))
				return;

			if (GlobalTransform.origin.y < FallY)
			{
				RespawnAtPlayer();
				return;
			}

			Vector3 playerPosition = Player.GlobalTransform.origin;
			if (Player.IsOnHook)
			{
				_targetStone = null;
				_targetRequiresJump = false;
				_jumpStartedForTarget = false;
				_collisionlessJumpInProgress = false;
				CollisionMask = PlayerController.WorldCollisionLayer;
				MoveWithGravity(Vector3.Zero, delta);
				FacePointSmoothly(playerPosition, delta);
				return;
			}

			float distanceToPlayer = GlobalTransform.origin.DistanceTo(playerPosition);
			if (distanceToPlayer <= PlayerReachDistance)
			{
				_targetStone = null;
				_targetRequiresJump = false;
				_jumpStartedForTarget = false;
				MoveWithGravity(Vector3.Zero, delta);
				FacePointSmoothly(playerPosition, delta);
				return;
			}

			if (!IsInstanceValid(_targetStone))
			{
				//GD.Print("Selecting stone");
				SelectTargetStone(playerPosition);
			}
			if (!IsInstanceValid(_targetStone))
			{
				//GD.Print("...failed to select stone");
				MoveWithGravity(Vector3.Zero, delta);
				return;
			}

			Vector3 targetPosition = _targetStone.TrackedPosition;
			Vector3 toTarget = targetPosition - GlobalTransform.origin;
			float reachDistance = Mathf.Max(_targetStone.SideLength, 0.01f);
			if (toTarget.Length() <= reachDistance)
			{
				//GD.Print("target reached");
				StoneManager.RemoveStone(_targetStone);
				_targetStone = null;
				_targetRequiresJump = false;
				_jumpStartedForTarget = false;
				if (GlobalTransform.origin.DistanceTo(playerPosition) <= PlayerReachDistance)
					FacePointSmoothly(playerPosition, delta);
				MoveWithGravity(Vector3.Zero, delta);
				return;
			}

			Vector3 horizontal = new Vector3(toTarget.x, 0.0f, toTarget.z);
			Vector3 horizontalVelocity = horizontal.LengthSquared() > 0.0001f
				? horizontal.Normalized() * MoveSpeed
				: Vector3.Zero;
			if (_targetRequiresJump && !_jumpStartedForTarget
				&& !_collisionlessJumpInProgress && IsOnFloor())
			{
				float requiredHeight = Mathf.Max(toTarget.y, 0.0f) + SideLength;
				float calculatedJump = Mathf.Sqrt(2.0f * Gravity * requiredHeight);
				_velocity.y = Mathf.Max(JumpSpeed, calculatedJump);
				_jumpStartedForTarget = true;
				BeginCollisionlessJump(_targetStone);
			}

			MoveWithGravity(horizontalVelocity, delta);
			FacePointSmoothly(targetPosition, delta);
		}

		public void RespawnAtPlayer()
		{
			if (Player == null || !IsInstanceValid(Player))
				return;
			GlobalTransform = new Transform(
				Basis.Identity,
				Player.GlobalTransform.origin + Vector3.Up * (SideLength * 0.5f + 0.05f));
			_velocity = Vector3.Zero;
			_targetStone = null;
			_targetRequiresJump = false;
			_jumpStartedForTarget = false;
			_collisionlessJumpInProgress = false;
			CollisionMask = PlayerController.WorldCollisionLayer;
		}

		private void SelectTargetStone(Vector3 playerPosition)
		{
			if (StoneManager == null || !IsInstanceValid(StoneManager))
				return;

			StoneManager.CopyStonesTo(_stoneCandidates);
			DroppedStone selected = null;
			float selectedPlayerDistance = float.MaxValue;
			ulong now = OS.GetTicksMsec();
			float minimumStoneAgeMilliseconds = Mathf.Max(
				StoneManager.EvaluationDelaySeconds, 0.0f) * 1000.0f;
			for (int i = 0; i < _stoneCandidates.Count; i++)
			{
				DroppedStone stone = _stoneCandidates[i];
				if (!IsInstanceValid(stone)
					|| !stone.PhysicsDisabled
					|| now - stone.SpawnTimeMilliseconds < minimumStoneAgeMilliseconds
					|| GlobalTransform.origin.DistanceTo(stone.TrackedPosition) > StoneSearchDistance)
					continue;

				float playerDistance = stone.TrackedPosition.DistanceTo(playerPosition);
				bool closerToPlayer = playerDistance < selectedPlayerDistance - 0.001f;
				bool sameDistanceButYounger = Mathf.Abs(playerDistance - selectedPlayerDistance) <= 0.001f
					&& (selected == null || stone.SpawnTimeMilliseconds > selected.SpawnTimeMilliseconds);
				if (!closerToPlayer && !sameDistanceButYounger)
					continue;

				selected = stone;
				selectedPlayerDistance = playerDistance;
			}

			if (selected == null)
			{
				float selectedPetDistance = float.MaxValue;
				for (int i = 0; i < _stoneCandidates.Count; i++)
				{
					DroppedStone stone = _stoneCandidates[i];
					if (!IsInstanceValid(stone) || !stone.PhysicsDisabled)
						continue;

					Vector3 petToStone = stone.TrackedPosition - GlobalTransform.origin;
					float petDistance = new Vector2(petToStone.x, petToStone.z).Length();
					if (petDistance >= selectedPetDistance)
						continue;
					selected = stone;
					selectedPetDistance = petDistance;
				}
			}

			if (selected == null)
				return;
			_targetStone = selected;
			_targetRequiresJump = Mathf.Abs(
				selected.TrackedPosition.y - GlobalTransform.origin.y) > VerticalPlaneThreshold;
			_jumpStartedForTarget = false;
			StoneManager.RemoveStonesOlderThan(selected.SpawnTimeMilliseconds);
		}

		private void MoveWithGravity(Vector3 horizontalVelocity, float delta)
		{
			float previousY = GlobalTransform.origin.y;
			_velocity.x = horizontalVelocity.x;
			_velocity.z = horizontalVelocity.z;
			if (!IsOnFloor())
				_velocity.y -= Gravity * delta;
			else if (_velocity.y < 0.0f)
				_velocity.y = 0.0f;
			_velocity = MoveAndSlide(_velocity, Vector3.Up, true, 4, Mathf.Deg2Rad(55.0f));
			TryFinishCollisionlessJump(previousY);
		}

		private void BeginCollisionlessJump(DroppedStone targetStone)
		{
			float stoneHalfHeight = Mathf.Max(targetStone.SideLength, 0.01f) * 0.5f;
			float petHalfHeight = Mathf.Max(SideLength, 0.05f) * 0.5f;
			float targetFloorY = targetStone.TrackedPosition.y - stoneHalfHeight;
			_jumpLandingCenterY = targetFloorY + petHalfHeight;
			_collisionlessJumpInProgress = true;
			CollisionMask = 0;
		}

		private void TryFinishCollisionlessJump(float previousY)
		{
			if (!_collisionlessJumpInProgress || _velocity.y > 0.0f)
				return;

			float currentY = GlobalTransform.origin.y;
			if (previousY < _jumpLandingCenterY || currentY > _jumpLandingCenterY)
				return;

			Transform transform = GlobalTransform;
			transform.origin = new Vector3(
				transform.origin.x,
				_jumpLandingCenterY,
				transform.origin.z);
			GlobalTransform = transform;
			_velocity.y = 0.0f;
			_collisionlessJumpInProgress = false;
			CollisionMask = PlayerController.WorldCollisionLayer;
		}

		private void FacePointSmoothly(Vector3 point, float delta)
		{
			Vector3 direction = point - GlobalTransform.origin;
			direction.y = 0.0f;
			if (direction.LengthSquared() <= 0.0001f)
				return;

			float targetYaw = Mathf.Atan2(-direction.x, -direction.z);
			float currentYaw = Rotation.y;
			float angleDifference = Mathf.PosMod(
				targetYaw - currentYaw + Mathf.Pi,
				Mathf.Pi * 2.0f) - Mathf.Pi;
			float maximumStep = Mathf.Deg2Rad(PlayerFacingSpeedDegreesPerSecond) * delta;
			float nextYaw = currentYaw + Mathf.Clamp(angleDifference, -maximumStep, maximumStep);
			Rotation = new Vector3(0.0f, nextYaw, 0.0f);
		}

		private void BuildPhysicalBody()
		{
			float side = Mathf.Max(SideLength, 0.05f);
			if (GetNodeOrNull("CollisionShape") == null)
			{
				AddChild(new CollisionShape
				{
					Name = "CollisionShape",
					Shape = new BoxShape { Extents = Vector3.One * side * 0.5f }
				});
			}

			float visualScale = Mathf.Max(UniformVisualScale, 0.001f);
			if (!UseDefaultVisual)
			{
				ScaleCustomVisualChildren(visualScale);
				return;
			}
			var material = new SpatialMaterial
			{
				AlbedoColor = new Color(0.05f, 0.25f, 1.0f, 1.0f),
				Roughness = 0.72f
			};
			AddChild(new MeshInstance
			{
				Name = "Mesh",
				Mesh = new CubeMesh { Size = Vector3.One * side },
				MaterialOverride = material,
				Scale = Vector3.One * visualScale
			});
		}

		private void ScaleCustomVisualChildren(float scale)
		{
			foreach (Node child in GetChildren())
			{
				if (child is CollisionShape || child is TriggerField)
					continue;
				Spatial visual = child as Spatial;
				if (visual != null)
				{
					visual.Scale *= scale;
					continue;
				}
				ScaleFirstSpatialDescendants(child, scale);
			}
		}

		private static void ScaleFirstSpatialDescendants(Node parent, float scale)
		{
			foreach (Node child in parent.GetChildren())
			{
				Spatial visual = child as Spatial;
				if (visual != null)
					visual.Scale *= scale;
				else
					ScaleFirstSpatialDescendants(child, scale);
			}
		}

		private void BuildTriggerField()
		{
			var trigger = new TriggerField
			{
				Name = "ActivationTrigger",
				EventName = TriggerEventName,
				ActivateTarget = this
			};
			trigger.AddChild(new CollisionShape
			{
				Name = "CollisionShape",
				Shape = new SphereShape { Radius = Mathf.Max(TriggerRadius, SideLength) }
			});
			AddChild(trigger);
		}
	}
}
