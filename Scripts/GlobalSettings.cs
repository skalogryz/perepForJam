using System;
using System.Collections.Generic;
using Godot;


namespace PereSkyroom
{
	public class GlobalSettings : Node
	{
		public static GlobalSettings inst = null;

		[Export]
		public List<DialogDescr> Dialogs = new List<DialogDescr>();

		public override void _Ready()
		{
			GD.Print("starting the global settings");
			if (inst == null)
				inst = this;
		}
	}
}
