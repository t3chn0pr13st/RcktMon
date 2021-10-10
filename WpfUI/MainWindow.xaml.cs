using Newtonsoft.Json;

using RcktMon.Models;

using System.IO;

namespace RcktMon
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        static MainWindow()
        {

        }

        public MainWindow()
        {
            InitializeComponent();
            LoadWindowPos();
        }

        public WindowSettings CurrentWindowSettings { get; set; }

        private void SaveWindowPos()
        {
            if (CurrentWindowSettings == null)
                CurrentWindowSettings = new WindowSettings();

            if (WindowState == System.Windows.WindowState.Normal)
            {
                CurrentWindowSettings.Left = Left;
                CurrentWindowSettings.Top = Top;
                CurrentWindowSettings.Width = Width;
                CurrentWindowSettings.Height = Height;
            }
            CurrentWindowSettings.State = WindowState;
            try
            {
                var text = JsonConvert.SerializeObject(CurrentWindowSettings);
                File.WriteAllText("WindowPos.json", text);
            } 
            catch
            {

            }
        }

        public void LoadWindowPos()
        {
            try
            {
                if (File.Exists("WindowPos.json"))
                {
                    CurrentWindowSettings = JsonConvert.DeserializeObject<WindowSettings>(File.ReadAllText("WindowPos.json"));
                }
            } 
            catch
            {

            }
            if (CurrentWindowSettings != null && CurrentWindowSettings.CheckVisible())
            {
                Left = CurrentWindowSettings.Left;
                Top = CurrentWindowSettings.Top;
                Width = CurrentWindowSettings.Width;
                Height = CurrentWindowSettings.Height;
                WindowState = CurrentWindowSettings.State;
            }

            SizeChanged += (s, e) => SaveWindowPos();
            LocationChanged += (s, e) => SaveWindowPos();
            StateChanged += (s, e) => SaveWindowPos();
        }
    }
}
