using System.Runtime.CompilerServices;
using AniSprinkles.Utilities;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.Views;

public partial class StudioDetailsLoadedContentView : ContentView
{
    private readonly ILogger<StudioDetailsLoadedContentView>? _logger;
    private readonly int _viewId;

    public StudioDetailsLoadedContentView()
    {
        InitializeComponent();

        _viewId = RuntimeHelpers.GetHashCode(this);
        try
        {
            _logger = ServiceProviderHelper.GetServiceProvider()
                .GetService<ILoggerFactory>()?.CreateLogger<StudioDetailsLoadedContentView>();
        }
        catch (InvalidOperationException)
        {
        }

        _logger?.LogInformation("LOADEDVIEW StudioDetails[#{ViewId:X}] constructed", _viewId);
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        _logger?.LogInformation(
            "LOADEDVIEW StudioDetails[#{ViewId:X}] OnHandlerChanged (handler={HasHandler})",
            _viewId, Handler is not null);
    }
}
