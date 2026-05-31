using System.Runtime.CompilerServices;
using AniSprinkles.Utilities;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.Views;

public partial class CharacterDetailsLoadedContentView : ContentView
{
    private readonly ILogger<CharacterDetailsLoadedContentView>? _logger;
    private readonly int _viewId;

    public CharacterDetailsLoadedContentView()
    {
        InitializeComponent();

        _viewId = RuntimeHelpers.GetHashCode(this);
        try
        {
            _logger = ServiceProviderHelper.GetServiceProvider()
                .GetService<ILoggerFactory>()?.CreateLogger<CharacterDetailsLoadedContentView>();
        }
        catch (InvalidOperationException)
        {
        }

        _logger?.LogInformation("LOADEDVIEW CharacterDetails[#{ViewId:X}] constructed", _viewId);
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        _logger?.LogInformation(
            "LOADEDVIEW CharacterDetails[#{ViewId:X}] OnHandlerChanged (handler={HasHandler})",
            _viewId, Handler is not null);
    }
}
