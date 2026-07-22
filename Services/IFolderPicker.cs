namespace Mendix_Tools.Services;

/// <summary>
/// MT-11 data-directory picker seam. The Database tab's "Browse…" button calls
/// <see cref="PickFolderAsync"/> to let the user choose where restored .backup files and
/// dumps land. The path is otherwise directly editable (the user is technical) — the picker
/// is a convenience. Behind a seam because folder picking is platform-specific; the Windows
/// implementation is <see cref="MauiFolderPicker"/>.
/// </summary>
public interface IFolderPicker
{
    /// <summary>Shows the OS folder chooser and returns the chosen path, or <c>null</c> when the
    /// user cancels or picking is unavailable on the platform.</summary>
    Task<string?> PickFolderAsync();
}
