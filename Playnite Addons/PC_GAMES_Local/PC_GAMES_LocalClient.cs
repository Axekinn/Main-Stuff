using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PC_GAMES_Local
{
    public class PC_GAMES_LocalClient : LibraryClient
    {
        public override bool IsInstalled => true;

        public override void Open()
        {
            // Implement your method to open the library client
        }
    }
}