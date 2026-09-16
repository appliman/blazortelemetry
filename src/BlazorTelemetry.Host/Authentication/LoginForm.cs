using System.ComponentModel.DataAnnotations;

namespace Blazor2fa;

public class LoginForm
{
    [Required(ErrorMessage = "Email is required")]
    public string? Email { get; set; }
    public string Step { get; set; } = "Email";
    public string? Digicode { get; set; }
}
