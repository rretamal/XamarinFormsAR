using Plugin.Permissions;
using Plugin.Permissions.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Forms;

namespace XamarinFormsAR
{
	public partial class MainPage : ContentPage
	{
		public MainPage()
		{
			InitializeComponent();

            btnShowExample.Clicked += BtnShowExample_Clicked;
        }

        protected async override void OnAppearing()
        {
            base.OnAppearing();
        }

        private async void BtnShowExample_Clicked(object sender, EventArgs e)
        {
            var status = await CrossPermissions.Current.CheckPermissionStatusAsync(Permission.Camera);
            if (status != PermissionStatus.Granted)
            {
                if (await CrossPermissions.Current.ShouldShowRequestPermissionRationaleAsync(Permission.Camera))
                {
                    Device.BeginInvokeOnMainThread(async () =>
                    {
                        await DisplayAlert("Need location", "Gunna need that location", "OK");
                    });
                }

                var results = await CrossPermissions.Current.RequestPermissionsAsync(Permission.Camera);

                //Best practice to always check that the key exists
                if (results.ContainsKey(Permission.Camera))
                    status = results[Permission.Camera];
            }

            App.Current.MainPage = new ARPage();
        }
    }
}
