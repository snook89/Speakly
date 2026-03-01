using System.Windows.Controls;

namespace Speakly.Pages
{
    public partial class RefinementPage : UserControl
    {
        public RefinementPage()
        {
            InitializeComponent();
            DataContext = App.ViewModel;
        }
    }
}
