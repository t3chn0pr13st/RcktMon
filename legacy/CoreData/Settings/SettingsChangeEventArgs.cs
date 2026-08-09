using CoreData.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CoreData.Settings
{
    public class SettingsChangeEventArgs : EventArgs
    {
        public INgineSettings PrevSettings { get; init; }
        public INgineSettings NewSettings { get; init; }

        public SettingsChangeEventArgs() { }

        public SettingsChangeEventArgs( INgineSettings oldSettings, INgineSettings newSettings )
        {
            PrevSettings = oldSettings;
            NewSettings = newSettings;
        }
    }
}
