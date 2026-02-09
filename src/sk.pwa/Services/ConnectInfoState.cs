
using Microsoft.JSInterop;
using sk.shared;

namespace sk.pwa.Services;

public sealed class ConnectInfoState
{
    private readonly IJSRuntime _js;

    public ConnectInfo? Current { get; private set; }

    public ConnectInfoState(IJSRuntime js)
    {
        _js = js;
    }

    public async Task LoadAsync()
    {
        Current = await _js.InvokeAsync<ConnectInfo?>("getConnectInfo");
    }

    public async Task SaveAsync(ConnectInfo info)
    {
        Current = info;
        await _js.InvokeVoidAsync("storeConnectInfo", info);
    }

    public async Task UpdateAsync(Action<ConnectInfo> update)
    {
        if (Current == null)
            return;

        update(Current);
        await _js.InvokeVoidAsync("storeConnectInfo", Current);
    }
}
