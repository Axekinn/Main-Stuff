using MyrientNintendo3DS;
using Playnite.SDK;
using Playnite.SDK.Data;

namespace myrient.Nintendo3DS
{
    public class MyrientNintendo3DSSettings
    {
        private readonly MyrientNintendo3DSStore plugin;

        public MyrientNintendo3DSSettings(MyrientNintendo3DSStore plugin)
        {
            this.plugin = plugin;

            // If there are any settings to load, load them here.
            var savedSettings = plugin.LoadPluginSettings<MyrientNintendo3DSSettings>();
            if (savedSettings != null)
            {
                LoadValues(savedSettings);
            }
        }

        // Define your settings properties here if needed.
        // For example:
        public string MyProperty { get; set; } = "Default Value";

        public void LoadValues(MyrientNintendo3DSSettings source)
        {
            // Load values from the source settings object.
            MyProperty = source.MyProperty;
        }
    }
}
