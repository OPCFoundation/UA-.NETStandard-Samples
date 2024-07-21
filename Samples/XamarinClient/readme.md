# UA Xamarin Sample Client
This sample runs on the following platform: Windows (UWP), Android, iOS.

The server has implemented these functionalities:
- Connect to an OPC UA server (secure or unsecure).
- Browse nodes.
- Read variable nodes. 

## How to build and run the sample in Visual Studio on Windows

### Prerequisites:
Install Windows 10 Fall Creators Update.

Install latest Visual Studio 2017 (min version 15.5.5).

Add [Xamarin](https://developer.xamarin.com/guides/cross-platform/getting_started/installation/windows/#vs2017) to Visual Studio.

### UWP:
1. Select UA Xamarin Client.UWP as startup project.
2. Hit `F5` to build and execute the sample.

### Android:
1. Install [install VisualStudio Android emulator](https://www.visualstudio.com/vs/msft-android-emulator/).
2. Add the UA Xamarin Client.Android build to the debug and release [configuration](https://learn.microsoft.com/en-us/visualstudio/ide/how-to-create-and-edit-configurations?view=vs-2022).
3. Select UA Xamarin Client.Android as startup project.
4. Hit `F5` to build and execute the sample.

Tested with Android Marshmallow (6.0.0 API 23) or higher. 

### iOS:
1. To build the iOS app you need a Mac. 
2. This [link](https://developer.xamarin.com/guides/ios/getting_started/installation/windows/introduction_to_xamarin_ios_for_visual_studio/) shows you how to build and test Xamarin iOS applications using Visual Studio.

Tested with Xcode 9.2 iOS 8 or higher