using Godot;
using System.Collections.Generic;

namespace PereSkyroom
{
	public class ControlTranslationGroup : Node
	{
		[Export] public Godot.Collections.Array TrackedControlPaths = new Godot.Collections.Array();

		private sealed class TrackedText
		{
			public Control Control;
			public string SourceText;
		}

		private readonly List<TrackedText> _trackedTexts = new List<TrackedText>();

		public override void _Ready()
		{
			CaptureInitialTexts();
			ApplyTranslations();
		}

		public override void _Notification(int what)
		{
			if (what == NotificationTranslationChanged && _trackedTexts.Count > 0)
				ApplyTranslations();
		}

		private void CaptureInitialTexts()
		{
			_trackedTexts.Clear();
			foreach (object pathValue in TrackedControlPaths)
			{
				NodePath path = pathValue as NodePath;
				if (path == null || path.IsEmpty())
					continue;

				Control control = GetNodeOrNull(path) as Control;
				if (control is Label label)
				{
					_trackedTexts.Add(new TrackedText
					{
						Control = label,
						SourceText = label.Text
					});
				}
				else if (control is Button button)
				{
					_trackedTexts.Add(new TrackedText
					{
						Control = button,
						SourceText = button.Text
					});
				}
				else
					GD.PushWarning("ControlTranslationGroup supports Label and Button nodes: " + path);
			}
		}

		private void ApplyTranslations()
		{
			GD.Print($"ApplyTranslations: {TranslationServer.GetLocale()}");
			foreach (TrackedText tracked in _trackedTexts)
			{
				if (tracked.Control == null || !IsInstanceValid(tracked.Control))
					continue;

				string translated = Tr(tracked.SourceText);
				GD.Print($"{tracked.SourceText} -> {translated}");
				if (tracked.Control is Label label)
					label.Text = translated;
				else if (tracked.Control is Button button)
					button.Text = translated;
			}
		}
	}
}
