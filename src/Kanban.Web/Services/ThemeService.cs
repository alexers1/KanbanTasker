using Microsoft.JSInterop;

namespace Kanban.Web.Services;

/// <summary>
/// Light/dark theme, mirroring the WinUI client's two theme dictionaries. The choice is
/// persisted in localStorage and applied as data-theme on the document element; the glass
/// CSS variables (glass.css) switch off that attribute. Dark is the default, matching WinUI.
/// </summary>
public class ThemeService
{
    private readonly IJSRuntime _js;
    public string Current { get; private set; } = "dark";

    public event Action? Changed;

    public ThemeService(IJSRuntime js) => _js = js;

    /// <summary>Read the persisted theme (called once after first render).</summary>
    public async Task InitAsync()
    {
        Current = await _js.InvokeAsync<string>("kanbanTheme.get");
        Changed?.Invoke();
    }

    public async Task ToggleAsync()
    {
        Current = Current == "dark" ? "light" : "dark";
        await _js.InvokeVoidAsync("kanbanTheme.set", Current);
        Changed?.Invoke();
    }
}
