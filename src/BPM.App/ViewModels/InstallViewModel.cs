using System.Collections.ObjectModel;
using BPM.Core.Actions;
using BPM.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BPM.App.ViewModels;

/// <summary>The "Install an extension" window: what to install (store link, folder or .crx file) and which profiles get it.</summary>
public sealed partial class InstallViewModel : ObservableObject
{
    readonly ScanResult scan;
    bool settingSource;

    public InstallViewModel(ScanResult scan, InstallSource? source, ProfileInfo? preselect)
    {
        this.scan = scan;
        foreach (var profile in scan.AllProfiles)
            Targets.Add(new InstallTargetRow(profile, OnTargetChanged));
        if (source != null) Use(source, source.Kind == InstallSourceKind.Store ? source.StoreId! : "");
        if (preselect != null && Targets.FirstOrDefault(t => t.Profile == preselect) is { CanInstall: true } row) row.IsChecked = true;
    }

    public ObservableCollection<InstallTargetRow> Targets { get; } = [];

    [ObservableProperty] private string sourceText = "";
    [ObservableProperty] private InstallSource? source;
    [ObservableProperty] private string sourceStatus = "Paste a Chrome Web Store link or extension ID, or choose an extension folder or .crx file.";
    [ObservableProperty] private bool sourceIsError;

    public bool IsFolder => Source?.Kind == InstallSourceKind.Folder;
    public IReadOnlyList<ProfileInfo> SelectedProfiles => Targets.Where(t => t.IsChecked && t.CanInstall).Select(t => t.Profile).ToList();
    public bool CanInstall => Source != null && SelectedProfiles.Count > 0;
    public string InstallText => SelectedProfiles.Count is var n && n == 1 ? "Install in 1 profile" : $"Install in {SelectedProfiles.Count} profiles";

    partial void OnSourceTextChanged(string value)
    {
        if (settingSource) return;
        var parsed = ExtensionInstall.ParseStore(value, KnownName);
        if (parsed != null) Use(parsed, value);
        else
        {
            Source = null;
            SourceIsError = value.Trim().Length > 0;
            SourceStatus = SourceIsError
                ? "That isn't a Chrome Web Store link or extension ID. Store links look like chromewebstore.google.com/detail/…"
                : "Paste a Chrome Web Store link or extension ID, or choose an extension folder or .crx file.";
        }
    }

    partial void OnSourceChanged(InstallSource? value)
    {
        foreach (var row in Targets) row.Update(value);
        OnPropertyChanged(nameof(IsFolder));
        OnTargetChanged();
    }

    void Use(InstallSource chosen, string text)
    {
        settingSource = true;
        SourceText = text;
        settingSource = false;
        Source = chosen;
        SourceIsError = false;
        SourceStatus = chosen.Kind == InstallSourceKind.Store && chosen.Name == chosen.StoreId
            ? $"Chrome Web Store extension {chosen.StoreId}. Its name shows when the store page opens."
            : chosen.Description;
    }

    string? KnownName(string id) => scan.AllExtensions.FirstOrDefault(e => e.Id == id)?.Name;

    void OnTargetChanged()
    {
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(InstallText));
    }

    [RelayCommand]
    void ChooseFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose the extension folder (the one with manifest.json)" };
        if (dialog.ShowDialog() != true) return;
        TryUse(() => ExtensionInstall.FromFolder(dialog.FolderName));
    }

    [RelayCommand]
    void ChooseCrx()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Choose a .crx extension file", Filter = "Extension package (*.crx)|*.crx" };
        if (dialog.ShowDialog() != true) return;
        // Browsers on Windows refuse .crx files from outside a store, so it's unpacked and loaded as a folder.
        var unpacked = Path.Combine(Path.GetTempPath(), "BrowserProfileManager-crx", Guid.NewGuid().ToString("N")[..8]);
        TryUse(() => ExtensionInstall.FromFolder(ExtensionInstall.UnpackCrx(dialog.FileName, unpacked)));
    }

    void TryUse(Func<InstallSource> read)
    {
        try
        {
            Use(read(), "");
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Source = null;
            SourceIsError = true;
            SourceStatus = e.Message;
        }
    }

    /// <summary>Ticks every profile it can install into: "chrome" (Chrome's channels), "all", or "none".</summary>
    [RelayCommand]
    void Select(string scope)
    {
        foreach (var row in Targets)
            row.IsChecked = row.CanInstall && scope switch
            {
                "chrome" => row.Profile.Browser.Id.StartsWith("chrome", StringComparison.Ordinal),
                "all" => true,
                _ => false,
            };
    }
}

/// <summary>One profile in the install window, with why it can't take the extension (if it can't).</summary>
public sealed partial class InstallTargetRow(ProfileInfo profile, Action changed) : ObservableObject
{
    public ProfileInfo Profile { get; } = profile;
    public string BrowserName => Profile.Browser.Name;
    public string Name => Profile.Name;
    public string? Email => Profile.Email;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall))]
    private string? reason;

    [ObservableProperty] private bool isChecked;

    public bool CanInstall => Reason == null;

    partial void OnIsCheckedChanged(bool value) => changed();

    public void Update(InstallSource? source)
    {
        Reason = source == null ? null : ExtensionInstall.CheckTarget(Profile, source);
        if (!CanInstall) IsChecked = false;
    }
}
