using Godot;

namespace PereSkyroom
{
	public class MainMenu : Control
	{
		[Export] public NodePath TitleLabelPath = new NodePath("Center/VBox/TitleLabel");
		[Export] public NodePath StartButtonPath = new NodePath("Center/VBox/StartButton");
		[Export] public NodePath LanguageButtonPath = new NodePath("Center/VBox/LanguageButton");
		[Export] public string GameScenePath = "res://Main.tscn";

		[Export] public string EnglishTitle = "PEREPETIA S REMNYAMI";
		[Export] public string RussianTitle = "ПЕРЕПЕТИЯ С РЕМНЯМИ";
		[Export] public string EnglishStartText = "START GAME";
		[Export] public string RussianStartText = "НАЧАТЬ ИГРУ";
		[Export] public string EnglishLanguageText = "LANGUAGE: ENGLISH";
		[Export] public string RussianLanguageText = "ЯЗЫК: РУССКИЙ";

		private Label _titleLabel;
		private Button _startButton;
		private Button _languageButton;

		public override void _Ready()
		{
			_titleLabel = GetNode<Label>(TitleLabelPath);
			_startButton = GetNode<Button>(StartButtonPath);
			_languageButton = GetNode<Button>(LanguageButtonPath);
			Input.MouseMode = Input.MouseModeEnum.Visible;
			GlobalSettings.EnsureLanguageSelected();
			UpdateTexts();
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
			UpdateTexts();
		}

		private void UpdateTexts()
		{
			bool russian = GlobalSettings.IsRussianLanguage;
			_titleLabel.Text = russian ? RussianTitle : EnglishTitle;
			_startButton.Text = russian ? RussianStartText : EnglishStartText;
			_languageButton.Text = russian ? RussianLanguageText : EnglishLanguageText;
		}
	}
}
