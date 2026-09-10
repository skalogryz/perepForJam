using System;
using System.Collections.Generic;
using Godot;


namespace PereSkyroom
{
	public class GlobalSettings : Node
	{
		[Signal]
		public delegate void TriggerEvent(string eventName, Node player, Node triggerField);
		[Signal]
		public delegate void DialogCloseRequested();

		public static GlobalSettings inst = null;
		public static string CurrentLanguageCode { get; private set; } = string.Empty;
		public static bool IsRussianLanguage
		{
			get
			{
				EnsureLanguageSelected();
				return CurrentLanguageCode == "ru";
			}
		}

		[Export]
		public List<DialogDescr> Dialogs = new List<DialogDescr>();

		private readonly List<TriggerField> _registeredTriggerFields = new List<TriggerField>();

		public override void _Ready()
		{
			GD.Print("starting the global settings");
			if (inst == null)
				inst = this;
			EnsureLanguageSelected();
		}

		public static void EnsureLanguageSelected()
		{
			if (!string.IsNullOrEmpty(CurrentLanguageCode))
				return;
			string systemLocale = OS.GetLocale() ?? string.Empty;
			SetLanguage(systemLocale.StartsWith("ru", StringComparison.OrdinalIgnoreCase) ? "ru" : "en");
		}

		public static void SetLanguage(string languageCode)
		{
			CurrentLanguageCode = string.Equals(
				languageCode, "ru", StringComparison.OrdinalIgnoreCase) ? "ru" : "en";
			TranslationServer.SetLocale(CurrentLanguageCode);
		}

		public void PublishTriggerEvent(string eventName, PlayerController player, TriggerField triggerField)
		{
			EmitSignal(nameof(TriggerEvent), eventName, player, triggerField);
		}

		public static void RegisterTriggerField(TriggerField triggerField)
		{
			if (inst == null || !IsInstanceValid(inst) || triggerField == null)
				return;
			if (!inst._registeredTriggerFields.Contains(triggerField))
				inst._registeredTriggerFields.Add(triggerField);
		}

		public static void UnregisterTriggerField(TriggerField triggerField)
		{
			if (inst == null || !IsInstanceValid(inst) || triggerField == null)
				return;
			inst._registeredTriggerFields.Remove(triggerField);
		}

		public static void ActivateTarget(Spatial target, PlayerController player)
		{
			if (inst == null || !IsInstanceValid(inst) || target == null || player == null)
				return;

			var fields = new List<TriggerField>(inst._registeredTriggerFields);
			foreach (TriggerField field in fields)
			{
				if (field == null || !IsInstanceValid(field))
				{
					inst._registeredTriggerFields.Remove(field);
					continue;
				}

				if (field.ActivateTarget == target && field.ContainsPlayer(player))
					field.Activate(player);
			}
		}

		public static void DoTriggerEvent(string eventName, PlayerController player, TriggerField triggerField)
		{
			if (inst == null) return;
			if (!IsInstanceValid(GlobalSettings.inst)) return;
			inst.PublishTriggerEvent(eventName, player, triggerField);
		}

		public static void DialogClosing()
		{
			if (inst == null || !IsInstanceValid(inst))
				return;
			inst.EmitSignal(nameof(DialogCloseRequested));
		}
	}
}
