using System.ComponentModel.DataAnnotations;
using ComputerUseAgent.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ComputerUseAgent.Web.Pages;

public sealed class IndexModel : PageModel
{
    private readonly AgentApiClient _apiClient;

    public IndexModel(AgentApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    [BindProperty]
    [Required]
    [Display(Name = "Task prompt")]
    public string Prompt { get; set; } = string.Empty;

    public string? ErrorMessage { get; private set; }

    public IReadOnlyList<string> Examples { get; } =
    [
        "Create a Python script named hello.py that prints \"Hello from the sandbox\". Run it. Save the output to result.txt. Then finish with a short summary.",
        "Create a Python script named calc.py with a deliberate syntax error. Run it. Inspect the error. Fix the script so it prints the sum of 2 and 3. Run it again. Save the final output to result.txt. Then finish.",
        "Create a CSV file named sales.csv with a few rows of sample sales data. Write a Python script named summarize.py that reads the CSV and writes a markdown report named report.md with total sales and row count. Run the script. Then finish."
    ];

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            var summary = await _apiClient.CreateSessionAsync(Prompt, cancellationToken);
            return RedirectToPage("/Sessions/Details", new { id = summary.Id });
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return Page();
        }
    }
}
