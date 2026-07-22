namespace Mendix_Tools.Services;

/// <summary>
/// Production <see cref="IFolderPicker"/>. On Windows it uses the WinUI
/// <c>Windows.Storage.Pickers.FolderPicker</c> initialised against the app window handle; on
/// other platforms it returns <c>null</c> (the path field stays directly editable). Isolated
/// so the platform-specific interop stays out of the settings logic.
/// </summary>
public sealed class MauiFolderPicker : IFolderPicker
{
    public async Task<string?> PickFolderAsync()
    {
#if WINDOWS
        var picker = new Windows.Storage.Pickers.FolderPicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add("*");

        // The WinUI picker must be associated with the app window handle or it throws.
        var mauiWindow = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault();
        var platformWindow = mauiWindow?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
        if (platformWindow is null)
        {
            return null;
        }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(platformWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
#else
        await Task.CompletedTask;
        return null;
#endif
    }
}
