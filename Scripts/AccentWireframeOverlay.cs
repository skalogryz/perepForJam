using Godot;
using System.Collections.Generic;

namespace PereSkyroom
{
	public class AccentWireframeOverlay : CanvasLayer
	{
		private static readonly int[,] BoxEdges =
		{
			{ 0, 1 }, { 1, 3 }, { 3, 2 }, { 2, 0 },
			{ 4, 5 }, { 5, 7 }, { 7, 6 }, { 6, 4 },
			{ 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 }
		};

		public List<ShootTarget> Targets;
		public PlayerController Player;
		public SideCameraMode SideCameraMode;
		public PanoramicFovMode PanoramicFovMode;
		public BlueNoiseDither Dither;
		public float LineWidthPixels = 3.0f;
		public Color AccentColor = new Color(0.0f, 1.0f, 0.0f, 1.0f);
		public int DrawPassCount { get; private set; }

		private WireframeCanvas _canvas;

		public override void _Ready()
		{
			Layer = 1100;
			_canvas = new WireframeCanvas
			{
				Name = "TargetWireframes",
				Overlay = this,
				AnchorRight = 1.0f,
				AnchorBottom = 1.0f,
				MouseFilter = Control.MouseFilterEnum.Ignore,
				Visible = false
			};
			AddChild(_canvas);
		}

		public override void _Process(float delta)
		{
			bool shouldDraw = Dither != null && IsInstanceValid(Dither) && Dither.Enabled;
			_canvas.Visible = shouldDraw;
			if (shouldDraw)
				_canvas.Update();
		}

		private void DrawWireframes()
		{
			Vector2 screenSize = GetViewport().Size;
			if (PanoramicFovMode != null && PanoramicFovMode.IsRenderingActive())
			{
				for (int i = 0; i < 3; i++)
				{
					float left = screenSize.x * i / 3.0f;
					float right = screenSize.x * (i + 1) / 3.0f;
					DrawForCamera(PanoramicFovMode.GetCamera(i), new Rect2(left, 0, right - left, screenSize.y));
				}
				DrawPassCount++;
				return;
			}

			if (SideCameraMode != null && SideCameraMode.IsEnabled())
			{
				float halfWidth = screenSize.x * 0.5f;
				DrawForCamera(SideCameraMode.GetCamera(0), new Rect2(0, 0, halfWidth, screenSize.y));
				DrawForCamera(SideCameraMode.GetCamera(1), new Rect2(halfWidth, 0, screenSize.x - halfWidth, screenSize.y));
				DrawPassCount++;
				return;
			}

			if (Player != null && IsInstanceValid(Player))
				DrawForCamera(Player.GetViewCamera(), new Rect2(Vector2.Zero, screenSize));
			DrawPassCount++;
		}

		private void DrawForCamera(Camera camera, Rect2 screenRect)
		{
			if (camera == null || !IsInstanceValid(camera) || Targets == null)
				return;

			Vector2 cameraViewportSize = camera.GetViewport().Size;
			if (cameraViewportSize.x <= 0.0f || cameraViewportSize.y <= 0.0f)
				return;

			var rayExclude = new Godot.Collections.Array { Player };
			foreach (ShootTarget target in Targets)
			{
				if (target == null || !IsInstanceValid(target) || target.IsQueuedForDeletion())
					continue;
				DrawTarget(camera, cameraViewportSize, screenRect, target, rayExclude);
			}
		}

		private void DrawTarget(
			Camera camera,
			Vector2 cameraViewportSize,
			Rect2 screenRect,
			ShootTarget target,
			Godot.Collections.Array rayExclude)
		{
			Vector3 extents = target.AccentHalfExtents;
			Vector3[] worldCorners = new Vector3[8];
			for (int i = 0; i < 8; i++)
			{
				Vector3 localCorner = new Vector3(
					(i & 1) == 0 ? -extents.x : extents.x,
					(i & 2) == 0 ? -extents.y : extents.y,
					(i & 4) == 0 ? -extents.z : extents.z);
				worldCorners[i] = target.GlobalTransform.Xform(localCorner);
			}

			for (int edge = 0; edge < BoxEdges.GetLength(0); edge++)
			{
				int first = BoxEdges[edge, 0];
				int second = BoxEdges[edge, 1];
				DrawVisibleEdge(
					camera,
					cameraViewportSize,
					screenRect,
					worldCorners[first],
					worldCorners[second],
					rayExclude);
			}
		}

		private void DrawVisibleEdge(
			Camera camera,
			Vector2 cameraViewportSize,
			Rect2 screenRect,
			Vector3 worldFrom,
			Vector3 worldTo,
			Godot.Collections.Array rayExclude)
		{
			if (camera.IsPositionBehind(worldFrom) || camera.IsPositionBehind(worldTo))
				return;

			Vector2 screenFrom = ProjectToScreen(camera, cameraViewportSize, screenRect, worldFrom);
			Vector2 screenTo = ProjectToScreen(camera, cameraViewportSize, screenRect, worldTo);
			int segmentCount = Mathf.Clamp((int)Mathf.Ceil(screenFrom.DistanceTo(screenTo) / 6.0f), 1, 128);
			float previousT = 0.0f;
			Vector3 previousWorld = worldFrom;
			Vector2 previousScreen = screenFrom;
			bool previousVisible = IsWorldPointVisible(camera, previousWorld, rayExclude);

			for (int segment = 1; segment <= segmentCount; segment++)
			{
				float currentT = segment / (float)segmentCount;
				Vector3 currentWorld = worldFrom.LinearInterpolate(worldTo, currentT);
				Vector2 currentScreen = ProjectToScreen(camera, cameraViewportSize, screenRect, currentWorld);
				bool currentVisible = IsWorldPointVisible(camera, currentWorld, rayExclude);

				if (previousVisible && currentVisible)
				{
					DrawClippedLine(screenRect, previousScreen, currentScreen);
				}
				else if (previousVisible != currentVisible)
				{
					float boundaryT = FindVisibilityBoundary(
						camera, worldFrom, worldTo, previousT, currentT, previousVisible, rayExclude);
					Vector3 boundaryWorld = worldFrom.LinearInterpolate(worldTo, boundaryT);
					Vector2 boundaryScreen = ProjectToScreen(camera, cameraViewportSize, screenRect, boundaryWorld);
					if (previousVisible)
						DrawClippedLine(screenRect, previousScreen, boundaryScreen);
					else
						DrawClippedLine(screenRect, boundaryScreen, currentScreen);
				}

				previousT = currentT;
				previousWorld = currentWorld;
				previousScreen = currentScreen;
				previousVisible = currentVisible;
			}
		}

		private float FindVisibilityBoundary(
			Camera camera,
			Vector3 edgeFrom,
			Vector3 edgeTo,
			float low,
			float high,
			bool lowVisible,
			Godot.Collections.Array rayExclude)
		{
			for (int iteration = 0; iteration < 5; iteration++)
			{
				float middle = (low + high) * 0.5f;
				bool middleVisible = IsWorldPointVisible(
					camera, edgeFrom.LinearInterpolate(edgeTo, middle), rayExclude);
				if (middleVisible == lowVisible)
					low = middle;
				else
					high = middle;
			}
			return (low + high) * 0.5f;
		}

		private bool IsWorldPointVisible(
			Camera camera,
			Vector3 worldPoint,
			Godot.Collections.Array rayExclude)
		{
			Vector3 cameraPosition = camera.GlobalTransform.origin;
			Vector3 cameraToPoint = worldPoint - cameraPosition;
			float pointDistance = cameraToPoint.Length();
			if (pointDistance <= 0.001f)
				return true;

			Vector3 rayEnd = worldPoint + cameraToPoint / pointDistance * 0.12f;
			Godot.Collections.Dictionary hit = camera.GetWorld().DirectSpaceState.IntersectRay(
				cameraPosition, rayEnd, rayExclude);
			if (hit.Count == 0)
				return true;

			Vector3 hitPosition = (Vector3)hit["position"];
			float tolerance = 0.08f + pointDistance * 0.001f;
			return hitPosition.DistanceTo(worldPoint) <= tolerance;
		}

		private Vector2 ProjectToScreen(
			Camera camera,
			Vector2 cameraViewportSize,
			Rect2 screenRect,
			Vector3 worldPoint)
		{
			Vector2 point = camera.UnprojectPosition(worldPoint);
			return screenRect.Position + new Vector2(
				point.x * screenRect.Size.x / cameraViewportSize.x,
				point.y * screenRect.Size.y / cameraViewportSize.y);
		}

		private void DrawClippedLine(Rect2 screenRect, Vector2 from, Vector2 to)
		{
			if (ClipLine(screenRect, ref from, ref to))
				_canvas.DrawLine(from, to, AccentColor, Mathf.Max(1.0f, LineWidthPixels), false);
		}

		private bool ClipLine(Rect2 rectangle, ref Vector2 from, ref Vector2 to)
		{
			int fromCode = RegionCode(rectangle, from);
			int toCode = RegionCode(rectangle, to);
			while (true)
			{
				if ((fromCode | toCode) == 0)
					return true;
				if ((fromCode & toCode) != 0)
					return false;

				int outsideCode = fromCode != 0 ? fromCode : toCode;
				Vector2 point = Vector2.Zero;
				float left = rectangle.Position.x;
				float top = rectangle.Position.y;
				float right = rectangle.End.x;
				float bottom = rectangle.End.y;
				if ((outsideCode & 8) != 0)
					point = new Vector2(from.x + (to.x - from.x) * (top - from.y) / (to.y - from.y), top);
				else if ((outsideCode & 4) != 0)
					point = new Vector2(from.x + (to.x - from.x) * (bottom - from.y) / (to.y - from.y), bottom);
				else if ((outsideCode & 2) != 0)
					point = new Vector2(right, from.y + (to.y - from.y) * (right - from.x) / (to.x - from.x));
				else if ((outsideCode & 1) != 0)
					point = new Vector2(left, from.y + (to.y - from.y) * (left - from.x) / (to.x - from.x));

				if (outsideCode == fromCode)
				{
					from = point;
					fromCode = RegionCode(rectangle, from);
				}
				else
				{
					to = point;
					toCode = RegionCode(rectangle, to);
				}
			}
		}

		private int RegionCode(Rect2 rectangle, Vector2 point)
		{
			int code = 0;
			if (point.x < rectangle.Position.x) code |= 1;
			else if (point.x > rectangle.End.x) code |= 2;
			if (point.y > rectangle.End.y) code |= 4;
			else if (point.y < rectangle.Position.y) code |= 8;
			return code;
		}

		private class WireframeCanvas : Control
		{
			public AccentWireframeOverlay Overlay;

			public override void _Draw()
			{
				if (Overlay != null)
					Overlay.DrawWireframes();
			}
		}
	}
}
