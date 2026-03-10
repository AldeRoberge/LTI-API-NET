using Microsoft.AspNetCore.Components;

namespace ADG.LTI.Components.Pages;

public partial class Home : ComponentBase
{
    [Inject] private NavigationManager NavigationManager { get; set; } = null!;
    [Inject] private IWebHostEnvironment WebHostEnvironment { get; set; } = null!;

    public string Version => "1.0.0";
    public string Host => NavigationManager.Uri;
    public string ServerType => "ADG LTI API";
    public string Environment => WebHostEnvironment.EnvironmentName;
    public bool ShowScalar => !WebHostEnvironment.IsProduction();
}