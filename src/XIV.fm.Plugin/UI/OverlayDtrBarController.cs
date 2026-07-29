using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;

namespace XIV.fm.Plugin.UI;

/// <summary>
/// Owns the native server-info-bar shortcut for listening-card visibility.
/// </summary>
internal sealed class OverlayDtrBarController : IDisposable
{
    private const string EntryTitle = "XIV.fm overlay";
    private const string MusicSymbol = "♫";
    private const ushort EnabledColor = 43;

    private readonly IDtrBarEntry entry;
    private readonly Func<bool> isOverlayEnabled;
    private readonly Action toggleOverlay;
    private readonly Action openSettings;

    public OverlayDtrBarController(
        IDtrBar dtrBar,
        Func<bool> isOverlayEnabled,
        Action toggleOverlay,
        Action openSettings)
    {
        this.isOverlayEnabled = isOverlayEnabled;
        this.toggleOverlay = toggleOverlay;
        this.openSettings = openSettings;
        this.entry = dtrBar.Get(EntryTitle);
        this.entry.OnClick += this.OnClick;
        this.entry.Shown = true;
        this.Refresh();
    }

    public void Refresh()
    {
        var enabled = this.isOverlayEnabled();
        this.entry.Text = enabled
            ? new SeStringBuilder()
                .AddUiForeground(MusicSymbol, EnabledColor)
                .Build()
            : new SeStringBuilder()
                .AddText(MusicSymbol)
                .Build();
        this.entry.Tooltip = new SeStringBuilder()
            .AddText(enabled
                ? "XIV.fm listening cards: Visible\nLeft-click to hide cards."
                : "XIV.fm listening cards: Hidden\nLeft-click to show cards.")
            .AddText("\nRight-click to open settings.")
            .Build();
    }

    public void Dispose()
    {
        this.entry.OnClick -= this.OnClick;
        this.entry.Remove();
    }

    private void OnClick(DtrInteractionEvent interaction)
    {
        switch (interaction.ClickType)
        {
            case MouseClickType.Left:
                this.toggleOverlay();
                this.Refresh();
                break;

            case MouseClickType.Right:
                this.openSettings();
                break;
        }
    }
}
