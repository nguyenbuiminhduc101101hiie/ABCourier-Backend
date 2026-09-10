using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace CourierApi.Hubs;

[Authorize]
public sealed class BookingNotificationHub : Hub
{
    public const string CompanyGroup = "company";

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, CompanyGroup);
        await base.OnConnectedAsync();
    }
}
