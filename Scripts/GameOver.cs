using Godot;

namespace PereSkyroom
{
	public class GameOver : Control
	{
		[Export] public string MainMenuScenePath = "res://MainMenu.tscn";

		private bool _returningToMenu;

		public override void _Ready()
		{
			Input.MouseMode = Input.MouseModeEnum.Visible;
		}

		public override void _Input(InputEvent inputEvent)
		{
			if (_returningToMenu || !IsPressedButton(inputEvent))
				return;

			_returningToMenu = true;
			GetTree().SetInputAsHandled();
			GetTree().Paused = false;
			Error result = GetTree().ChangeScene(MainMenuScenePath);
			if (result != Error.Ok)
			{
				_returningToMenu = false;
				GD.PushError("Unable to return to main menu: "
					+ MainMenuScenePath + " (" + result + ")");
			}
		}

		private static bool IsPressedButton(InputEvent inputEvent)
		{
			var key = inputEvent as InputEventKey;
			if (key != null)
				return key.Pressed && !key.Echo;

			var mouseButton = inputEvent as InputEventMouseButton;
			if (mouseButton != null)
				return mouseButton.Pressed;

			var joypadButton = inputEvent as InputEventJoypadButton;
			return joypadButton != null && joypadButton.Pressed;
		}
	}
}
