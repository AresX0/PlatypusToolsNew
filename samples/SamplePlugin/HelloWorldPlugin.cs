using System;
using System.Collections.Generic;
using System.Windows;
using PlatypusTools.UI.Services;

namespace SamplePlugin
{
    /// <summary>
    /// Sample plugin that demonstrates the PlatypusTools plugin system.
    /// This plugin adds a menu item that shows a greeting message.
    /// </summary>
    public class HelloWorldPlugin : PluginBase, IMenuPlugin
    {
        public override string Id => "com.platypustools.sample.helloworld";
        public override string Name => "Hello World Plugin";
        public override string Description => "A sample plugin that demonstrates the plugin system by showing a greeting message.";
        public override string Version => "1.0.0";
        public override string Author => "PlatypusTools Team";

        public override void Initialize()
        {
            // Plugin initialization code goes here
            // This is called when the plugin is first loaded or enabled
            LoggingService.Instance.Info("Hello World Plugin initialized!");
        }

        public override void Shutdown()
        {
            // Plugin cleanup code goes here
            // This is called when the plugin is unloaded or disabled
            LoggingService.Instance.Info("Hello World Plugin shutdown!");
        }

        /// <summary>
        /// Returns menu items to be added to the Plugins menu.
        /// </summary>
        public IEnumerable<PluginMenuItem> GetMenuItems()
        {
            yield return new PluginMenuItem
            {
                Header = "Say Hello",
                Icon = "👋",
                Category = "Sample",
                Command = () => ShowGreeting()
            };
            
            yield return new PluginMenuItem
            {
                Header = "About Sample Plugin",
                Icon = "ℹ️",
                Category = "Sample",
                Command = () => ShowAbout()
            };
        }

        private void ShowGreeting()
        {
            MessageBox.Show(
                "Hello from the Sample Plugin!\n\n" +
                "This demonstrates how plugins can extend PlatypusTools with custom functionality.",
                "Hello World Plugin",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void ShowAbout()
        {
            MessageBox.Show(
                $"Sample Plugin v{Version}\n" +
                $"Author: {Author}\n\n" +
                $"{Description}\n\n" +
                "This plugin runs in a sandboxed context for security.",
                "About Sample Plugin",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}
