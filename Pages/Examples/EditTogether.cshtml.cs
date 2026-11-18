using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;

namespace SurveyJsAspNetCore.Pages.Examples;

/// <summary>
/// Edit Forms Together. ?form picks the shared form (its collaboration room); the page writes it as
/// window.SURVEYJS_PAGE.formId. A form ID that isn't in the picker opens a new form with that ID.
/// </summary>
public partial class EditTogetherModel : ExampleModel
{
    public string FormId { get; private set; } = "support-team";

    public IActionResult OnGet()
    {
        // Several ?form values join with a comma, which the pattern rejects
        FormId = (string?)Request.Query["form"] ?? "support-team";
        return FormIdPattern().IsMatch(FormId) ? Page() : BadRequest("Invalid form ID");
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex FormIdPattern();
}
