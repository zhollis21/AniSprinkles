using CommunityToolkit.Maui.Views;

namespace AniSprinkles.Views;

/// <summary>
/// The send-diagnostics sheet (#112): what is about to be collected, and an optional box for what the
/// user was doing.
/// <para>
/// Closes with a (possibly empty) <see cref="string"/> for send, and with the non-string
/// <see cref="Cancelled"/> sentinel for cancel — a dismiss by tapping outside surfaces as
/// <c>null</c>, which the caller treats identically. So the contract is "a string means send";
/// anything else means don't. That distinction is what lets the caller tell "the user backed out"
/// from "the user sent it without a note", which are different answers where only one may put
/// anything on the wire.
/// </para>
/// </summary>
public partial class DiagnosticsReportPopup : Popup<object>
{
    /// <summary>
    /// Closed with this rather than <c>null</c> when the user backs out.
    /// <para>
    /// <c>Popup&lt;object&gt;</c> is not nullable-annotated, so the sibling popups reach for
    /// <c>CloseAsync(null!)</c>. A sentinel says the same thing without a suppression, and reads the
    /// same at the call site: the caller only ever asks "is this a string", and this deliberately
    /// is not one.
    /// </para>
    /// </summary>
    public static readonly object Cancelled = new();

    public DiagnosticsReportPopup(string summary)
    {
        InitializeComponent();

        SummaryLabel.Text = summary;
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        // Indistinguishable from a dismiss-by-tapping-outside on purpose. Both mean the same thing to
        // the caller: send nothing, and say nothing about having sent it.
        await CloseAsync(Cancelled);
    }

    private async void OnSendClicked(object? sender, EventArgs e)
    {
        // Empty string rather than null when the box is untouched — the note is optional, and
        // requiring text would be friction for someone who only wants the log attached.
        await CloseAsync(DescriptionEditor.Text ?? string.Empty);
    }
}
