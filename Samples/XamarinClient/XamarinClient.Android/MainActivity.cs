using System;

using Android.App;
using Android.Content.PM;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Android.OS;
using Android;
using Google.Android.Material.Snackbar;

namespace XamarinClient.Droid
{
    [Activity(Label = "XamarinClient", Icon = "@drawable/icon", Theme = "@style/MainTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation)]
    public class MainActivity : global::Xamarin.Forms.Platform.Android.FormsAppCompatActivity
    {
        protected override void OnCreate(Bundle bundle)
        {
            TabLayoutResource = Resource.Layout.Tabbar;
            ToolbarResource = Resource.Layout.Toolbar;

            base.OnCreate(bundle);

            global::Xamarin.Forms.Forms.Init(this, bundle);
            LoadApplication(new App());

            //if (CheckSelfPermission(Manifest.Permission.ReadExternalStorage) == Permission.Denied)
            //{
            //    RequestPermissions(new string[] { Manifest.Permission.WriteExternalStorage }, 5);
            //}
        }

        //public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
        //{
        //    if (requestCode == 5)
        //    {
        //        if (permissions.Length > 0 && permissions[0] == Manifest.Permission.ReadExternalStorage &&
        //            grantResults.Length > 0 && grantResults[0] == Permission.Granted)
        //        {
        //            //Success
        //        }
        //        else
        //        {
        //            Toast.MakeText(this, "Failure to get Read Permission!", ToastLength.Long).Show();
        //        }
        //    }
        //}
    }
}

