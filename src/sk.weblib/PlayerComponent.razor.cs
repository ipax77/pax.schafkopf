using Microsoft.AspNetCore.Components;
using sk.shared;

namespace sk.weblib;

public partial class PlayerComponent
{
    [Parameter, EditorRequired]
    public TablePlayer TablePlayer { get; set; } = default!;


    [Parameter]
    public EventCallback<Card> OnCardPlayed { get; set; }
}