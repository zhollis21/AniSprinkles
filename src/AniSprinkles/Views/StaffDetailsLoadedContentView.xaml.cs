using System.Runtime.CompilerServices;
using AniSprinkles.Utilities;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.Views;

public partial class StaffDetailsLoadedContentView : ContentView
{
    private readonly ILogger<StaffDetailsLoadedContentView>? _logger;
    private readonly int _viewId;

    public StaffDetailsLoadedContentView()
    {
        InitializeComponent();

        _viewId = RuntimeHelpers.GetHashCode(this);
        try
        {
            _logger = ServiceProviderHelper.GetServiceProvider()
                .GetService<ILoggerFactory>()?.CreateLogger<StaffDetailsLoadedContentView>();
        }
        catch (InvalidOperationException)
        {
        }

        _logger?.LogInformation("LOADEDVIEW StaffDetails[#{ViewId:X}] constructed", _viewId);
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        _logger?.LogInformation(
            "LOADEDVIEW StaffDetails[#{ViewId:X}] OnHandlerChanged (handler={HasHandler})",
            _viewId, Handler is not null);
    }
}
