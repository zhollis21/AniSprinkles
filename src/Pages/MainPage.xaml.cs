using AniSprinkles.Models;
using AniSprinkles.PageModels;

namespace AniSprinkles.Pages
{
    public partial class MainPage : ContentPage
    {
        public MainPage(MainPageModel model)
        {
            InitializeComponent();
            BindingContext = model;
        }
    }
}