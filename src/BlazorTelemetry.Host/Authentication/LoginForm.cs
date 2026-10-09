using System.ComponentModel.DataAnnotations;

namespace BlazorTelemetry.Host.Authentication;

public class LoginForm
{
    [Required(ErrorMessage = "Identifier is required")]
    public string? Identifier { get; set; }
    public string? Digicode { get; set; }
}
