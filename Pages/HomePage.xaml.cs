using System.Windows.Controls;

namespace Speakly.Pages
{
    public partial class HomePage : UserControl
    {
        public HomePage()
        {
            InitializeComponent();
            DataContext = App.ViewModel;
        }
    }
}
