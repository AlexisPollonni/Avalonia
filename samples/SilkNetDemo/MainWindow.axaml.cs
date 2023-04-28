using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace SilkNetDemo
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            Unloaded += delegate
            {
                var vulkanDemo = this.Get<VulkanDemo>("VulkanDemo1");
                vulkanDemo.Dispose();
            };

#if DEBUG
            this.AttachDevTools();
            Renderer.DrawFps = true;
#endif
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
