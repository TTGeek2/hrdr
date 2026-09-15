using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Hrdr.Pages;

public class IndexModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Items/Index");
}
