using Godot;
using System.Collections.Generic;

namespace PereSkyroom
{
	public class StoneDropManager : Spatial
	{
		public const uint StoneCollisionLayer = 1u << 2;

		public PlayerController Player;
		public float StoneSideLength = 0.25f;
		public float SpawnIntervalSeconds = 3.0f;
		public float EvaluationDelaySeconds = 3.0f;
		public int MaximumStoneCount = 20;
		public float LinearVelocityThreshold = 0.05f;
		public float AngularVelocityThreshold = 0.05f;

		private readonly List<DroppedStone> _stones = new List<DroppedStone>();
		private float _spawnTimer;

		public override void _PhysicsProcess(float delta)
		{
			_spawnTimer += delta;
			if (_spawnTimer >= Mathf.Max(SpawnIntervalSeconds, 0.01f))
			{
				_spawnTimer = 0.0f;
				SpawnStone();
			}

			ulong now = OS.GetTicksMsec();
			for (int i = _stones.Count - 1; i >= 0; i--)
			{
				DroppedStone stone = _stones[i];
				if (!IsInstanceValid(stone))
				{
					_stones.RemoveAt(i);
					continue;
				}

				stone.TrackPosition();
				if (stone.PhysicsDisabled
					|| now - stone.SpawnTimeMilliseconds < EvaluationDelaySeconds * 1000.0f)
					continue;

				DroppedStone previous = stone.PreviousStone;
				if (IsInstanceValid(previous)
					&& stone.TrackedPosition.DistanceTo(previous.TrackedPosition)
						< Mathf.Max(StoneSideLength, 0.01f))
				{
					RemoveStoneAt(i);
					continue;
				}

				if (stone.IsMoving(LinearVelocityThreshold, AngularVelocityThreshold))
					RemoveStoneAt(i);
				else
					stone.DisablePhysicsInteraction();
			}
		}

		public void ClearStones()
		{
			for (int i = 0; i < _stones.Count; i++)
				if (IsInstanceValid(_stones[i]))
					_stones[i].QueueFree();
			_stones.Clear();
			_spawnTimer = 0.0f;
		}

		private void SpawnStone()
		{
			if (Player == null || !IsInstanceValid(Player))
				return;

			RemoveInvalidStones();
			int limit = Mathf.Max(MaximumStoneCount, 1);
			while (_stones.Count >= limit)
				RemoveStoneAt(0);

			float sideLength = Mathf.Max(StoneSideLength, 0.01f);
			DroppedStone previous = _stones.Count == 0 ? null : _stones[_stones.Count - 1];
			var stone = new DroppedStone
			{
				Name = "Stone",
				PreviousStone = previous
			};
			AddChild(stone);
			stone.GlobalTransform = new Transform(Basis.Identity, Player.GlobalTransform.origin);
			stone.Initialize(sideLength, Player);
			_stones.Add(stone);
		}

		private void RemoveInvalidStones()
		{
			for (int i = _stones.Count - 1; i >= 0; i--)
				if (!IsInstanceValid(_stones[i]))
					_stones.RemoveAt(i);
		}

		private void RemoveStoneAt(int index)
		{
			DroppedStone stone = _stones[index];
			_stones.RemoveAt(index);
			if (IsInstanceValid(stone))
				stone.QueueFree();
		}
	}
}
