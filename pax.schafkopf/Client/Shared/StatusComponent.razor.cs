using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using pax.schafkopf.Client.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace pax.schafkopf.Client.Shared
{
    public partial class StatusComponent : ComponentBase
    {
        [Inject]
        protected IJSRuntime _js { get; set; }
        [Inject]
        protected ILogger<StatusComponent> logger { get; set; }

        [CascadingParameter]
        public ClientTable table { get; set; }

        private static Action<bool> action;
        private static Action<bool> hubaction;

        protected override void OnInitialized()
        {
            action = UpdateStatus;
            hubaction = UpdateDataStatus;
            base.OnInitialized();
        }



        void UpdateDataStatus(bool status)
        {
            InvokeAsync(() => StateHasChanged());
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            //if (firstRender)
            //{
            //    try
            //    {
            //        await Task.Delay(250);
            //        UpdateStatus(await _js.InvokeAsync<bool>("CheckOnlineStatus"));
            //    } catch (Exception e)
            //    {
            //        logger.LogError(e.Message);
            //    }
            //}
        }

        private async void UpdateStatus(bool status)
        {
            logger.LogInformation($"set status to {status}");
            await InvokeAsync(() => StateHasChanged());
        }

        [JSInvokable]
        public static void SetOnlineStatus(bool status)
        {
            action.Invoke(status);
        }

        public void Dispose()
        {
        }
    }
}
