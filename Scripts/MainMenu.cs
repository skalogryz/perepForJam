using Godot;

namespace PereSkyroom
{
	public class MainMenu : Control
	{
		[Export] public NodePath StartButtonPath = new NodePath("Center/VBox/StartButton");
		[Export] public string GameScenePath = "res://Main.tscn";

		private Button _startButton;

		public override void _Ready()
		{
			_startButton = GetNode<Button>(StartButtonPath);
			Input.MouseMode = Input.MouseModeEnum.Visible;
			GlobalSettings.EnsureLanguageSelected();
			_startButton.GrabFocus();
		}

		public void OnStartButtonPressed()
		{
			Error result = GetTree().ChangeScene(GameScenePath);
			if (result != Error.Ok)
				GD.PushError("Unable to start game scene: " + GameScenePath + " (" + result + ")");
		}

		public void OnLanguageButtonPressed()
		{
			GlobalSettings.SetLanguage(GlobalSettings.IsRussianLanguage ? "en" : "ru");
		}
	}
}
