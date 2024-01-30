using System.Windows.Controls;

namespace Paster.UserController
{
    /// <summary>
    /// Interaction logic for ALabel.xaml
    /// </summary>
    public partial class ALabel : UserControl
    {
        public ALabel(string Text)
        {
            InitializeComponent();
            Title.Content = Text;
        }
    }
}