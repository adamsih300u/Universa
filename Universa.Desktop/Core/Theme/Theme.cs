using System.Windows.Media;

namespace Universa.Desktop.Core.Theme
{
    public class Theme
    {
        public string Name { get; set; }
        public Color WindowBackground { get; set; }
        public Color MenuBackground { get; set; }
        public Color MenuForeground { get; set; }
        public Color TabBackground { get; set; }
        public Color TabForeground { get; set; }
        public Color ActiveTabBackground { get; set; }
        public Color ActiveTabForeground { get; set; }
        public Color ContentBackground { get; set; }
        public Color ContentForeground { get; set; }
        public Color AccentColor { get; set; }
        public Color BorderColor { get; set; }

        public Theme()
        {
            // Default constructor
        }

        public Theme(string name)
        {
            Name = name;
            InitializeDefaultColors();
        }

        private void InitializeDefaultColors()
        {
            if (Name.Equals("Dark", System.StringComparison.OrdinalIgnoreCase))
            {
                WindowBackground = Color.FromRgb(32, 32, 32);
                MenuBackground = Color.FromRgb(45, 45, 45);
                MenuForeground = Color.FromRgb(255, 255, 255);
                TabBackground = Color.FromRgb(45, 45, 45);
                TabForeground = Color.FromRgb(200, 200, 200);
                ActiveTabBackground = Color.FromRgb(60, 60, 60);
                ActiveTabForeground = Color.FromRgb(255, 255, 255);
                ContentBackground = Color.FromRgb(32, 32, 32);
                ContentForeground = Color.FromRgb(255, 255, 255);
                AccentColor = Color.FromRgb(76, 175, 80);
                BorderColor = Color.FromRgb(63, 63, 63);
            }
            else // Light theme
            {
                WindowBackground = Color.FromRgb(255, 255, 255);
                MenuBackground = Color.FromRgb(245, 245, 245);
                MenuForeground = Color.FromRgb(0, 0, 0);
                TabBackground = Color.FromRgb(240, 240, 240);
                TabForeground = Color.FromRgb(0, 0, 0);
                ActiveTabBackground = Color.FromRgb(255, 255, 255);
                ActiveTabForeground = Color.FromRgb(0, 0, 0);
                ContentBackground = Color.FromRgb(255, 255, 255);
                ContentForeground = Color.FromRgb(0, 0, 0);
                AccentColor = Color.FromRgb(0, 120, 215);
                BorderColor = Color.FromRgb(200, 200, 200);
            }
        }
    }
} 