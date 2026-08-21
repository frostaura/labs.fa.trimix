using System.ComponentModel;
using Trimix.App.Controls;
using Trimix.Core;
using Trimix.Core.ViewModels;

namespace Trimix.App.Pages;

/// <summary>
/// Providers (prototype S5). Binds the registry-driven <see cref="ConnectedAccountsViewModel"/>.
/// The optional <c>nudge</c> query parameter (an unlinked "Sync now" tap routed here) runs the
/// design's <c>connectNudge</c> — an animated red pulse on the unlinked Connect buttons — via the
/// VM's nudge state. Rows enter with the design's <c>rowIn</c> stagger.
/// </summary>
[QueryProperty(nameof(Nudge), Routes.Params.Nudge)]
public partial class ConnectedAccountsPage : TrimixPage
{
    private const string NudgeAnimationHandle = "trimix.connectNudge";

    private readonly ConnectedAccountsViewModel _viewModel;
    private bool _nudgeRequested;

    public ConnectedAccountsPage(ConnectedAccountsViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        BindingContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>"1" when arrival should pulse the Connect buttons (set by Shell query).</summary>
    public string? Nudge
    {
        set => _nudgeRequested = value is "1";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadCommand.ExecuteAsync(null);

        // rowIn: staggered list-row entrance (design: .4s ease, translateX −8 → 0 per row).
        _ = Motion.RowStaggerAsync([.. RowsHost.Children.OfType<VisualElement>()]);

        if (_nudgeRequested)
        {
            _nudgeRequested = false;
            await _viewModel.BeginNudgeAsync();
        }
    }

    /// <summary>The connected list finished a native drag-reorder — persist the new merge priority. The
    /// CollectionView has already moved the item in the bound ObservableCollection.</summary>
    private void OnConnectedReordered(object? sender, EventArgs e) => _viewModel.PersistConnectedOrder();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ConnectedAccountsViewModel.IsNudging))
        {
            return;
        }

        if (_viewModel.IsNudging)
        {
            StartConnectNudge();
        }
        else
        {
            EndConnectNudge();
        }
    }

    /// <summary>
    /// The design's <c>connectNudge 1.1s ease</c>: border + text of every unlinked Connect button
    /// pulse mint → red (peaking through 30–60% of the cycle) → mint. Reduced motion: a plain red
    /// hold for the nudge window instead of a pulse (state is still signalled, nothing moves).
    /// </summary>
    private void StartConnectNudge()
    {
        var buttons = UnlinkedConnectButtons();
        if (buttons.Count == 0)
        {
            return;
        }

        var mint = ThemeTokens.Get("Sonar");
        var red = ThemeTokens.Get("Alert");

        if (MotionSettings.IsReducedMotionEnabled)
        {
            foreach (var button in buttons)
            {
                button.TextColor = red;
                button.BorderColor = red;
            }

            return; // EndConnectNudge (IsNudging → false) restores the themed mint.
        }

        this.AbortAnimation(NudgeAnimationHandle);
        var animation = new Animation(t =>
        {
            var color = NudgeColor(t, mint, red);
            foreach (var button in buttons)
            {
                button.TextColor = color;
                button.BorderColor = color;
            }
        });
        animation.Commit(
            this, NudgeAnimationHandle,
            rate: 16,
            length: (uint)Math.Max(1, _viewModel.NudgeDurationMs),
            Easing.Linear,
            finished: (_, _) => RestoreConnectButtonColors());
    }

    private void EndConnectNudge()
    {
        this.AbortAnimation(NudgeAnimationHandle);
        RestoreConnectButtonColors();
    }

    /// <summary>Clears the animated values so the XAML triggers (theme-aware mint) reassert.</summary>
    private void RestoreConnectButtonColors()
    {
        foreach (var button in UnlinkedConnectButtons())
        {
            button.ClearValue(Button.TextColorProperty);
            button.ClearValue(Button.BorderColorProperty);
        }
    }

    /// <summary>connectNudge keyframes: 0/100% mint, 30–60% red, eased linearly between.</summary>
    private static Color NudgeColor(double t, Color mint, Color red) => t switch
    {
        < 0.3 => LerpColor(mint, red, t / 0.3),
        <= 0.6 => red,
        _ => LerpColor(red, mint, (t - 0.6) / 0.4),
    };

    private static Color LerpColor(Color from, Color to, double t)
    {
        var f = (float)Math.Clamp(t, 0, 1);
        return new Color(
            from.Red + ((to.Red - from.Red) * f),
            from.Green + ((to.Green - from.Green) * f),
            from.Blue + ((to.Blue - from.Blue) * f),
            from.Alpha + ((to.Alpha - from.Alpha) * f));
    }

    /// <summary>The Connect CTAs of the currently-unlinked rows (the nudge targets).</summary>
    private List<Button> UnlinkedConnectButtons() =>
        [.. RowsHost.Children
            .OfType<Border>()
            .Select(border => border.Content)
            .OfType<Grid>()
            .SelectMany(grid => grid.Children.OfType<Button>())
            .Where(button => button.ClassId == "ConnectCta"
                && button.BindingContext is ProviderRow { IsLinked: false })];
}
