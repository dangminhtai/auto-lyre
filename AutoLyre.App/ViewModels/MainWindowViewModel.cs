using JetBrains.Annotations;
using Stylet;
using StyletIoC;
using Wpf.Ui.Mvvm.Contracts;

namespace AutoLyre.App.ViewModels;

[UsedImplicitly]
public class MainWindowViewModel : Conductor<IScreen>
{
    public MainWindowViewModel(IContainer ioc, IThemeService theme)
    {
        Title = "Auto Lyre";
        PlaylistView = new(ioc, this) { Loop = PlaylistViewModel.LoopMode.Once };
        SettingsView = new(ioc, this);
        // Keep legacy models internal; the window exposes only the player.
        PianoSheetView = new(this);
        ActiveItem = PlayerView = new(ioc, this);
    }

    public LyrePlayerViewModel PlayerView { get; }
    public PianoSheetViewModel PianoSheetView { get; }
    public PlaylistViewModel PlaylistView { get; }
    public SettingsPageViewModel SettingsView { get; }
    public string Title { get; set; }

    protected override async void OnViewLoaded()
    {
        SettingsView.OnThemeChanged();
        // Resolve a known installation quietly; compact settings handles overrides.
        await SettingsView.TryGetLocationAsync();
    }
}
