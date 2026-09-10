using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;

namespace PereSkyroom
{
    public class DialogDescr : Resource
    {
        [Export]
        public string Name;
        
        [Export(PropertyHint.File, "*.tscn")]
        public string UIScene;
    }
}
