using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PC_GAMES_Local
{
    public class PC_GAMES_LocalSettings : ObservableObject
    {
        private readonly PC_GAMES_Local plugin;

        public PC_GAMES_LocalSettings(PC_GAMES_Local plugin)
        {
            this.plugin = plugin;
        }

        // Implement your settings properties and methods
    }
}