using AniSprinkles.Utilities;
using Microsoft.Maui.Controls.Handlers.Compatibility;
using Microsoft.Maui.Controls.Platform.Compatibility;

namespace AniSprinkles.Platforms.Android;

/// <summary>
/// The legacy Android Shell renderer (#177) with one addition: a tap on the bottom tab that is
/// already selected goes to <see cref="TabReselect"/> (#200). Stock Shell ignores it.
/// </summary>
/// <remarks>
/// The hook is MAUI's own: <c>ShellItemRenderer.OnItemSelected</c> calls the empty virtual
/// <c>OnTabReselected(ShellSection)</c> when the tapped item is already checked. MAUI 11's
/// handler-based <c>ShellItemHandler</c> exposes a virtual of the same name and signature, so
/// lifting the #177 pin means moving this override to a <c>ShellItemHandler</c> subclass, and
/// re-checking there whether programmatic selection also reaches it (see the guard below);
/// <see cref="TabReselect"/> itself does not change.
/// </remarks>
public sealed class TabReselectShellRenderer : ShellRenderer
{
    protected override IShellItemRenderer CreateShellItemRenderer(ShellItem shellItem)
        => new TabReselectShellItemRenderer(this);

    private sealed class TabReselectShellItemRenderer(IShellContext shellContext) : ShellItemRenderer(shellContext)
    {
        // MAUI's menu setup reassigns BottomNavigationView.SelectedItemId to the current tab, and the
        // view reports that as a selection of an already-checked item, so it arrives here looking exactly
        // like a re-tap. Setup runs at startup and whenever any tab's Icon or Title changes, and AppShell
        // swaps the Regular/Filled icons on every tab switch, so without this guard coming back to a tab
        // with pages pushed would pop it. The assignment is synchronous inside base.SetupMenu (only icon
        // loading is awaited), so a flag around the call covers it.
        private bool _settingUpMenu;

        protected override void SetupMenu(global::Android.Views.IMenu menu, int maxBottomItems, ShellItem shellItem)
        {
            _settingUpMenu = true;
            try
            {
                base.SetupMenu(menu, maxBottomItems, shellItem);
            }
            finally
            {
                _settingUpMenu = false;
            }
        }

        protected override void OnTabReselected(ShellSection shellSection)
        {
            base.OnTabReselected(shellSection);
            if (!_settingUpMenu)
            {
                TabReselect.Handle(shellSection);
            }
        }
    }
}
