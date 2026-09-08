using System;
using System.Collections.Generic;
using Godot;


namespace PereSkyroom
{
	public class GlobalSettings : Node
	{
		[Signal]
		public delegate void TriggerEvent(string eventName, Node player, Node triggerField);

		public static GlobalSettings inst = null;

		[Export]
		public List<DialogDescr> Dialogs = new List<DialogDescr>();

		public override void _Ready()
		{
			GD.Print("starting the global settings");
			if (inst == null)
				inst = this;
		}

		public void PublishTriggerEvent(string eventName, PlayerController player, TriggerField triggerField)
		{
			EmitSignal(nameof(TriggerEvent), eventName, player, triggerField);
		}

		public static void DoTriggerEvent(string eventName, PlayerController player, TriggerField triggerField)
        {
			if (inst == null) return;
			if (!IsInstanceValid(GlobalSettings.inst)) return;
			inst.PublishTriggerEvent(eventName, player, triggerField);
		}
	}
}
