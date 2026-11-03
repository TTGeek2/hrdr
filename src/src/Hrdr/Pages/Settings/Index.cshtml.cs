using Hrdr.Core.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Hrdr.Pages.Settings;

public class IndexModel(AgentLaunchSettingsService settings, HerdrAgentLauncher launcher) : PageModel
{
    public string? ErrorMessage { get; private set; }

    [BindProperty]
    public string Message { get; set; } = "";

    [BindProperty]
    public string Kind { get; set; } = AgentLaunchSettingsService.DefaultKind;

    [BindProperty]
    public string PaneName { get; set; } = AgentLaunchSettingsService.DefaultPaneName;

    public async Task OnGetAsync(CancellationToken ct)
    {
        var current = await settings.GetAsync(ct);
        Message = current.Message;
        Kind = current.Kind;
        PaneName = current.PaneName;
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
    {
        try
        {
            await settings.SaveAsync(new AgentLaunchSettings(Message, Kind, PaneName), ct);
            TempData["Status"] = "Agent settings saved.";
            return RedirectToPage();
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
            return Page();
        }
    }

    public async Task<IActionResult> OnPostStartAsync(CancellationToken ct)
    {
        try
        {
            AgentLaunchSettings current;
            if (Request.HasFormContentType && Request.Form.ContainsKey("Message"))
            {
                current = new AgentLaunchSettings(Message, Kind, PaneName);
                await settings.SaveAsync(current, ct);
            }
            else
            {
                current = await settings.GetAsync(ct);
            }

            var started = await launcher.StartAsync(current, ct);
            TempData["Status"] = started.CreatedPane
                ? $"Created Herdr pane {started.PaneName} and sent the message."
                : $"Sent the message to Herdr pane {started.PaneName}.";
        }
        catch (Exception ex) when (ex is ArgumentException or HerdrLaunchException)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectAfterPost();
    }

    private IActionResult RedirectAfterPost()
    {
        var referer = Request.Headers.Referer.ToString();
        if (Uri.TryCreate(referer, UriKind.Absolute, out var uri)
            && HostMatches(uri)
            && uri.AbsolutePath.StartsWith("/", StringComparison.Ordinal))
            return Redirect(uri.PathAndQuery);

        return RedirectToPage();
    }

    private bool HostMatches(Uri uri)
    {
        if (!string.Equals(uri.Host, Request.Host.Host, StringComparison.OrdinalIgnoreCase))
            return false;

        var requestPort = Request.Host.Port ?? (Request.IsHttps ? 443 : 80);
        return uri.Port == requestPort;
    }
}
