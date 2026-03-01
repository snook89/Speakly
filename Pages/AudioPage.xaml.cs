using System.Windows.Controls;

namespace Speakly.Pages
{
    public partial class AudioPage : UserControl
    {
        public AudioPage()
        {
            InitializeComponent();
            DataContext = App.ViewModel;
        }
    }
}
